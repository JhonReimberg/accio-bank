namespace Bank.Domain.Common;

/// <summary>A business rule was violated. Code is stable for clients; Message is shown to the user (pt-BR).</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
