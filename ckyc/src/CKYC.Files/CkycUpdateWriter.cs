using System.Text;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Core.Domain;
using CKYC.Core.Spec;

namespace CKYC.Files;

/// <summary>
/// Writes the CERSAI bulk-update (.UPD) files — pipe-delimited layouts taken field-for-field
/// from <c>vendor/individual-format-update.xlsx</c> (client type "I") and
/// <c>vendor/legal-format-update.xlsx</c> (client type "L").
///
/// Both share the same shape: a record-10 header followed by detail records whose first three
/// positions are Record Type, Line Number (running sequence over every detail line of the file)
/// and the existing CKYC Number being amended. Every subsequent position comes from the matching
/// <see cref="UpdateFormat"/> catalog entry; each detail line ends with the FVU-managed Hash
/// Value placeholder column (the trailing pipe seen in vendor samples).
///
/// A section's detail record is emitted only when its payload carries values beyond the update
/// flags and selector prelude (see <see cref="SelectorExclusions"/>) — mirroring how the sheets
/// condition every block column on its "*Update Flg" switch.
/// </summary>
public abstract class CkycUpdateWriter : IUpdateFileWriter
{
    /// <summary>
    /// Positions that merely echo the customer rather than decide whether their block is being
    /// amended (a KYC-type echo on records 30/40/70, the constitution echoed onto the POI block).
    /// </summary>
    private static readonly Dictionary<(string ClientType, string RecordType), IReadOnlySet<string>> SelectorExclusions =
        new()
        {
            [("I", "30")] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kycType" },
            [("I", "40")] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kycType" },
            [("I", "70")] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kycType" },
            [("L", "30")] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "entityConstitution" },
        };

    private protected readonly UpdateSettings Settings;

    protected CkycUpdateWriter(UpdateSettings settings, string clientType)
    {
        Settings = settings;
        ClientType = clientType;
    }

    public string ClientType { get; }

    public string Write(IReadOnlyList<UpdateRequest> records, DateOnly businessDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader(businessDate, records.Count));
        var lineNo = 1;
        foreach (var record in records)
            lineNo = WriteDetail(sb, record, lineNo);
        return sb.ToString();
    }

    /// <summary>
    /// Maps each amended CKYC number to its record-20 line number inside the generated file,
    /// so a .UPD.RESm reply ("Line Number of Record type 20") can be attributed back to the
    /// right submission row.
    /// </summary>
    public IReadOnlyDictionary<string, int> ComputeRecord20Lines(IReadOnlyList<UpdateRequest> records)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lineNo = 1;
        foreach (var record in records)
        {
            map[record.CkycNumber] = lineNo++;
            foreach (var _ in PlanLayouts(record)) lineNo++;
        }
        return map;
    }

    /// <summary>Support_docs file names referenced by document-typed positions of this record's emitted lines.</summary>
    public IReadOnlyCollection<string> ReferencedDocuments(UpdateRequest record)
    {
        var names = new List<string>();
        foreach (var layout in PlanLayouts(record))
            foreach (var field in layout.Fields)
                if (field.Document && Value(record, field.Key) is { Length: > 0 } name && !names.Contains(name))
                    names.Add(name);
        return names;
    }

    /// <summary>The format keys of document-typed positions carrying values on this record's emitted lines.</summary>
    public IReadOnlyCollection<string> ReferencedDocumentFieldKeys(UpdateRequest record)
    {
        var keys = new List<string>();
        foreach (var layout in PlanLayouts(record))
            foreach (var field in layout.Fields)
                if (field.Document && Value(record, field.Key).Length > 0 && !keys.Contains(field.Key))
                    keys.Add(field.Key);
        return keys;
    }

    // ------------------------------------------------------------------
    // Per-client-type emission.
    // ------------------------------------------------------------------

    /// <summary>Appends the detail lines of one request starting at <paramref name="lineNo"/>; returns the next free line number.</summary>
    protected abstract int WriteDetail(StringBuilder sb, UpdateRequest record, int lineNo);

    /// <summary>The optional detail-record layouts (after record 20) that carry amendment payload.</summary>
    protected abstract IEnumerable<UpdateFormat.Layout> PlanLayouts(UpdateRequest record);

    protected string BuildHeader(DateOnly businessDate, int customerCount)
    {
        var fields = new List<string?>
        {
            CkycRecords.Header,                       // 10 - Header
            Settings.FiCode,                          // FI Code (6)
            Settings.RegionCode,                      // Region/Branch code (11)
            ClientType,                               // I-Individual / L-Legal Entity
            customerCount.ToString(),                 // Total No of Detail Records (count of '20')
            Settings.VersionNumber,                   // Version number (e.g. V2.0 per the sheets)
            businessDate.ToString("dd-MM-yyyy"),      // Create Date DD-MM-YYYY
            "",                                       // Filler 1
            "",                                       // Filler 2
        };
        // The legal-entity header workbook adds one more filler before the FVU-managed columns
        // (FVU version number, record-level hash and file-level hash appended by the FVU).
        if (string.Equals(ClientType, "L", StringComparison.OrdinalIgnoreCase)) fields.Add("");
        return string.Join('|', fields);
    }

    /// <summary>Renders one detail line: prefix + catalog positions + Hash Value placeholder.</summary>
    protected static string BuildLine(string recordType, UpdateRequest record, int lineNo, UpdateFormat.Layout layout,
        IReadOnlyDictionary<string, bool>? computedCounts = null)
    {
        var fields = new List<string?>
        {
            recordType,
            lineNo.ToString(),
            record.CkycNumber.Trim(),       // existing CKYC number being amended
        };
        foreach (var field in layout.Fields)
        {
            if (field.Key.StartsWith("countRecord", StringComparison.Ordinal))
            {
                // "Count of Record Type NN associated with this '20' record".
                var attachedRecordType = field.Key["countRecord".Length..];
                fields.Add(computedCounts is not null && computedCounts.TryGetValue(attachedRecordType, out var emitted) && emitted
                    ? "1" : "");
            }
            else
            {
                fields.Add(Value(record, field.Key));
            }
        }
        fields.Add("");                                 // Hash Value — filled by the FVU
        return string.Join('|', fields);
    }

    /// <summary>Whether a section carries amendment payload beyond its flags and selector prelude.</summary>
    protected static bool HasPayload(UpdateRequest record, UpdateFormat.Layout layout)
    {
        var exclusions = SelectorExclusions.TryGetValue((layout.ClientType, layout.RecordType), out var excluded)
            ? excluded : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return layout.Fields.Any(field => !field.Flag
            && !field.Key.StartsWith("countRecord", StringComparison.Ordinal)
            && !exclusions.Contains(field.Key)
            && Value(record, field.Key).Length > 0);
    }

    /// <summary>Trims a submitted value defensively; pipe/newline payloads can never reach a line.</summary>
    public static string Value(UpdateRequest record, string key)
    {
        var raw = record.Values.TryGetValue(key, out var value) ? value : null;
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Contains('|') || trimmed.Contains('\r') || trimmed.Contains('\n'))
            throw new InvalidDataException($"Update field '{key}' cannot contain pipe or newline characters.");
        return trimmed;
    }
}

