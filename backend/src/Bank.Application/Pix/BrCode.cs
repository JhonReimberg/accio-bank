using System.Globalization;
using System.Text;
using Bank.Domain.Common;

namespace Bank.Application.Pix;

public record BrCodeData(string Key, decimal? Amount, string MerchantName, string MerchantCity, string? Description, string TxId);

/// <summary>
/// Static PIX BR Code ("copia e cola"), a simplified EMV QRCPS payload: ID-length-value fields
/// ending with a CRC16-CCITT checksum (field 63).
/// </summary>
public static class BrCode
{
    private const string PixGui = "br.gov.bcb.pix";

    public static string Build(string key, string merchantName, string merchantCity, decimal? amount = null, string? description = null, string? txId = null)
    {
        var account = Field("00", PixGui) + Field("01", key);
        if (!string.IsNullOrWhiteSpace(description)) account += Field("02", Ascii(description, 72 - key.Length));

        var payload = new StringBuilder()
            .Append(Field("00", "01"))          // payload format
            .Append(Field("01", "11"))          // static, reusable
            .Append(Field("26", account))       // merchant account (PIX)
            .Append(Field("52", "0000"))        // merchant category
            .Append(Field("53", "986"))         // BRL
            .Append(amount is { } a ? Field("54", a.ToString("0.00", CultureInfo.InvariantCulture)) : "")
            .Append(Field("58", "BR"))
            .Append(Field("59", Ascii(merchantName, 25)))
            .Append(Field("60", Ascii(merchantCity, 15)))
            .Append(Field("62", Field("05", string.IsNullOrWhiteSpace(txId) ? "***" : Ascii(txId, 25))))
            .Append("6304")
            .ToString();
        return payload + Crc16(payload).ToString("X4");
    }

    public static BrCodeData Parse(string payload)
    {
        payload = payload.Trim();
        if (payload.Length < 8 || !payload[^8..^4].Equals("6304", StringComparison.Ordinal))
            throw Invalid();
        if (!payload[^4..].Equals(Crc16(payload[..^4]).ToString("X4"), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("brcode.checksum", "Código PIX inválido (verificação falhou). Copie novamente.");

        var root = ReadFields(payload[..^8]);
        if (!root.TryGetValue("26", out var accountField)) throw Invalid();
        var account = ReadFields(accountField);
        if (!account.TryGetValue("00", out var gui) || !gui.Equals(PixGui, StringComparison.OrdinalIgnoreCase) || !account.TryGetValue("01", out var key))
            throw Invalid();

        decimal? amount = root.TryGetValue("54", out var a) && decimal.TryParse(a, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
        var txId = root.TryGetValue("62", out var extra) && ReadFields(extra).TryGetValue("05", out var t) ? t : "***";
        return new BrCodeData(key, amount, root.GetValueOrDefault("59", ""), root.GetValueOrDefault("60", ""), account.GetValueOrDefault("02"), txId);
    }

    /// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF), as required by the BR Code spec.</summary>
    public static ushort Crc16(string data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(data))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    private static string Field(string id, string value) => $"{id}{value.Length:00}{value}";

    private static Dictionary<string, string> ReadFields(string data)
    {
        var fields = new Dictionary<string, string>();
        var i = 0;
        while (i < data.Length)
        {
            if (i + 4 > data.Length || !int.TryParse(data.AsSpan(i + 2, 2), out var length) || i + 4 + length > data.Length)
                throw Invalid();
            fields[data.Substring(i, 2)] = data.Substring(i + 4, length);
            i += 4 + length;
        }
        return fields;
    }

    /// <summary>BR Code fields only allow plain ASCII: strip accents and cut to the field size.</summary>
    private static string Ascii(string value, int maxLength)
    {
        var plain = new string(value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c < 128).ToArray()).Trim();
        return plain.Length > maxLength ? plain[..maxLength] : plain;
    }

    private static DomainException Invalid() => new("brcode.invalid", "Código PIX inválido.");
}
