using Bank.Application.Ledger;
using Microsoft.Extensions.Logging;

namespace Bank.Application.Jobs;

/// <summary>Daily proof that every materialized balance still matches the ledger.</summary>
public class ReconcileLedgerJob(ReconciliationService reconciliation, ILogger<ReconcileLedgerJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var report = await reconciliation.RunAsync(ct);
        if (report.IsBalanced)
            logger.LogInformation("Ledger reconciled: debits {Debits} = credits {Credits}", report.TotalDebits, report.TotalCredits);
        else
            logger.LogCritical("Ledger NOT reconciled: {Count} account mismatches, debits {Debits}, credits {Credits}",
                report.Mismatches.Count, report.TotalDebits, report.TotalCredits);
    }
}
