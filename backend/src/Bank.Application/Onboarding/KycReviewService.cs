using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Customers;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Onboarding;

public record KycQueueItem(Guid CustomerId, string? FullName, string Email, string? MaskedCpf, decimal? MonthlyIncome, DateTime? SubmittedAt, KycStatus Status);

public record KycDocumentResponse(Guid Id, DocumentType Type, string ContentType, long SizeBytes, DateTime UploadedAt);

public record KycDetailResponse(
    Guid CustomerId, string? FullName, string Email, string? Cpf, DateOnly? BirthDate, string? Phone, Address? Address,
    decimal? MonthlyIncome, string? Occupation, KycStatus Status, string? RejectionReason, DateTime? SubmittedAt,
    IReadOnlyList<KycDocumentResponse> Documents);

public record RejectKycRequest(string Reason);

public record StoredFile(Stream Content, string ContentType, string FileName);

public class RejectKycRequestValidator : AbstractValidator<RejectKycRequest>
{
    public RejectKycRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300).WithName("Motivo");
}

/// <summary>Back-office KYC queue: analysts review documents and approve or reject account openings.</summary>
public class KycReviewService(
    IBankDbContext db,
    IFileStorage storage,
    IAuditLog audit,
    TimeProvider clock,
    IValidator<RejectKycRequest> rejectValidator)
{
    public async Task<PagedResult<KycQueueItem>> ListAsync(KycStatus status, int page, int pageSize, CancellationToken ct)
    {
        var result = await (
                from c in db.Customers
                join u in db.Users on c.UserId equals u.Id
                where c.KycStatus == status
                orderby c.SubmittedAt
                select new KycQueueItem(c.Id, c.FullName, u.Email, c.Cpf, c.MonthlyIncome, c.SubmittedAt, c.KycStatus))
            .ToPagedAsync(page, pageSize, ct);
        // The queue only shows masked CPFs; the full one is in the audited detail view.
        return result with { Items = result.Items.Select(i => i with { MaskedCpf = i.MaskedCpf is null ? null : Cpf.Mask(i.MaskedCpf) }).ToList() };
    }

    public async Task<KycDetailResponse> GetAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await db.Customers.Include(c => c.Documents).AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException("Cadastro não encontrado.");
        var email = await db.Users.Where(u => u.Id == customer.UserId).Select(u => u.Email).SingleAsync(ct);
        audit.Record("kyc.viewed", nameof(Customer), customer.Id);
        await db.SaveChangesAsync(ct);
        return new KycDetailResponse(
            customer.Id, customer.FullName, email, customer.Cpf is null ? null : Cpf.Format(customer.Cpf), customer.BirthDate, customer.Phone,
            customer.Address, customer.MonthlyIncome, customer.Occupation, customer.KycStatus, customer.KycRejectionReason, customer.SubmittedAt,
            customer.Documents.Select(d => new KycDocumentResponse(d.Id, d.Type, d.ContentType, d.SizeBytes, d.UploadedAt)).ToList());
    }

    public async Task<StoredFile> OpenDocumentAsync(Guid customerId, Guid documentId, CancellationToken ct)
    {
        var customer = await db.Customers.Include(c => c.Documents).AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException("Cadastro não encontrado.");
        var document = customer.Documents.SingleOrDefault(d => d.Id == documentId) ?? throw new NotFoundException("Documento não encontrado.");
        audit.Record("kyc.document_viewed", nameof(Customer), customer.Id, after: new { DocumentId = document.Id, document.Type });
        await db.SaveChangesAsync(ct);
        var extension = document.StoragePath[(document.StoragePath.LastIndexOf('.') + 1)..];
        return new StoredFile(await storage.OpenReadAsync(document.StoragePath, ct), document.ContentType, $"{document.Type}.{extension}");
    }

    /// <summary>Approves the customer and activates the account with the tier implied by the declared income.</summary>
    public async Task ApproveAsync(Guid customerId, Guid? reviewerId, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct) ?? throw new NotFoundException("Cadastro não encontrado.");
        var account = await db.Accounts.SingleAsync(a => a.UserId == customer.UserId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        customer.Approve(reviewerId, now);
        var tier = Account.TierForIncome(customer.MonthlyIncome!.Value);
        account.Activate(tier, now);
        audit.Record("kyc.approved", nameof(Customer), customer.Id, after: new { Tier = tier, Automatic = reviewerId is null },
            actorUserId: reviewerId, bySystem: reviewerId is null);
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid customerId, Guid reviewerId, RejectKycRequest req, CancellationToken ct)
    {
        await rejectValidator.ValidateAndThrowAsync(req, ct);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct) ?? throw new NotFoundException("Cadastro não encontrado.");
        customer.Reject(reviewerId, req.Reason.Trim(), clock.GetUtcNow().UtcDateTime);
        audit.Record("kyc.rejected", nameof(Customer), customer.Id, after: new { req.Reason });
        await db.SaveChangesAsync(ct);
    }
}
