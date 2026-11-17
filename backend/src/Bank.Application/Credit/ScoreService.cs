using System.Text.Json;
using Bank.Application.Abstractions;
using Bank.Domain.Credit;
using Bank.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bank.Application.Credit;

public record ScoreResponse(int Score, IReadOnlyList<ScoreFactor> Factors, DateTime CalculatedAt, IReadOnlyList<ScoreHistoryPoint> History);

public record ScoreHistoryPoint(int Score, DateTime CalculatedAt);

/// <summary>Computes the internal score from the ledger and loan history and stores it with its factors.</summary>
public class ScoreService(IBankDbContext db, CreditData data, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CreditScore> RecalculateAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.UserId == userId, ct);
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == userId, ct);
        var installments = db.LoanInstallments.Where(i => db.Loans.Any(l => l.Id == i.LoanId && l.UserId == userId));

        var inputs = new ScoreInputs(
            KycApproved: customer.KycStatus == KycStatus.Approved,
            AccountAgeDays: account.ActivatedAt is { } at ? (int)(now - at).TotalDays : 0,
            Credits90Days: await data.CreditsSinceAsync(account.Id, now.AddDays(-90), ct),
            MonthlyIncome: customer.MonthlyIncome ?? 0,
            InstallmentsPaidOnTime: await installments.CountAsync(i => i.Status == InstallmentStatus.Paid && !i.WasLate, ct),
            InstallmentsPaidLate: await installments.CountAsync(i => i.Status == InstallmentStatus.Paid && i.WasLate, ct),
            InstallmentsOverdueNow: await installments.CountAsync(i => i.Status == InstallmentStatus.Overdue, ct),
            OverdraftUsageRatio: account.OverdraftLimit > 0 ? account.OverdraftUsed / account.OverdraftLimit : 0);

        var (score, factors) = ScoreCalculator.Calculate(inputs);
        var record = CreditScore.Record(userId, score, JsonSerializer.Serialize(factors, Json), now);
        db.CreditScores.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<ScoreResponse> GetAsync(Guid userId, CancellationToken ct)
    {
        var latest = await db.CreditScores.Where(s => s.UserId == userId).OrderByDescending(s => s.CalculatedAt).FirstOrDefaultAsync(ct)
            ?? await RecalculateAsync(userId, ct);
        var history = await db.CreditScores.Where(s => s.UserId == userId).OrderByDescending(s => s.CalculatedAt).Take(30)
            .Select(s => new ScoreHistoryPoint(s.Score, s.CalculatedAt)).ToListAsync(ct);
        return new ScoreResponse(latest.Score, JsonSerializer.Deserialize<List<ScoreFactor>>(latest.Factors, Json)!, latest.CalculatedAt, history);
    }
}

/// <summary>Daily job: recalculates the score of every approved customer (keeps the history).</summary>
public class RecalculateScoresJob(IBankDbContext db, IServiceScopeFactory scopes, ILogger<RecalculateScoresJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var userIds = await db.Customers.Where(c => c.KycStatus == KycStatus.Approved).Select(c => c.UserId).ToListAsync(ct);
        foreach (var userId in userIds)
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ScoreService>().RecalculateAsync(userId, ct);
        }
        logger.LogInformation("Scores recalculated for {Count} customers", userIds.Count);
    }
}
