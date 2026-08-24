using System.Net.Http.Json;
using CKYC.Core;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;

namespace CKYC.Crm;

/// <summary>
/// HTTP client for the CKYC CRM API. In the demo it points at the bundled dummy
/// Kestrel server; in production the same client is pointed at the real CRM endpoint
/// without any code change.
/// </summary>
public sealed class HttpCrmApiClient : ICrmApiClient
{
    private readonly HttpClient _http;
    private readonly CrmSettings _settings;

    public HttpCrmApiClient(CrmSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) };
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(settings.BaseUrl);
    }

    public async Task<IReadOnlyList<string>> GetCustomerIdsAsync(CancellationToken ct = default)
    {
        var ids = await _http.GetFromJsonAsync<List<string>>(_settings.ListEndpoint.TrimStart('/'), ct);
        return ids ?? new List<string>();
    }

    public async Task<Individual?> GetCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var endpoint = _settings.CustomersEndpoint.Replace("{id}", Uri.EscapeDataString(customerId)).TrimStart('/');
        return await _http.GetFromJsonAsync<Individual>(endpoint, ct);
    }
}
