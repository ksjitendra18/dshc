using System.Text.Json;
using CKYC.Core.Domain;

namespace CKYC.Processor.Commands;

/// <summary>
/// Manually insert a customer record (name/DOB/address/contact/etc.) into the record
/// tables, then mark it Saved so it can be batched and submitted to the FVU.
///
/// Use this to create a brand-new record rather than relying on the dummy CRM auto-data:
///   CKYCProcessor.exe insert --file ./customer.json
///   CKYCProcessor.exe insert --customer-id CUST202608240099 --name "Amrish Puri" --dob 22-06-1932 ...
///
/// Any detail record (proof/address/contact/related/other) you omit is filled with
/// FVU-valid defaults, so even a minimal name+DOB produces a batch the FVU accepts.
/// </summary>
public sealed class InsertCommand : ICommand
{
    public string Name => "insert";
    public string Usage => "CKYCProcessor.exe insert --file <customer.json>\n" +
                            "                      [--customer-id X --name \"First Last\" --dob DD-MM-YYYY --gender M/F --email e@x.com --mobile 98XXXXXXXX --pan ABCDE1234F]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var individual = BuildIndividual(ctx, args);
        Normalize(individual);
        if (string.IsNullOrWhiteSpace(individual.SourceCustomerId))
        {
            Console.Error.WriteLine("[insert] A source customer id is required (--customer-id or sourceCustomerId in --file).");
            return 1;
        }
        if (!individual.Name.FirstName.Any(char.IsLetter))
        {
            Console.Error.WriteLine("[insert] A name is required (--name \"First Last\" or name.firstName in --file).");
            return 1;
        }

        // Fill any missing detail record with FVU-valid defaults (mirrors the dummy CRM).
        ApplyValidDefaults(ctx, individual);

        // Get-or-create the master row, then save the record tables.
        var businessDate = DateOnly.FromDateTime(DateTime.Today);
        var master = await ctx.Master.EnsureAsync(individual.SourceCustomerId, businessDate, ct);

        individual.MasterRecordId = master.Id;
        individual.SourceCustomerId = master.SourceCustomerId;

        var save = await ctx.Individuals.SaveAsync(individual, ct);
        if (!save.Success)
        {
            Console.Error.WriteLine($"[insert] Save failed: {save.Error}");
            return 1;
        }

        await ctx.Master.UpdateStatusAsync(master.Id, MasterRecordStatus.Saved, save.Summary, null, ct);

