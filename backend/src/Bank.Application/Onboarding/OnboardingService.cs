using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Customers;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Onboarding;

public record PersonalDataRequest(string FullName, string Cpf, DateOnly BirthDate, string Phone);

public record AddressRequest(string Cep, string Street, string Number, string? Complement, string Neighborhood, string City, string State);

public record FinancialProfileRequest(decimal MonthlyIncome, string Occupation);

public record OnboardingStatusResponse(
    OnboardingStep NextStep,
    KycStatus KycStatus,
    AccountStatus AccountStatus,
    AccountTier AccountTier,
    string? RejectionReason,
    string? FullName,
    string? MaskedCpf,
    IReadOnlyList<DocumentType> UploadedDocuments);

public record KycOptions(bool AutoApprove = false);

public class PersonalDataRequestValidator : AbstractValidator<PersonalDataRequest>
{
    public PersonalDataRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().Length(5, 100)
            .Must(n => n.Trim().Contains(' ')).WithMessage("Informe nome e sobrenome.").WithName("Nome completo");
        RuleFor(x => x.Cpf).Must(Cpf.IsValid).WithMessage("CPF inválido.");
        RuleFor(x => x.Phone).Must(p => Phone.TryNormalize(p) is not null).WithMessage("Telefone inválido. Use DDD + número.");
        RuleFor(x => x.BirthDate).GreaterThan(new DateOnly(1900, 1, 1)).WithName("Data de nascimento");
    }
}

public class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.Cep).Matches("^[0-9]{5}-?[0-9]{3}$").WithMessage("CEP inválido.");
        RuleFor(x => x.Street).NotEmpty().MaximumLength(150).WithName("Logradouro");
        RuleFor(x => x.Number).NotEmpty().MaximumLength(20).WithName("Número");
        RuleFor(x => x.Complement).MaximumLength(100).WithName("Complemento");
        RuleFor(x => x.Neighborhood).NotEmpty().MaximumLength(100).WithName("Bairro");
        RuleFor(x => x.City).NotEmpty().MaximumLength(100).WithName("Cidade");
        RuleFor(x => x.State).NotEmpty().Matches("^[A-Za-z]{2}$").WithMessage("Use a sigla do estado (ex.: SP).");
    }
}

public class FinancialProfileRequestValidator : AbstractValidator<FinancialProfileRequest>
{
    public FinancialProfileRequestValidator()
    {
        RuleFor(x => x.MonthlyIncome).MoneyAmount(max: 10_000_000).WithName("Renda mensal");
        RuleFor(x => x.Occupation).NotEmpty().MaximumLength(100).WithName("Profissão");
    }
}

