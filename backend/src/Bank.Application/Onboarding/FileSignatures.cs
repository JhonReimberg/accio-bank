namespace Bank.Application.Onboarding;

public record DetectedFile(string ContentType, string Extension);

/// <summary>
/// Identifies uploads by their first bytes ("magic numbers") instead of trusting the file name or
/// the Content-Type header sent by the client.
/// </summary>
public static class FileSignatures
{
    public static DetectedFile? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return new("image/jpeg", "jpg");
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return new("image/png", "png");
        if (header.StartsWith("%PDF-"u8)) return new("application/pdf", "pdf");
        return null;
    }
}