        Console.WriteLine($"[insert] Created '{individual.SourceCustomerId}' ({individual.Name.FirstName} {individual.Name.LastName})");
        Console.WriteLine($"[insert]   {save.Summary}");
        Console.WriteLine("[insert] Next: `build-zip` then `fvu` to validate and process.");
        return 0;
    }

    internal static void ApplyValidDefaults(AppContext ctx, Individual individual)
    {
        var defaults = ctx.CrmData.GetCustomer(individual.SourceCustomerId);

        if (individual.Proofs.Count == 0) individual.Proofs = defaults.Proofs;
        if (individual.PermanentAddress is null) individual.PermanentAddress = defaults.PermanentAddress;
        if (individual.CurrentAddress is null) individual.CurrentAddress = defaults.CurrentAddress;
        if (individual.Contact is null) individual.Contact = defaults.Contact;
        if (individual.RelatedParties.Count == 0) individual.RelatedParties = defaults.RelatedParties;
        if (individual.Other is null) individual.Other = defaults.Other;
        // record-20 document fields must reference a file that exists in support_docs
        if (string.IsNullOrEmpty(individual.PhotoOfIndividual)) individual.PhotoOfIndividual = defaults.PhotoOfIndividual;

        // Always fill still-empty conditional-mandatory fields from the compliant default record.
        FillMissingConditionalFields(individual, defaults);
    }

    /// <summary>
    /// Fills conditional-mandatory fields that are still empty using the compliant default
    /// record, so a deliberately minimal insert still satisfies the FVU validation rules.
    /// </summary>
    private static void FillMissingConditionalFields(Individual r, Individual d)
    {
        // ---- record 20 ----
        r.PanDocument ??= d.PanDocument;
        r.DateOfBirthMatchWithOvd ??= d.DateOfBirthMatchWithOvd;
        r.NameMatchWithOvd ??= d.NameMatchWithOvd;
        r.PhotoProvidedMatchWithOvd ??= d.PhotoProvidedMatchWithOvd;
        r.GenderProvidedInOvd ??= d.GenderProvidedInOvd;
        r.GenderMatchWithOvd ??= d.GenderMatchWithOvd;

        // One of PAN / Form 97 / Form 61 is required; default Form 97 when none is supplied.
        if (string.IsNullOrWhiteSpace(r.Pan)
            && !string.Equals(r.Form97Provided, "Y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(r.Form61Provided, "Y", StringComparison.OrdinalIgnoreCase))
            r.Form97Provided = "Y";

        // ---- record 30 (pair by index with the default proof) ----
        for (var i = 0; i < r.Proofs.Count && i < d.Proofs.Count; i++)
        {
            var p = r.Proofs[i];
            var dp = d.Proofs[i];
            if (!string.Equals(p.OvdType, dp.OvdType, StringComparison.OrdinalIgnoreCase)) continue;

            p.ModeOfAadhaarVerification = Coalesce(p.ModeOfAadhaarVerification, dp.ModeOfAadhaarVerification);
            p.LengthOfAadhaar = Coalesce(p.LengthOfAadhaar, dp.LengthOfAadhaar);
            p.IdNumber = Coalesce(p.IdNumber, dp.IdNumber);
            p.ModeOfAuthentication = Coalesce(p.ModeOfAuthentication, dp.ModeOfAuthentication);
            p.EkycDataFromUidai = Coalesce(p.EkycDataFromUidai, dp.EkycDataFromUidai);
            p.CopyOfOvd = Coalesce(p.CopyOfOvd, dp.CopyOfOvd);
        }

        // ---- record 40 (current-address proof-of-address fields) ----
        if (r.CurrentAddress is not null && d.CurrentAddress is not null)
        {
            var c = r.CurrentAddress;
            var dc = d.CurrentAddress;
            c.ProofOfAddress = Coalesce(c.ProofOfAddress, dc.ProofOfAddress);
            c.ProofOfAddressType = Coalesce(c.ProofOfAddressType, dc.ProofOfAddressType);
            c.LengthOfAadhaar = Coalesce(c.LengthOfAadhaar, dc.LengthOfAadhaar);
            c.IdNumber = Coalesce(c.IdNumber, dc.IdNumber);
            c.ModeOfAadhaarVerification = Coalesce(c.ModeOfAadhaarVerification, dc.ModeOfAadhaarVerification);
            c.CopyOfOvd = Coalesce(c.CopyOfOvd, dc.CopyOfOvd);
            c.RemoteGeoTagging = Coalesce(c.RemoteGeoTagging, dc.RemoteGeoTagging);
            c.AddressExactlyMatch = Coalesce(c.AddressExactlyMatch, dc.AddressExactlyMatch);
            c.PositiveVerification = Coalesce(c.PositiveVerification, dc.PositiveVerification);
            c.PhysicalVerificationByThirdParty = Coalesce(c.PhysicalVerificationByThirdParty, dc.PhysicalVerificationByThirdParty);
            c.PhysicalVerificationByReOfficial = Coalesce(c.PhysicalVerificationByReOfficial, dc.PhysicalVerificationByReOfficial);
            c.PresenceInRepository = Coalesce(c.PresenceInRepository, dc.PresenceInRepository);
        }
    }

    private static string Coalesce(string? value, string? fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;

    /// <summary>Re-initialize any name block / collection a JSON might have set to null.</summary>
    private static void Normalize(Individual i)
    {
        i.Name ??= new PersonName();
        i.MaidenName ??= new PersonName();
        i.MotherName ??= new PersonName();
        i.FatherName ??= new PersonName();
        i.SpouseName ??= new PersonName();
        i.Proofs ??= new List<ProofOfIdentity>();
        i.RelatedParties ??= new List<RelatedParty>();
    }

    private static Individual BuildIndividual(AppContext ctx, string[] args)
    {
        var file = Option(args, "--file");
        if (file is not null && File.Exists(file))
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<Individual>(json, JsonOptions) ?? new Individual();
        }

        var individual = new Individual
        {
            SourceCustomerId = Option(args, "--customer-id") ?? string.Empty,
            SearchKey = "IMO" + Guid.NewGuid().ToString("N").Replace("-", "")[..17],
            KycType = "N",
        };

        var name = Option(args, "--name");
        if (name is not null)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            individual.Name.FirstName = parts.Length > 0 ? parts[0] : string.Empty;
            individual.Name.LastName = parts.Length > 1 ? parts[^1] : string.Empty;
            if (parts.Length > 2) individual.Name.MiddleName = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));
        }

        individual.DateOfBirth = Option(args, "--dob");
        individual.Gender = Option(args, "--gender");
        individual.Pan = Option(args, "--pan");

        var email = Option(args, "--email");
        var mobile = Option(args, "--mobile");
        if (email is not null || mobile is not null)
        {
            individual.Contact = new ContactDetails
            {
                Email = email ?? string.Empty,
                MobileNumber = mobile ?? string.Empty,
                MobileValidatedViaOtp = mobile is not null ? "Y" : null,
                EmailValidatedViaOtp = email is not null ? "Y" : null,
                MobileValidatedViaThirdParty = mobile is not null ? "Y" : null,
            };
        }

        return individual;
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
