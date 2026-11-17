using Bank.Application.Abstractions;
using Bank.Domain.Accounts;
using Bank.Domain.Customers;
using Bank.Domain.Pix;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Pix;

public record PixRecipient(Guid AccountId, Guid UserId, string Name, string? Cpf, long AccountNumber, PixKeyType KeyType, string KeyValue)
{
    public PixRecipientResponse ToResponse() =>
        new(Name, Cpf is null ? null : Domain.Customers.Cpf.Mask(Cpf), "Vela", KeyType, PixKeys.Mask(KeyType, KeyValue));
}

/// <summary>What the payer sees before confirming: enough to recognize the person, never the full CPF.</summary>
public record PixRecipientResponse(string Name, string? MaskedCpf, string Bank, PixKeyType KeyType, string MaskedKey);

/// <summary>Key lookup (the bank's own "DICT"): finds which account a PIX key points to.</summary>
public class PixDirectory(IBankDbContext db)
{
    public async Task<PixRecipient?> FindAsync(string keyInput, CancellationToken ct)
    {
        if (PixKeys.Detect(keyInput) is not { } key) return null;
        var row = await (
                from k in db.PixKeys
                join a in db.Accounts on k.AccountId equals a.Id
                join u in db.Users on a.UserId equals u.Id
                join c in db.Customers on u.Id equals c.UserId
                where k.Value == key.Value && a.Type == AccountType.Customer
                select new { a.Id, UserId = u.Id, Name = c.FullName ?? u.Name, c.Cpf, a.Number, k.Type, k.Value })
            .SingleOrDefaultAsync(ct);
        return row is null ? null : new PixRecipient(row.Id, row.UserId, row.Name, row.Cpf, row.Number, row.Type, row.Value);
    }
}
