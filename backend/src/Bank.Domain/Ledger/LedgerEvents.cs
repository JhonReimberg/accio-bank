using Bank.Domain.Common;

namespace Bank.Domain.Ledger;

/// <summary>Money arrived in a customer account (transfer, PIX, deposit, refund...).</summary>
public sealed record FundsCredited(Guid AccountId, Guid TransactionId, TransactionKind Kind, decimal Amount, string Description) : IDomainEvent;