/// <summary>Step-by-step account opening (KYC). Steps can be saved in any order until submission.</summary>
public class OnboardingService(
    IBankDbContext db,
    ICurrentUser currentUser,
    ICepLookup cepLookup,
    IFileStorage storage,
    IAuditLog audit,
    KycReviewService review,
    KycOptions options,
    TimeProvider clock,
    IValidator<PersonalDataRequest> personalValidator,
    IValidator<AddressRequest> addressValidator,
    IValidator<FinancialProfileRequest> financialValidator)
{
    public const long MaxUploadBytes = 5 * 1024 * 1024;

    public async Task<OnboardingStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var customer = await db.Customers.Include(c => c.Documents).AsNoTracking().SingleAsync(c => c.UserId == userId, ct);
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == userId, ct);
        var hasPin = await db.Users.Where(u => u.Id == userId).Select(u => u.TransactionPinHash != null).SingleAsync(ct);
        return new OnboardingStatusResponse(
            customer.NextStep(hasPin), customer.KycStatus, account.Status, account.Tier, customer.KycRejectionReason,
            customer.FullName, customer.Cpf is null ? null : Cpf.Mask(customer.Cpf),
            customer.Documents.Select(d => d.Type).ToList());
    }

    public async Task SavePersonalDataAsync(PersonalDataRequest req, CancellationToken ct)
    {
        await personalValidator.ValidateAndThrowAsync(req, ct);
        var customer = await MyCustomerAsync(ct);
        var cpf = Cpf.Normalize(req.Cpf);
        if (await db.Customers.AnyAsync(c => c.Cpf == cpf && c.Id != customer.Id, ct))
            throw new ConflictException("CPF já cadastrado em outra conta.");
        var today = DateOnly.FromDateTime(BankTime.ToLocal(Now()));
        customer.SetPersonalData(req.FullName, cpf, req.BirthDate, Phone.TryNormalize(req.Phone)!, today);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CepAddress> LookupCepAsync(string cep, CancellationToken ct)
    {
        var digits = new string(cep.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 8) throw new DomainException("cep.invalid", "CEP inválido.");
        return await cepLookup.LookupAsync(digits, ct)
            ?? throw new NotFoundException("CEP não encontrado. Preencha o endereço manualmente.");
    }

    public async Task SaveAddressAsync(AddressRequest req, CancellationToken ct)
    {
        await addressValidator.ValidateAndThrowAsync(req, ct);
        var customer = await MyCustomerAsync(ct);
        customer.SetAddress(new Address(
            new string(req.Cep.Where(char.IsAsciiDigit).ToArray()), req.Street.Trim(), req.Number.Trim(), req.Complement?.Trim(),
            req.Neighborhood.Trim(), req.City.Trim(), req.State.Trim().ToUpperInvariant()));
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveFinancialProfileAsync(FinancialProfileRequest req, CancellationToken ct)
    {
        await financialValidator.ValidateAndThrowAsync(req, ct);
        var customer = await MyCustomerAsync(ct);
        customer.SetFinancialProfile(req.MonthlyIncome, req.Occupation);
        await db.SaveChangesAsync(ct);
    }

    public async Task UploadDocumentAsync(DocumentType type, Stream content, long length, CancellationToken ct)
    {
        if (length is <= 0 or > MaxUploadBytes)
            throw new DomainException("upload.size", "O arquivo deve ter até 5 MB.");

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxUploadBytes) throw new DomainException("upload.size", "O arquivo deve ter até 5 MB.");
        var file = FileSignatures.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        var allowed = type == DocumentType.Selfie ? ["image/jpeg", "image/png"] : new[] { "image/jpeg", "image/png", "application/pdf" };
        if (file is null || !allowed.Contains(file.ContentType))
            throw new DomainException("upload.type", type == DocumentType.Selfie
                ? "A selfie deve ser uma imagem JPG ou PNG."
                : "O documento deve ser uma imagem JPG/PNG ou um PDF.");

        var customer = await MyCustomerAsync(ct);
        var path = $"kyc/{customer.Id}/{type.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.{file.Extension}";
        buffer.Position = 0;
        await storage.SaveAsync(path, buffer, ct);
        customer.AttachDocument(type, path, file.ContentType, buffer.Length, Now());
        audit.Record("onboarding.document_uploaded", nameof(Customer), customer.Id, after: new { Type = type, file.ContentType, Size = buffer.Length });
        await db.SaveChangesAsync(ct);
    }

    public async Task<OnboardingStatusResponse> SubmitAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var customer = await MyCustomerAsync(ct);
        var hasPin = await db.Users.Where(u => u.Id == userId).Select(u => u.TransactionPinHash != null).SingleAsync(ct);
        customer.Submit(hasPin, Now());
        audit.Record("onboarding.submitted", nameof(Customer), customer.Id);
        await db.SaveChangesAsync(ct);

        // Demo mode: an automatic "analyst" approves right away so the app can be tried end to end.
        if (options.AutoApprove) await review.ApproveAsync(customer.Id, reviewerId: null, ct);
        return await GetStatusAsync(ct);
    }

    private async Task<Customer> MyCustomerAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Customers.Include(c => c.Documents).SingleOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new UnauthorizedException("Cadastro não encontrado.");
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
