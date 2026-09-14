using CKYC.Core.Domain;

namespace CKYC.Files.Documents;

/// <summary>
/// Maps a configured document slot name to the record field that publishes the document's file
/// name. <see cref="DocumentReferences.For(Individual)"/> is the read side of this mapping, so
/// every slot here is guaranteed to be collected into the batch's supporting documents.
/// </summary>
internal static class RecordDocumentSlots
{
    public static void Apply(Individual record, IReadOnlyList<string> slots, string fileName, List<string> warnings)
    {
        foreach (var slot in slots)
        {
            if (!string.IsNullOrWhiteSpace(slot)) Apply(record, slot, fileName, warnings);
        }
    }

    private static void Apply(Individual record, string slot, string fileName, List<string> warnings)
    {
        switch ((slot ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "proofovd":
                var applied = false;
                foreach (var proof in record.Proofs.Where(p => string.Equals(p.OvdType, "E", StringComparison.OrdinalIgnoreCase)))
                {
                    proof.CopyOfOvd = fileName;
                    applied = true;
                }
                if (!applied) warnings.Add($"No Aadhaar/VID (OVD E) proof found; '{fileName}' could not be attached.");
                break;

            case "permanentaddressovd":
                if (record.PermanentAddress is not null) record.PermanentAddress.CopyOfOvd = fileName;
                break;

            case "currentaddressovd":
                if (record.CurrentAddress is not null) record.CurrentAddress.CopyOfOvd = fileName;
                break;

            case "photoofindividual":
                record.PhotoOfIndividual = fileName;
                break;

            case "pandocument":
                record.PanDocument = fileName;
                break;

            case "clientconsent":
                if (record.Other is not null) record.Other.ClientConsent = fileName;
                else warnings.Add($"Record has no record-70 details; '{fileName}' could not be attached as client consent.");
                break;

            case "declarationdocument":
                if (record.Other is not null) record.Other.DeclarationDocument = fileName;
                else warnings.Add($"Record has no record-70 details; '{fileName}' could not be attached as the declaration/undertaking.");
                break;

            default:
                warnings.Add($"Unknown document slot '{slot}'.");
                break;
        }
    }
}
