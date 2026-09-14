using System.Security.Cryptography;
using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;

namespace CKYC.Crm;

/// <summary>
/// Deterministic in-process stand-in for the customer-search API. It exercises both
/// outcomes without a network: every <see cref="SearchApiSettings.SimulateFoundEvery"/>-th
/// customer (by a stable hash of the customer id) is treated as already existing and gets a
/// CKYC reference number; everyone else is "not found" and gets a 20-character search key to
/// write into record 20. The same customer always produces the same result, and the search
/// key is derived from the customer id so every row for that customer agrees.
/// </summary>
public sealed class DummyIndividualSearchApi : IIndividualSearchApiClient
{
    private readonly SearchApiSettings _settings;

    public DummyIndividualSearchApi(SearchApiSettings settings) => _settings = settings;

    public Task<IndividualSearchApiResult> SearchAsync(IndividualSearchApiRequest request, CancellationToken ct = default)
    {
        var customerId = request.CustomerId ?? string.Empty;

        if (_settings.SimulateErrorsEnabled
            && ((!string.IsNullOrEmpty(_settings.SimulateErrorForCustomerId) && customerId == _settings.SimulateErrorForCustomerId)
                || (_settings.SimulateErrorEvery > 0 && StableIndex(customerId) % _settings.SimulateErrorEvery == 0)))
        {
            return Task.FromResult(IndividualSearchApiResult.Failed($"Simulated search API failure for customer '{customerId}'"));
        }

        var found = _settings.SimulateFoundEvery > 0 && StableIndex(customerId) % _settings.SimulateFoundEvery == 0;

        if (found)
        {
            var reference = "R" + StableDigits(customerId + "REF", 13);
            var raw = BuildRaw(request, "Found");
            return Task.FromResult(IndividualSearchApiResult.Found(reference, "Record found in CKYCR", raw));
        }

        var searchKey = "ISR" + StableDigits(customerId + "KEY", 17);
        var rawNotFound = BuildRaw(request, "NotFound");
        return Task.FromResult(IndividualSearchApiResult.NotFound(searchKey, "No record found", rawNotFound));
    }

    private static string BuildRaw(IndividualSearchApiRequest request, string outcome)
        => $"{{\"outcome\":\"{outcome}\",\"customerId\":\"{request.CustomerId}\"," +
           $"\"searchOption\":\"{request.SearchOption}\",\"identity\":\"{request.IdentityTypeAndNumber}\"}}";

    private static int StableIndex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
    }

    private static string StableDigits(string value, int length)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++) sb.Append((char)('0' + bytes[i % bytes.Length] % 10));
        return sb.ToString();
    }
}
