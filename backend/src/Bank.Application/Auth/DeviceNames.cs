namespace Bank.Application.Auth;

/// <summary>Human-friendly device label ("Chrome no Windows") for the devices list and alerts.</summary>
public static class DeviceNames
{
    public static string FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Dispositivo desconhecido";
        var browser =
            userAgent.Contains("Edg/") ? "Edge" :
            userAgent.Contains("OPR/") ? "Opera" :
            userAgent.Contains("Firefox/") ? "Firefox" :
            userAgent.Contains("Chrome/") ? "Chrome" :
            userAgent.Contains("Safari/") ? "Safari" :
            "Navegador";
        var os =
            userAgent.Contains("Android") ? "Android" :
            userAgent.Contains("iPhone") || userAgent.Contains("iPad") ? "iOS" :
            userAgent.Contains("Windows") ? "Windows" :
            userAgent.Contains("Mac OS") ? "macOS" :
            userAgent.Contains("Linux") ? "Linux" :
            null;
        return os is null ? browser : $"{browser} no {os}";
    }
}
