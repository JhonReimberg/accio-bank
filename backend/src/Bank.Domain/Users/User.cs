using System.Text.Json.Serialization;
using Bank.Domain.Common;

namespace Bank.Domain.Users;

[JsonConverter(typeof(JsonStringEnumConverter<Role>))]
public enum Role
{
    Customer,
    /// <summary>Back-office: reviews KYC, holds and fraud queues.</summary>
    Analyst,
    /// <summary>Back-office with configuration rights (rates, rules, limits).</summary>
    Admin,
}

public class User : Entity
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LoginLockout = TimeSpan.FromMinutes(15);
    public const int MaxPinAttempts = 3;
    public static readonly TimeSpan PinLockout = TimeSpan.FromMinutes(30);

    private User() { }

    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public Role Role { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockoutEndsAt { get; private set; }

    /// <summary>TOTP secret, encrypted at rest. Set while 2FA is being configured, active once enabled.</summary>
    public string? TwoFactorSecret { get; private set; }
    public bool TwoFactorEnabled { get; private set; }

    public string? TransactionPinHash { get; private set; }
    public int FailedPinAttempts { get; private set; }
    public DateTime? PinLockoutEndsAt { get; private set; }

    /// <summary>Where the profile photo is stored (file storage path), if the user set one.</summary>
    public string? PhotoPath { get; private set; }

    public void SetPhoto(string? path) => PhotoPath = path;

    public static User Create(string name, string email, DateTime now, Role role = Role.Customer) => new()
    {
        Name = name.Trim(),
        Email = NormalizeEmail(email),
        Role = role,
        CreatedAt = now,
    };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    // --- Login lockout ---

    public void EnsureCanSignIn(DateTime now)
    {
        if (LockoutEndsAt > now)
            throw new DomainException("auth.locked_out",
                "Acesso bloqueado temporariamente por excesso de tentativas. Tente novamente mais tarde.");
    }

    /// <summary>Counts a wrong password or 2FA code; the Nth failure locks sign-in for a while.</summary>
    public void RegisterFailedSignIn(DateTime now)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts < MaxFailedLogins) return;
        FailedLoginAttempts = 0;
        LockoutEndsAt = now + LoginLockout;
        Raise(new SignInLockedOut(Id, LockoutEndsAt.Value));
    }

    public void RegisterSuccessfulSignIn()
    {
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
    }

    // --- Two-factor (TOTP) ---

    public void BeginTwoFactorSetup(string protectedSecret)
    {
        if (TwoFactorEnabled)
            throw new DomainException("2fa.already_enabled", "A verificação em duas etapas já está ativa.");
        TwoFactorSecret = protectedSecret;
    }

    public void EnableTwoFactor()
    {
        if (TwoFactorSecret is null)
            throw new DomainException("2fa.not_configured", "Configure o aplicativo autenticador primeiro.");
        TwoFactorEnabled = true;
    }

    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
    }

    // --- Transaction PIN ---

    public bool HasTransactionPin => TransactionPinHash is not null;

    public void SetTransactionPin(string pin, Func<string, string> hash)
    {
        TransactionPin.EnsureStrong(pin);
        TransactionPinHash = hash(pin);
        FailedPinAttempts = 0;
        PinLockoutEndsAt = null;
    }

    /// <summary>
    /// Checks the 4-digit PIN required for PIX, payments and credit. Wrong attempts are counted on
    /// the entity, so the caller must persist the user even when this throws.
    /// </summary>
    public void VerifyTransactionPin(string pin, Func<string, string, bool> verify, DateTime now)
    {
        if (TransactionPinHash is null)
            throw new DomainException("pin.not_set", "Cadastre sua senha transacional antes de continuar.");
        if (PinLockoutEndsAt > now)
            throw new DomainException("pin.locked_out", "Senha transacional bloqueada por excesso de tentativas. Tente novamente mais tarde.");
        if (verify(TransactionPinHash, pin))
        {
            FailedPinAttempts = 0;
            return;
        }

        FailedPinAttempts++;
        if (FailedPinAttempts >= MaxPinAttempts)
        {
            FailedPinAttempts = 0;
            PinLockoutEndsAt = now + PinLockout;
            throw new DomainException("pin.locked_out", "Senha transacional bloqueada por 30 minutos após 3 tentativas erradas.");
        }
        var left = MaxPinAttempts - FailedPinAttempts;
        throw new DomainException("pin.invalid", $"Senha transacional incorreta. {left} tentativa(s) restante(s).");
    }
}

public sealed record SignInLockedOut(Guid UserId, DateTime Until) : IDomainEvent;
