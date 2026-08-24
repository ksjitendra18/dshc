using CKYC.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CKYC.Crm;

/// <summary>
/// Bundled dummy CRM API. Serves the daily customer-id list and per-customer KYC data
/// over HTTP so the pipeline exercises a real client/server boundary that can be swapped
/// for the production CRM endpoint unchanged.
/// </summary>
public sealed class CrmServer
{
    private readonly DummyCrmDataProvider _data;
    private readonly IDailyCustomerIdProvider _ids;

    public CrmServer(DummyCrmDataProvider data, IDailyCustomerIdProvider ids)
    {
        _data = data;
        _ids = ids;
    }

    public WebApplication Build(string urls)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(urls);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        var app = builder.Build();

        app.MapGet("/api/customers", () =>
            _ids.GetIds(DateOnly.FromDateTime(DateTime.Today)).ToList());

        app.MapGet("/api/customers/{id}", (string id) =>
        {
            var customer = _data.GetCustomer(id);
            return Results.Ok(customer);
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        return app;
    }

    public async Task RunAsync(string urls, CancellationToken ct = default)
    {
        var app = Build(urls);
        await app.RunAsync(ct);
    }
}
