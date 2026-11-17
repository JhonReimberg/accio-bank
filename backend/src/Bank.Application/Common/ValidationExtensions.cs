using FluentValidation;

namespace Bank.Application.Common;

public static class ValidationExtensions
{
    /// <summary>Positive amount with at most two decimal places.</summary>
    public static IRuleBuilderOptions<T, decimal> MoneyAmount<T>(this IRuleBuilder<T, decimal> rule, decimal max) =>
        rule.GreaterThan(0)
            .LessThanOrEqualTo(max)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage("Use no máximo 2 casas decimais.");
}
