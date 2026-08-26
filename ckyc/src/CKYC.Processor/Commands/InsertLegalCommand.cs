using System.Text.Json;
using CKYC.Core.Domain;
using CKYC.Core.Spec;
using NLog;

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
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "insert-legal";
    public string Usage => "CKYCProcessor.exe insert-legal --file <entity.json>\n" +
                           "         [--customer-id X --name \"Acme Pvt Ltd\" --constitution D --date-inco DD-MM-YYYY --cin UXXXX --pan ABCDE1234F --email e@x.com --mobile 98XXXXXXXX]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var legal = BuildLegalEntity(ctx, args);
        Normalize(legal);

        if (string.IsNullOrWhiteSpace(legal.CustomerId))
        {
            Log.Error("[insert-legal] A customer id is required (--customer-id or customerId in --file).");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(legal.EntityName))
        {
            Log.Error("[insert-legal] An entity name is required (--name or name in --file).");
            return 1;
        }

        // Fill any missing detail record with FVU-valid defaults (mirrors the dummy CRM).
        ApplyValidDefaults(ctx, legal);

        var validationErrors = LegalEntityRecordValidator.Validate(legal);
        if (validationErrors.Count > 0)
        {
            Log.Error("[insert-legal] Legal entity failed create-format validation:");
            foreach (var error in validationErrors)
                Log.Error("[insert-legal]   - [{RecordType}/{FieldName}] {ErrorDescription}", error.RecordType, error.FieldName, error.ErrorDescription);
            return 1;
        }

        var businessDate = DateOnly.FromDateTime(DateTime.Today);
        var master = await ctx.Master.EnsureAsync(legal.CustomerId, businessDate, "L", ct);
        if (!string.Equals(master.ClientType, "L", StringComparison.OrdinalIgnoreCase))
        {
            Log.Error("[insert-legal] Customer id '{CustomerId}' already belongs to client type '{ClientType}' and cannot be stored as a legal entity.", legal.CustomerId, master.ClientType);
            return 1;
        }

        legal.MasterRecordId = master.Id;
        legal.CustomerId = master.CustomerId;

        var save = await ctx.LegalEntities.SaveAsync(legal, ct);
        if (!save.Success)
        {
            Log.Error("[insert-legal] Save failed: {Error}", save.Error);
            return 1;
        }

        await ctx.Master.UpdateStatusAsync(master.Id, MasterRecordStatus.Saved, save.Summary, null, ct);

        Log.Info("[insert-legal] Created '{CustomerId}' ({EntityName})", legal.CustomerId, legal.EntityName);
        Log.Info("[insert-legal]   {Summary}", save.Summary);
        Log.Info("[insert-legal] Next: `build-zip-legal` then `fvu` to validate and process.");
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
        else if (legal.Proofs.Count > 0 && defaults.Proofs.Count > 0) FillMissingStrings(legal.Proofs[0], defaults.Proofs[0]);
        if (legal.RegisteredAddress is null) legal.RegisteredAddress = defaults.RegisteredAddress;
        else if (defaults.RegisteredAddress is not null) FillMissingStrings(legal.RegisteredAddress, defaults.RegisteredAddress);
        if (legal.PrincipalAddress is null) legal.PrincipalAddress = defaults.PrincipalAddress;
        else if (defaults.PrincipalAddress is not null) FillMissingStrings(legal.PrincipalAddress, defaults.PrincipalAddress);
        if (legal.Contact is null) legal.Contact = defaults.Contact;
        else if (defaults.Contact is not null) FillMissingStrings(legal.Contact, defaults.Contact);
        if (legal.RelatedParties.Count == 0) legal.RelatedParties = defaults.RelatedParties;
        if (legal.Other is null) legal.Other = defaults.Other;
        else if (defaults.Other is not null) FillMissingStrings(legal.Other, defaults.Other);

        // Populate record-20 fields the user may have omitted but the format requires.
        legal.SearchKey = Missing(legal.SearchKey, defaults.SearchKey) ?? string.Empty;
        legal.EntityConstitution = Missing(legal.EntityConstitution, defaults.EntityConstitution) ?? string.Empty;
        legal.ListedCompany = Missing(legal.ListedCompany, defaults.ListedCompany);
        legal.RegisteredFirm = Missing(legal.RegisteredFirm, defaults.RegisteredFirm);
        legal.RegisteredTrust = Missing(legal.RegisteredTrust, defaults.RegisteredTrust);
        legal.DateOfIncorporation = Missing(legal.DateOfIncorporation, defaults.DateOfIncorporation);
        legal.DateOfCommencement = Missing(legal.DateOfCommencement, defaults.DateOfCommencement);
        legal.PlaceOfIncorporation = Missing(legal.PlaceOfIncorporation, defaults.PlaceOfIncorporation);
        legal.CountryOfIncorporation = Missing(legal.CountryOfIncorporation, defaults.CountryOfIncorporation);
        legal.TinIssuingCountry = Missing(legal.TinIssuingCountry, defaults.TinIssuingCountry);
        if (!string.Equals(legal.Form97?.Trim(), "Y", StringComparison.OrdinalIgnoreCase)) legal.Pan = Missing(legal.Pan, defaults.Pan);
        legal.PanVerified = Missing(legal.PanVerified, defaults.PanVerified);
        legal.PanDocument = Missing(legal.PanDocument, defaults.PanDocument);
        legal.TinGstNumber = Missing(legal.TinGstNumber, defaults.TinGstNumber);
        legal.TinGstnDocument = Missing(legal.TinGstnDocument, defaults.TinGstnDocument);
        legal.Form97 = Missing(legal.Form97, defaults.Form97);
        legal.RegisteredAddressDocument = Missing(legal.RegisteredAddressDocument, defaults.RegisteredAddressDocument);
        legal.PrincipalAddressDocument = Missing(legal.PrincipalAddressDocument, defaults.PrincipalAddressDocument);
    }

    private static void FillMissingStrings<T>(T target, T defaults)
    {
        foreach (var property in typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite))
            if (string.IsNullOrWhiteSpace((string?)property.GetValue(target)))
                property.SetValue(target, property.GetValue(defaults));
    }

    private static string? Missing(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

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
