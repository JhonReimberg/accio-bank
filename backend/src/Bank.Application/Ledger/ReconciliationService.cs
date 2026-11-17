using Bank.Application.Abstractions;
using Bank.Domain.Accounts;
using Bank.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Bank.Application.Ledger;

public record AccountMismatch(Guid AccountId, decimal MaterializedBalance, decimal LedgerBalance, decimal BlockedBalance, decimal ActiveHolds);

public record ReconciliationReport(bool IsBalanced, decimal TotalDebits, decimal TotalCredits, IReadOnlyList<AccountMismatch> Mismatches);

/// <summary>Proves the materialized balances can be rebuilt from the ledger.</summary>
public class ReconciliationService(IBankDbContext db)
{
    public async Task<ReconciliationReport> RunAsync(CancellationToken ct)
    {
        var totals = await db.LedgerEntries
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Debits = g.Where(e => e.Direction == EntryDirection.Debit).Sum(e => e.Amount),
                Credits = g.Where(e => e.Direction == EntryDirection.Credit).Sum(e => e.Amount),
            })
            .SingleOrDefaultAsync(ct);

        var rows = await db.Accounts
            .Select(a => new
            {
                a.Id,
                a.Balance,
                Ledger = db.LedgerEntries.Where(e => e.AccountId == a.Id)
                    .Sum(e => e.Direction == EntryDirection.Credit ? e.Amount : -e.Amount),
                a.BlockedBalance,
                Holds = db.BalanceHolds.Where(h => h.AccountId == a.Id && h.Status == HoldStatus.Active).Sum(h => h.Amount),
            })
            .Where(m => m.Balance != m.Ledger || m.BlockedBalance != m.Holds)
            .ToListAsync(ct);
        var mismatches = rows.Select(m => new AccountMismatch(m.Id, m.Balance, m.Ledger, m.BlockedBalance, m.Holds)).ToList();

        var debits = totals?.Debits ?? 0;
        var credits = totals?.Credits ?? 0;
        return new ReconciliationReport(debits == credits && mismatches.Count == 0, debits, credits, mismatches);
    }
}
