using Bank.Domain.Common;

namespace Bank.Domain.Users;

public class User : Entity
{
    private User() { }

    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    public static User Create(string name, string email, DateTime now) => new()
    {
        Name = name.Trim(),
        Email = NormalizeEmail(email),
        CreatedAt = now,
    };

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
