using System.Net.Http.Json;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;

namespace CKYC.Crm;

/// <summary>
/// HTTP client for the customer-search API. The wire contract mirrors the search-format
/// fields (a single record-20 search request in, one result out). In the demo it points at
/// the bundled simulator; in production the same client is pointed at the real endpoint
/// without any code change.
/// </summary>
public sealed class HttpIndividualSearchApiClient : IIndividualSearchApiClient
{
    private readonly HttpClient _http;
    private readonly SearchApiSettings _settings;

    public HttpIndividualSearchApiClient(SearchApiSettings settings, HttpClient? http = null)
    {
        _settings = settings;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) };
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(settings.BaseUrl);
    }

    public async Task<IndividualSearchApiResult> SearchAsync(IndividualSearchApiRequest request, CancellationToken ct = default)
    {
        try
        {
            var endpoint = _settings.SearchEndpoint.TrimStart('/');
            using var response = await _http.PostAsJsonAsync(endpoint, request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<SearchApiResponse>(ct);
            if (body is null) return IndividualSearchApiResult.Failed("Search API returned an empty response.");

            var outcome = string.Equals(body.Outcome, "Found", StringComparison.OrdinalIgnoreCase)
                ? IndividualSearchOutcome.Found
                : IndividualSearchOutcome.NotFound;
            return new IndividualSearchApiResult(outcome, body.SearchKey, body.CkycReferenceNumber,
                body.Remark, body.RawResponseJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return IndividualSearchApiResult.Failed(ex.Message);
        }
    }

    private sealed record SearchApiResponse(
        string? Outcome,
        string? SearchKey,
        string? CkycReferenceNumber,
        string? Remark,
        string? RawResponseJson);
}