/// <summary>Client type "I": demographic details (20) plus up to five optional amendment blocks.</summary>
public sealed class CkycIndividualUpdateWriter : CkycUpdateWriter
{
    private readonly UpdateFormat.Layout _record20;
    private readonly UpdateFormat.Layout _record30;
    private readonly UpdateFormat.Layout _record40;
    private readonly UpdateFormat.Layout _record50;
    private readonly UpdateFormat.Layout _record60;
    private readonly UpdateFormat.Layout _record70;

    public CkycIndividualUpdateWriter(UpdateSettings settings) : base(settings, "I")
    {
        _record20 = Single("I", "20");
        _record30 = Single("I", "30");
        _record40 = Single("I", "40");
        _record50 = Single("I", "50");
        _record60 = Single("I", "60");
        _record70 = Single("I", "70");
    }

    protected override int WriteDetail(StringBuilder sb, UpdateRequest record, int lineNo)
    {
        // The record-20 sheet asks for counts of the attached 30/40/50/60/70 records.
        var counts = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["30"] = HasPayload(record, _record30),
            ["40"] = HasPayload(record, _record40),
            ["50"] = HasPayload(record, _record50),
            ["60"] = HasPayload(record, _record60),
            ["70"] = HasPayload(record, _record70),
        };

        sb.AppendLine(BuildLine(CkycRecords.Demographic, record, lineNo++, _record20, counts));
        if (counts["30"]) sb.AppendLine(BuildLine(CkycRecords.Proof, record, lineNo++, _record30));
        if (counts["40"]) sb.AppendLine(BuildLine(CkycRecords.Address, record, lineNo++, _record40));
        if (counts["50"]) sb.AppendLine(BuildLine(CkycRecords.Contact, record, lineNo++, _record50));
        if (counts["60"]) sb.AppendLine(BuildLine(CkycRecords.RelatedParty, record, lineNo++, _record60));
        if (counts["70"]) sb.AppendLine(BuildLine(CkycRecords.Other, record, lineNo++, _record70));
        return lineNo;
    }

    protected override IEnumerable<UpdateFormat.Layout> PlanLayouts(UpdateRequest record)
    {
        if (HasPayload(record, _record30)) yield return _record30;
        if (HasPayload(record, _record40)) yield return _record40;
        if (HasPayload(record, _record50)) yield return _record50;
        if (HasPayload(record, _record60)) yield return _record60;
        if (HasPayload(record, _record70)) yield return _record70;
    }

    private static UpdateFormat.Layout Single(string clientType, string recordType)
        => UpdateFormat.DetailLayouts[(clientType, recordType)].Single();
}

