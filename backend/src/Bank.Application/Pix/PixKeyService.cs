using Bank.Application.Abstractions;
using Bank.Application.Auditing;
using Bank.Application.Common;
using Bank.Domain.Common;
using Bank.Domain.Pix;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Pix;

/// <param name="Value">Ignored for random keys; for CPF it must be the holder's own CPF.</param>
public record RegisterPixKeyRequest(PixKeyType Type, string? Value);

public record PixKeyResponse(Guid Id, PixKeyType Type, string Value, DateTime CreatedAt);

public record AddContactRequest(string Nickname, string Key);

public record ContactResponse(Guid Id, string Nickname, PixKeyType KeyType, string MaskedKey, string KeyValue);

public class RegisterPixKeyRequestValidator : AbstractValidator<RegisterPixKeyRequest>
{
    public RegisterPixKeyRequestValidator() => RuleFor(x => x.Value).MaximumLength(100).WithName("Chave");
}

public class AddContactRequestValidator : AbstractValidator<AddContactRequest>
{
    public AddContactRequestValidator()
    {
        RuleFor(x => x.Nickname).NotEmpty().MaximumLength(50).WithName("Apelido");
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100).WithName("Chave");
    }
}

public class PixKeyService(
    IBankDbContext db,
    ICurrentUser currentUser,
    PixDirectory directory,
    IAuditLog audit,
    TimeProvider clock,
    IValidator<RegisterPixKeyRequest> keyValidator,
    IValidator<AddContactRequest> contactValidator)
{
    public async Task<IReadOnlyList<PixKeyResponse>> ListAsync(CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        return await db.PixKeys.Where(k => k.AccountId == accountId).OrderBy(k => k.CreatedAt)
            .Select(k => new PixKeyResponse(k.Id, k.Type, k.Value, k.CreatedAt)).ToListAsync(ct);
    }

    public async Task<PixKeyResponse> RegisterAsync(RegisterPixKeyRequest req, CancellationToken ct)
    {
        await keyValidator.ValidateAndThrowAsync(req, ct);
        var userId = currentUser.UserId;
        var accountId = await MyAccountIdAsync(ct);
        var ownerCpf = await db.Customers.Where(c => c.UserId == userId).Select(c => c.Cpf).SingleAsync(ct)
            ?? throw new DomainException("pix.no_cpf", "Complete seu cadastro antes de criar chaves PIX.");
        var count = await db.PixKeys.CountAsync(k => k.AccountId == accountId, ct);

        // shortcut: e-mail and phone keys are not confirmed with a one-time code, as a real PSP would require.
        var key = PixKey.Register(accountId, req.Type, req.Value, ownerCpf, count, clock.GetUtcNow().UtcDateTime);
        if (await db.PixKeys.AnyAsync(k => k.Value == key.Value, ct))
            throw new ConflictException("Esta chave já está cadastrada.");
        db.PixKeys.Add(key);
        audit.Record("pix.key_registered", nameof(PixKey), key.Id, after: new { key.Type, Value = PixKeys.Mask(key.Type, key.Value) });
        await db.SaveChangesAsync(ct);
        return new PixKeyResponse(key.Id, key.Type, key.Value, key.CreatedAt);
    }

    public async Task DeleteAsync(Guid keyId, CancellationToken ct)
    {
        var accountId = await MyAccountIdAsync(ct);
        var key = await db.PixKeys.SingleOrDefaultAsync(k => k.Id == keyId && k.AccountId == accountId, ct)
            ?? throw new NotFoundException("Chave não encontrada.");
        db.PixKeys.Remove(key);
        audit.Record("pix.key_deleted", nameof(PixKey), key.Id, before: new { key.Type, Value = PixKeys.Mask(key.Type, key.Value) });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ContactResponse>> ListContactsAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var contacts = await db.PixContacts.AsNoTracking().Where(c => c.OwnerUserId == userId).OrderBy(c => c.Nickname).ToListAsync(ct);
        return contacts.Select(c => new ContactResponse(c.Id, c.Nickname, c.KeyType, PixKeys.Mask(c.KeyType, c.KeyValue), c.KeyValue)).ToList();
    }

    public async Task<ContactResponse> AddContactAsync(AddContactRequest req, CancellationToken ct)
    {
        await contactValidator.ValidateAndThrowAsync(req, ct);
        var userId = currentUser.UserId;
        var recipient = await directory.FindAsync(req.Key, ct) ?? throw new NotFoundException("Chave PIX não encontrada.");
        if (await db.PixContacts.AnyAsync(c => c.OwnerUserId == userId && c.KeyValue == recipient.KeyValue, ct))
            throw new ConflictException("Este contato já está nos seus favoritos.");
        var contact = PixContact.Create(userId, req.Nickname, recipient.KeyType, recipient.KeyValue, clock.GetUtcNow().UtcDateTime);
        db.PixContacts.Add(contact);
        await db.SaveChangesAsync(ct);
        return new ContactResponse(contact.Id, contact.Nickname, contact.KeyType, PixKeys.Mask(contact.KeyType, contact.KeyValue), contact.KeyValue);
    }

    public async Task DeleteContactAsync(Guid contactId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var deleted = await db.PixContacts.Where(c => c.Id == contactId && c.OwnerUserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Contato não encontrado.");
    }

    private async Task<Guid> MyAccountIdAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return await db.Accounts.Where(a => a.UserId == userId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedException("Conta não encontrada.");
    }
}
