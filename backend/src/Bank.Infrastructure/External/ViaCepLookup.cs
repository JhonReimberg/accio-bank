using System.Net.Http.Json;
using Bank.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bank.Infrastructure.External;

/// <summary>Public ViaCEP API. Any failure degrades to "not found" so onboarding never blocks on it.</summary>
internal class ViaCepLookup(HttpClient http, ILogger<ViaCepLookup> logger) : ICepLookup
{
    private sealed record ViaCepResponse(string? Cep, string? Logradouro, string? Bairro, string? Localidade, string? Uf, bool? Erro);

    public async Task<CepAddress?> LookupAsync(string cep, CancellationToken ct)
    {
        try
        {
            var response = await http.GetFromJsonAsync<ViaCepResponse>($"ws/{cep}/json/", ct);
            if (response is null || response.Erro == true) return null;
            return new CepAddress(cep, response.Logradouro ?? "", response.Bairro ?? "", response.Localidade ?? "", response.Uf ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "CEP lookup failed for {Cep}", cep);
            return null;
        }
    }
}
