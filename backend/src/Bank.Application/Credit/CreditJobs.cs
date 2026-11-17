using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Application.Ledger;
using Bank.Domain.Accounts;
using Bank.Domain.Common;
using Bank.Domain.Credit;
using Bank.Domain.Ledger;
using Bank.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bank.Application.Credit;

/// <summary>
/// Daily collection: marks late installments as overdue, then tries an automatic debit of every
/// installment due today or overdue. Without balance (or overdraft) it stays overdue and is retried
/// tomorrow, with the fine and default interest growing.
/// </summary>
public class LoanCollectionJob(IBankDbContext db, IServiceScopeFactory scopes, TimeProvider clock, ILogger<LoanCollectionJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(BankTime.ToLocal(clock.GetUtcNow().UtcDateTime));

        var lateLoans = await db.Loans.Include(l => l.Installments)
            .Where(l => l.Status == LoanStatus.Active && l.Installments.Any(i => i.Status == InstallmentStatus.Pending && i.DueDate < today))
            .ToListAsync(ct);
        foreach (var loan in lateLoans) loan.MarkOverdue(today);
        await db.SaveChangesAsync(ct);

        var due = await (
                from i in db.LoanInstallments
                join l in db.Loans on i.LoanId equals l.Id
                where l.Status == LoanStatus.Active && i.Status != InstallmentStatus.Paid && i.DueDate <= today
                orderby i.DueDate
                select new { l.AccountId, i.LoanId, i.Number })
            .ToListAsync(ct);

        var paid = 0;
        foreach (var item in due)
        {
            using var scope = scopes.CreateScope();
            var credit = scope.ServiceProvider.GetRequiredService<CreditService>();
            var policy = await scope.ServiceProvider.GetRequiredService<CreditData>().PolicyAsync(ct);
            try
            {
                await credit.PayAsync(item.AccountId, item.LoanId, item.Number, policy, bySystem: true, ct);
                paid++;
            }
            catch (DomainException e)
            {
                logger.LogInformation("Automatic debit of loan {LoanId} installment {Number} refused: {Reason}", item.LoanId, item.Number, e.Message);
            }
        }
        logger.LogInformation("Loan collection: {Late} loans with new overdue installments, {Paid}/{Due} installments debited", lateLoans.Count, paid, due.Count);
    }
}

/// <summary>Daily job: charges overdraft interest on negative balances (compound daily rate from the monthly one).</summary>
public class OverdraftInterestJob(IBankDbContext db, IServiceScopeFactory scopes, ILogger<OverdraftInterestJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var accountIds = await db.Accounts.Where(a => a.Type == AccountType.Customer && a.Balance < 0).Select(a => a.Id).ToListAsync(ct);
        foreach (var accountId in accountIds)
        {
            using var scope = scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var policy = await sp.GetRequiredService<CreditData>().PolicyAsync(ct);
            await sp.GetRequiredService<LedgerPosting>().ExecuteIfNeededAsync([accountId, SystemAccounts.CreditRevenue], (accounts, now) =>
            {
                // Re-read under the lock: the balance may have changed since the list was taken.
                var account = accounts[accountId];
                var interest = Money.Round(account.OverdraftUsed * policy.OverdraftDailyRate);
                return interest > 0
                    ? LedgerTransaction.Charge(TransactionKind.OverdraftInterest, account, accounts[SystemAccounts.CreditRevenue], interest, "Juros do cheque especial", now)
                    : null;
            }, ct);
        }
        logger.LogInformation("Overdraft interest checked on {Count} accounts", accountIds.Count);
    }
}

public class LoanInstallmentOverdueHandler(IBankDbContext db, IEmailSender email, TimeProvider clock) : IDomainEventHandler<LoanInstallmentOverdue>
{
    public async Task HandleAsync(LoanInstallmentOverdue e, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == e.UserId, ct);
        var body = $"A parcela {e.Number} ({Formatting.Brl(e.Amount)}), vencida em {e.DueDate:dd/MM/yyyy}, não foi paga. " +
                   "Há multa de 2% e juros de mora por dia. Deixe saldo na conta para o débito automático ou pague pelo app.";
        db.Notifications.Add(Notification.Create(user.Id, "Parcela em atraso", body, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        await email.SendAsync(user.Email, "AccioBank: parcela em atraso", body, ct);
    }
}

public class LoanInstallmentPaidHandler(IBankDbContext db, TimeProvider clock) : IDomainEventHandler<LoanInstallmentPaid>
{
    public async Task HandleAsync(LoanInstallmentPaid e, CancellationToken ct)
    {
        db.Notifications.Add(Notification.Create(e.UserId, "Parcela debitada",
            $"A parcela {e.Number} do seu empréstimo ({Formatting.Brl(e.Amount)}) foi paga.", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}

public class LoanPaidOffHandler(IBankDbContext db, TimeProvider clock) : IDomainEventHandler<LoanPaidOff>
{
    public async Task HandleAsync(LoanPaidOff e, CancellationToken ct)
    {
        db.Notifications.Add(Notification.Create(e.UserId, "Empréstimo quitado", "Todas as parcelas foram pagas. Obrigado!", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}
