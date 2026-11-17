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
    private User() { }

    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public Role Role { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static User Create(string name, string email, DateTime now, Role role = Role.Customer) => new()
    {
        Name = name.Trim(),
        Email = NormalizeEmail(email),
        Role = role,
        CreatedAt = now,
    };

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
