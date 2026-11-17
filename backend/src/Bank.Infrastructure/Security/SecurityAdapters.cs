using Bank.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using QRCoder;

namespace Bank.Infrastructure.Security;

/// <summary>RFC 6238 TOTP (6 digits, 30s), compatible with Google Authenticator and similar apps.</summary>
internal class TotpService : ITotpService
{
    private const string IssuerName = "AccioBank";

    public string NewSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    /// <summary>Accepts the previous and next 30s step too, to tolerate small clock differences.</summary>
    public bool Verify(string secret, string code, DateTime now) =>
        new Totp(Base32Encoding.ToBytes(secret)).VerifyTotp(now, code, out _, new VerificationWindow(previous: 1, future: 1));

    public string ProvisioningUri(string accountName, string secret) =>
        new OtpUri(OtpType.Totp, secret, accountName, IssuerName).ToString();
}

/// <summary>Data Protection keys live in the database, so encrypted secrets survive container restarts.</summary>
internal class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Bank.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}

internal class QrCodeGenerator : IQrCodeGenerator
{
    public string PngBase64(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(8));
    }
}
