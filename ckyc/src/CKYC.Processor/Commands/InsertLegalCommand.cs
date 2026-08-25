using System.Text.Json;
using CKYC.Core.Domain;

namespace CKYC.Processor.Commands;

/// <summary>
/// Manually insert a legal-entity record (entity name/constitution/incorporation/etc.) into
/// the dedicated legal-entity record tables, then mark it Saved so it can be batched and
/// submitted to the FVU. Mirrors <see cref="InsertCommand"/> for the client type "L".
///
///   CKYCProcessor.exe insert-legal --file ./entity.json
///   CKYCProcessor.exe insert-legal --customer-id ENT202608240099 --name "Acme Pvt Ltd" --constitution D --date-inco 01-01-2015 ...
///
/// Any omitted POI/address/contact/related/attestation detail is filled with FVU-valid
/// defaults from the dummy CRM, so even a minimal name+constitution produces a batch.
/// </summary>
public sealed class InsertLegalCommand : ICommand
{
    public string Name => "insert-legal";
    public string Usage => "CKYCProcessor.exe insert-legal --file <entity.json>\n" +
                           "         [--customer-id X --name \"Acme Pvt Ltd\" --constitution D --date-inco DD-MM-YYYY --cin UXXXX --pan ABCDE1234F --email e@x.com --mobile 98XXXXXXXX]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var legal = BuildLegalEntity(ctx, args);
        Normalize(legal);

        if (string.IsNullOrWhiteSpace(legal.CustomerId))
        {
            Console.Error.WriteLine("[insert-legal] A customer id is required (--customer-id or customerId in --file).");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(legal.EntityName))
        {
            Console.Error.WriteLine("[insert-legal] An entity name is required (--name or name in --file).");
            return 1;
        }

        // Fill any missing detail record with FVU-valid defaults (mirrors the dummy CRM).
        ApplyValidDefaults(ctx, legal);

        var businessDate = DateOnly.FromDateTime(DateTime.Today);
        var master = await ctx.Master.EnsureAsync(legal.CustomerId, businessDate, "L", ct);

        legal.MasterRecordId = master.Id;
        legal.CustomerId = master.CustomerId;

        var save = await ctx.LegalEntities.SaveAsync(legal, ct);
        if (!save.Success)
        {
            Console.Error.WriteLine($"[insert-legal] Save failed: {save.Error}");
            return 1;
        }

        await ctx.Master.UpdateStatusAsync(master.Id, MasterRecordStatus.Saved, save.Summary, null, ct);

        Console.WriteLine($"[insert-legal] Created '{legal.CustomerId}' ({legal.EntityName})");
        Console.WriteLine($"[insert-legal]   {save.Summary}");
        Console.WriteLine("[insert-legal] Next: `build-zip-legal` then `fvu` to validate and process.");
        return 0;
    }

    private static void ApplyValidDefaults(AppContext ctx, LegalEntity legal)
    {
        // Build the compliant default for THIS entity's constitution (not a deterministic
        // alternate), so the POI proof always matches the requested constitution branch.
        var defaults = string.IsNullOrWhiteSpace(legal.EntityConstitution)
            ? ctx.CrmLegalEntities.GetLegalEntity(legal.CustomerId)
            : ctx.CrmLegalEntities.GetLegalEntity(legal.CustomerId, legal.EntityConstitution);

        if (legal.Proofs.Count == 0 && defaults.Proofs.Count > 0) legal.Proofs = defaults.Proofs;
        if (legal.RegisteredAddress is null) legal.RegisteredAddress = defaults.RegisteredAddress;
        if (legal.PrincipalAddress is null) legal.PrincipalAddress = defaults.PrincipalAddress;
        if (legal.Contact is null) legal.Contact = defaults.Contact;
        if (legal.RelatedParties.Count == 0) legal.RelatedParties = defaults.RelatedParties;
        if (legal.Other is null) legal.Other = defaults.Other;

        // Populate record-20 fields the user may have omitted but the format requires.
        legal.RegisteredAddressDocument ??= defaults.RegisteredAddressDocument;
        legal.PrincipalAddressDocument ??= defaults.PrincipalAddressDocument;
        legal.Pan ??= defaults.Pan;
        legal.PanVerified ??= defaults.PanVerified;
        legal.PanDocument ??= defaults.PanDocument;
        legal.TinGstNumber ??= defaults.TinGstNumber;
        legal.TinGstnDocument ??= defaults.TinGstnDocument;
        legal.Form97 ??= defaults.Form97;
        legal.PlaceOfIncorporation ??= defaults.PlaceOfIncorporation;
        legal.CountryOfIncorporation ??= defaults.CountryOfIncorporation;
        legal.TinIssuingCountry ??= defaults.TinIssuingCountry;
        legal.EntityConstitution ??= defaults.EntityConstitution;
    }

    private static void Normalize(LegalEntity legal)
    {
        legal.Proofs ??= new List<LeProofOfIdentity>();
        legal.RelatedParties ??= new List<LeRelatedParty>();
    }

    private static LegalEntity BuildLegalEntity(AppContext ctx, string[] args)
    {
        // For default-filling we always need the CRM provider for this customer.
        var file = Option(args, "--file");
        if (file is not null && File.Exists(file))
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<LegalEntity>(json, JsonOptions) ?? new LegalEntity();
        }

        var legal = new LegalEntity
        {
            CustomerId = Option(args, "--customer-id") ?? string.Empty,
            SearchKey = "LMO" + Guid.NewGuid().ToString("N").Replace("-", "")[..17],
            EntityConstitution = Option(args, "--constitution") ?? string.Empty,
        };

        legal.EntityName = Option(args, "--name") ?? string.Empty;
        legal.DateOfIncorporation = Option(args, "--date-inco");
        legal.Pan = Option(args, "--pan");

        var cin = Option(args, "--cin");
        if (cin is not null)
            legal.Proofs.Add(new LeProofOfIdentity { Cin = cin });

        var email = Option(args, "--email");
        var mobile = Option(args, "--mobile");
        if (email is not null || mobile is not null)
            legal.Contact = new LeContactDetails
            {
                Email1 = email ?? string.Empty,
                MobileNumber1 = mobile ?? string.Empty,
            };

        return legal;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
