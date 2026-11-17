namespace Bank.Domain.Customers;

/// <summary>Brazilian phone numbers, stored in E.164 (+55 + area code + number).</summary>
public static class Phone
{
    /// <summary>Accepts "(11) 91234-5678", "11912345678" or "+55 11 91234-5678". Returns null when invalid.</summary>
    public static string? TryNormalize(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is 12 or 13 && digits.StartsWith("55")) digits = digits[2..];
        if (digits.Length is not (10 or 11)) return null;
        var areaCode = int.Parse(digits[..2]);
        if (areaCode < 11) return null;
        // 11 digits = mobile, always starting with 9 after the area code.
        if (digits.Length == 11 && digits[2] != '9') return null;
        return "+55" + digits;
    }
}
