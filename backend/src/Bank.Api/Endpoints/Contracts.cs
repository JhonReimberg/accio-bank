using System.ComponentModel.DataAnnotations;
using Bank.Api.Data;

namespace Bank.Api.Endpoints;

public record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, EmailAddress, StringLength(200)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password);

public record LoginRequest([Required] string Email, [Required] string Password);

public record AuthResponse(string Token, string Name);

public record AccountResponse(string Name, string Email, string Number, decimal Balance);

public record TransactionResponse(Guid Id, TransactionType Type, decimal Amount, string Description, DateTime CreatedAt);

// ParseLimitsInInvariantCulture: otherwise "0.01" fails to parse on pt-BR servers.
public record DepositRequest([Range(typeof(decimal), "0.01", "10000", ParseLimitsInInvariantCulture = true)] decimal Amount);

// To = account number or the recipient's e-mail.
public record TransferRequest(
    [Required, StringLength(200)] string To,
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true)] decimal Amount);
