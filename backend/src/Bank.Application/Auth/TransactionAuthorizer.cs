using System.Runtime.ExceptionServices;
using Bank.Application.Abstractions;
using Bank.Application.Common;
using Bank.Domain.Common;

namespace Bank.Application.Auth;

/// <summary>Checks the transaction PIN before PIX, payments and credit contracts.</summary>
public class TransactionAuthorizer(IBankDbContext db, ICurrentUser currentUser, ISecretHasher hasher, TimeProvider clock)
{
    /// <summary>
    /// Must be called before the operation changes anything. The user row is locked and the attempt
    /// counter is committed even when the PIN is wrong, so parallel guesses cannot bypass the lockout.
    /// </summary>
    public async Task VerifyPinAsync(string? pin, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var user = await db.LockUserAsync(currentUser.UserId, ct) ?? throw new UnauthorizedException("Sessão inválida.");
        DomainException? failure = null;
        try
        {
            user.VerifyTransactionPin(pin ?? "", hasher.Verify, clock.GetUtcNow().UtcDateTime);
        }
        catch (DomainException e)
        {
            failure = e;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
    }
}