/// <summary>
/// Client type "L": entity details (20), constitution-specific POI (sheet '30' defines one block
/// per constitution family — exactly one applies), registered/principal addresses (40), contact
/// (50), related parties (60) and attestation (70).
/// </summary>
public sealed class CkycLegalEntityUpdateWriter : CkycUpdateWriter
{
    private readonly UpdateFormat.Layout _record20;
    private readonly UpdateFormat.Layout _company30;
    private readonly UpdateFormat.Layout _partnership30;
    private readonly UpdateFormat.Layout _trust30;
    private readonly UpdateFormat.Layout _unincorporated30;
    private readonly UpdateFormat.Layout _otherConstitution30;
    private readonly UpdateFormat.Layout _record40;
    private readonly UpdateFormat.Layout _record50;
    private readonly UpdateFormat.Layout _record60;
    private readonly UpdateFormat.Layout _record70;

    public CkycLegalEntityUpdateWriter(UpdateSettings settings) : base(settings, "L")
    {
        _record20 = Single("L", "20");
        _record40 = Single("L", "40");
        _record50 = Single("L", "50");
        _record60 = Single("L", "60");
        _record70 = Single("L", "70");

        var poiVariants = UpdateFormat.DetailLayouts[("L", "30")].ToArray();
        _company30 = poiVariants.Single(l => l.Fields.Any(f => f.Key == "cin"));
        _partnership30 = poiVariants.Single(l => l.Fields.Any(f => f.Key == "llpin"));
        _trust30 = poiVariants.Single(l => l.Fields.Any(f => f.Key == "trustDeed"));
        _unincorporated30 = poiVariants.Single(l => l.Fields.Any(f => f.Key == "resolutionManagingBody"));
        _otherConstitution30 = poiVariants.Except(new[] { _company30, _partnership30, _trust30, _unincorporated30 }).Single();
    }

    protected override int WriteDetail(StringBuilder sb, UpdateRequest record, int lineNo)
    {
        sb.AppendLine(BuildLine(CkycRecords.Demographic, record, lineNo++, _record20));

        var poi = PoiLayout(Value(record, "entityConstitution"));
        if (HasPayload(record, poi)) sb.AppendLine(BuildLine(CkycRecords.Proof, record, lineNo++, poi));

        if (HasPayload(record, _record40)) sb.AppendLine(BuildLine(CkycRecords.Address, record, lineNo++, _record40));
        if (HasPayload(record, _record50)) sb.AppendLine(BuildLine(CkycRecords.Contact, record, lineNo++, _record50));
        if (HasPayload(record, _record60)) sb.AppendLine(BuildLine(CkycRecords.RelatedParty, record, lineNo++, _record60));
        if (HasPayload(record, _record70)) sb.AppendLine(BuildLine(CkycRecords.Other, record, lineNo++, _record70));
        return lineNo;
    }

    protected override IEnumerable<UpdateFormat.Layout> PlanLayouts(UpdateRequest record)
    {
        var poi = PoiLayout(Value(record, "entityConstitution"));
        if (HasPayload(record, poi)) yield return poi;
        if (HasPayload(record, _record40)) yield return _record40;
        if (HasPayload(record, _record50)) yield return _record50;
        if (HasPayload(record, _record60)) yield return _record60;
        if (HasPayload(record, _record70)) yield return _record70;
    }

    private UpdateFormat.Layout PoiLayout(string constitution)
    {
        if (LeConstitution.IsCompany(constitution)) return _company30;
        if (constitution is LeConstitution.PartnershipFirm or LeConstitution.Llp) return _partnership30;
        if (string.Equals(constitution, LeConstitution.Trust, StringComparison.OrdinalIgnoreCase)) return _trust30;
        if (string.Equals(constitution, LeConstitution.UnincorporatedAssociation, StringComparison.OrdinalIgnoreCase)) return _unincorporated30;
        return _otherConstitution30;   // any other constitution type uses the residual sheet block
    }

    private static UpdateFormat.Layout Single(string clientType, string recordType)
        => UpdateFormat.DetailLayouts[(clientType, recordType)].Single();
}
