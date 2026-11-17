using Bank.Application.Common;
using Bank.Domain.Accounts;
using Bank.Domain.Customers;

namespace Bank.Application.Credit;

/// <param name="Progress">0..1 when the requirement is measurable (shown as a progress bar).</param>
public record RuleResult(string Code, bool Passed, string Message, decimal? Progress = null);

/// <summary>One requirement for personal loans. Each rule is its own class with its own tests.</summary>
public interface IEligibilityRule
{
    Task<RuleResult> EvaluateAsync(Customer customer, CancellationToken ct);
}

/// <summary>
/// Rules receive the customer (as in the spec) plus a per-request profile with the data they need
/// (account, policy, score, movement...), so the database is read once for all of them.
/// </summary>
public interface IEligibilityContext
{
    CreditProfile Profile { get; }
}

public sealed class EligibilityContext : IEligibilityContext
{
    public CreditProfile Profile { get; set; } = null!;
}

public sealed class ActiveAccountRule(IEligibilityContext ctx) : IEligibilityRule
{
    public Task<RuleResult> EvaluateAsync(Customer customer, CancellationToken ct)
    {
        var ok = customer.KycStatus == KycStatus.Approved && ctx.Profile.Account.Status == AccountStatus.Active;
        return Task.FromResult(new RuleResult("account_active", ok,
            ok ? "Conta ativa e cadastro aprovado" : "Sua conta precisa estar ativa e com o cadastro aprovado"));
    }
}

public sealed class AccountAgeRule(IEligibilityContext ctx) : IEligibilityRule
{
    public Task<RuleResult> EvaluateAsync(Customer customer, CancellationToken ct)
    {
        var p = ctx.Profile;
        var min = p.Policy.MinAccountAgeDays;
        var days = p.Account.ActivatedAt is { } at ? (int)(p.Now - at).TotalDays : 0;
        var ok = days >= min;
        return Task.FromResult(new RuleResult("account_age", ok,
            ok ? $"Conta aberta há {days} dias" : $"Faltam {min - days} dias de conta (mínimo de {min})",
            min == 0 ? 1 : Math.Min(1, (decimal)days / min)));
    }
}

public sealed class MinimumMovementRule(IEligibilityContext ctx) : IEligibilityRule
{
    public Task<RuleResult> EvaluateAsync(Customer customer, CancellationToken ct)
    {
        var p = ctx.Profile;
        var min = p.Policy.MinMovement30Days;
        var ok = p.Credits30Days >= min;
        return Task.FromResult(new RuleResult("movement", ok,
            ok ? $"Movimentou {Formatting.Brl(p.Credits30Days)} nos últimos 30 dias"
               : $"Movimente mais {Formatting.Brl(min - p.Credits30Days)} em 30 dias (mínimo de {Formatting.Brl(min)})",
            min == 0 ? 1 : Math.Min(1, p.Credits30Days / min)));
    }
}

public sealed class NoOverdueRule(IEligibilityContext ctx) : IEligibilityRule
{
    /// <summary>Using the overdraft is fine; staying negative for this long counts as being behind.</summary>
    public const int NegativeBalanceToleranceDays = 30;

    public Task<RuleResult> EvaluateAsync(Customer customer, CancellationToken ct)
    {
        var p = ctx.Profile;
        var negativeDays = p.NegativeSince is { } since ? (int)(p.Now - since).TotalDays : 0;
        var result =
            p.OverdueInstallments > 0 ? new RuleResult("no_overdue", false, $"Você tem {p.OverdueInstallments} parcela(s) em atraso")
            : negativeDays > NegativeBalanceToleranceDays ? new RuleResult("no_overdue", false, $"Saldo negativo há {negativeDays} dias")
            : new RuleResult("no_overdue", true, "Nenhuma pendência em atraso");
        return Task.FromResult(result);
    }
}

public sealed class MinimumScoreRule(IEligibilityContext ctx) : IEligibilityRule
{
    public Task<RuleResult> EvaluateAsync(Customer customer, CancellationToken ct)
    {
        var p = ctx.Profile;
        var min = p.Policy.MinScore;
        var score = p.Score ?? 0;
        var ok = p.Score is not null && score >= min;
        return Task.FromResult(new RuleResult("score", ok,
            p.Score is null ? "Seu score ainda está sendo calculado"
            : ok ? $"Score {score} (mínimo {min})" : $"Score {score}: faltam {min - score} pontos para {min}",
            min == 0 ? 1 : Math.Min(1, (decimal)score / min)));
    }
}

public sealed class IncomeCommitmentRule(IEligibilityContext ctx) : IEligibilityRule
{
    public Task<RuleResult> EvaluateAsync(Customer customer, CancellationToken ct)
    {
        var p = ctx.Profile;
        var income = customer.MonthlyIncome ?? 0;
        var limit = income * p.Policy.MaxIncomeCommitment;
        var share = income == 0 ? 1 : p.MonthlyCommitment / income;
        var ok = income > 0 && p.MonthlyCommitment < limit;
        return Task.FromResult(new RuleResult("income_commitment", ok,
            ok ? $"Parcelas atuais comprometem {Formatting.Percent(share)} da renda (máximo {Formatting.Percent(p.Policy.MaxIncomeCommitment)})"
               : $"Suas parcelas já comprometem {Formatting.Percent(share)} da renda (máximo {Formatting.Percent(p.Policy.MaxIncomeCommitment)})"));
    }
}
