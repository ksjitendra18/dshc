using System.Globalization;
using System.IO.Compression;
using System.Text;
using CKYC.Core.Domain;
using CKYC.Core.Spec;
using CKYC.Data;
using CKYC.Files;
using NLog;

namespace CKYC.Processor.Commands;

/// <summary>
/// Stage two of the bulk-update pipeline: atomically claim pending submissions per client type
/// and generate the pipe-delimited .UPD file (plus its supporting-document zip) using the field
/// layouts of <c>vendor/individual-format-update.xlsx</c> ("I") and
/// <c>vendor/legal-format-update.xlsx</c> ("L").
///
/// Submissions whose referenced support documents have not been imported are excluded from the
/// batch and reported — a broken reference can never reach the FVU or CERSAI.
/// </summary>
public sealed class UpdateProcessCommand : ICommand
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public string Name => "update-process";
    public string Usage => "CKYCProcessor.exe update-process [--limit N] [--date yyyy-MM-dd] [--client I|L]";

    public async Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default)
    {
        var limit = OptionInt(args, "--limit") ?? 1000;
        var businessDate = OptionDate(args, "--date") ?? DateOnly.FromDateTime(DateTime.Today);
        var clientFilter = OptionValue(args, "--client")?.Trim().ToUpperInvariant();
        if (clientFilter is not (null or "I" or "L"))
            throw new ArgumentException("--client must be I (individual) or L (legal entity).");

        var clientTypes = clientFilter is null ? new[] { "I", "L" } : new[] { clientFilter };
        var timeout = TimeSpan.FromMinutes(Math.Max(1, ctx.Settings.Update.ClaimTimeoutMinutes));
        var exitCode = 0;

        foreach (var clientType in clientTypes)
        {
            // One .UPD file per client type; each keeps its own daily sequence counter.
            var claim = await ctx.Updates.ClaimAsync(clientType, limit, businessDate, ctx.Settings.Update.SequenceStart, timeout, ct);
            if (claim is null) continue;
            try
            {
                await ProcessClaim(ctx, claim, businessDate, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ctx.Updates.FailAsync(claim, ex.Message, ct);
                Log.Error(ex, "[update-process] Failed claim {Token}: {Message}", claim.Token, ex.Message);
                exitCode = 1;
            }
        }
        return exitCode;
    }

    private static async Task ProcessClaim(AppContext ctx, UpdateClaim claim, DateOnly businessDate, CancellationToken ct)
    {
        var writer = string.Equals(claim.ClientType, "L", StringComparison.OrdinalIgnoreCase)
            ? (CkycUpdateWriter)ctx.LegalEntityUpdateWriter : (CkycUpdateWriter)ctx.IndividualUpdateWriter;

        // Claimed rows carry their JSON body only — re-parse the submitted amendment fields.
        foreach (var record in claim.Records) UpdateJsonReader.HydrateValues(record);

        var valid = new List<UpdateRequest>(claim.Records);
        var skippedErrors = new Dictionary<long, string>();

        // ---- Supporting-document gate: every doc name written into the .UPD must exist in the store.
        var masterByCustomer = (await ctx.Master.GetByCustomerIdsAsync(
                valid.Select(r => r.CustomerId!).Distinct().ToList(), ct))
            .GroupBy(m => m.CustomerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var documentsByMaster = new Dictionary<long, List<CustomerDocument>>();
        var masterIdsToLoad = valid.Select(r => r.CustomerId!)
            .Where(c => masterByCustomer.ContainsKey(c))
            .Select(c => masterByCustomer[c].Id)
            .Distinct().ToList();
        var documentStore = string.Equals(claim.ClientType, "L", StringComparison.OrdinalIgnoreCase)
            ? ctx.LegalEntityDocuments : ctx.IndividualDocuments;
        foreach (var document in await documentStore.GetByMasterRecordIdsAsync(masterIdsToLoad, ct))
        {
            if (!documentsByMaster.TryGetValue(document.MasterRecordId, out var bucket))
                documentsByMaster[document.MasterRecordId] = bucket = [];
            bucket.Add(document);
        }

        foreach (var record in valid.ToList())
        {
            // Documents are mandatory only where the emitted lines reference file names.
            var referenced = writer.ReferencedDocuments(record);
            if (referenced.Count == 0) continue;
            if (!masterByCustomer.TryGetValue(record.CustomerId!, out var master))
            {
                Exclude(valid, skippedErrors, record,
                    $"No master record exists for customer '{record.CustomerId}'; import documents first (`documents import`).");
                continue;
            }
            var missing = referenced
                .Where(name => FindDocument(documentsByMaster.GetValueOrDefault(master.Id), name) is null)
                .ToList();
            if (missing.Count > 0)
            {
                Exclude(valid, skippedErrors, record,
                    "The following supporting documents have not been imported: " + string.Join(", ", missing) + ".");
            }
        }
        await ctx.Updates.SkipAsync(claim.Token, skippedErrors, ct);

        if (valid.Count == 0)
            throw new InvalidOperationException(
                $"All {claim.Records.Count} submission(s) were excluded — no .UPD was produced. {string.Join(" | ", skippedErrors.Values)}");

        // ---- Directory layout mirrors build-zip: output/<batchKey>/upload[/support_docs].
        var fileName = CkycFileName.Build(writer.ClientType, ctx.Settings.Update.UserId, ctx.Settings.Update.FiCode,
            businessDate, claim.FileSequence, "UPD");
        var batchKey = Path.GetFileNameWithoutExtension(fileName);
        var outputRoot = Path.GetFullPath(ctx.Settings.Update.OutputRoot);
        var uploadDir = Path.Combine(outputRoot, batchKey, "upload");
        var docDir = Path.Combine(uploadDir, "support_docs");
        Directory.CreateDirectory(uploadDir);
        Directory.CreateDirectory(docDir);

        // ---- Materialise documents under collision-safe names and rewrite the references in
        //      the request values so the .UPD text matches the zip entries exactly.
        var materialisedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in valid)
        {
            if (!masterByCustomer.TryGetValue(record.CustomerId!, out var master)) continue;
            var masterId = master.Id;
            foreach (var key in writer.ReferencedDocumentFieldKeys(record))
            {
                var name = CkycUpdateWriter.Value(record, key);
                if (name.Length == 0) continue;
                var document = FindDocument(documentsByMaster.GetValueOrDefault(masterId), name)!;
                var materialised = $"{Path.GetFileNameWithoutExtension(document.OriginalFileName)}__d{document.Id}{Path.GetExtension(document.OriginalFileName)}";
                await File.WriteAllBytesAsync(Path.Combine(docDir, materialised), document.Content, ct);
                materialisedNames.Add(materialised);
                record.Values[key] = materialised;
            }
        }

        // ---- Write the .UPD payload.
        var content = writer.Write(valid, businessDate);
        var uploadPath = Path.Combine(uploadDir, fileName);
        var temporaryPath = uploadPath + "." + claim.Token + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), ct);
        File.Move(temporaryPath, uploadPath, overwrite: false);

        // ---- Zip the .UPD with its support docs (upload/… layout as expected by CERSAI).
        var zipPath = Path.Combine(outputRoot, batchKey, $"{batchKey}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(uploadPath, $"upload/{fileName}", CompressionLevel.Optimal);
            foreach (var document in materialisedNames)
                archive.CreateEntryFromFile(Path.Combine(docDir, document), $"upload/support_docs/{document}", CompressionLevel.Optimal);
        }

        var lineByCkycNumber = writer.ComputeRecord20Lines(valid);
        await ctx.Updates.CompleteAsync(claim, batchKey, fileName, uploadPath, lineByCkycNumber, ct);
        Log.Info("[update-process] Created {UploadPath} ({Count} customer(s), {Skipped} skipped) and archived to {ZipPath}.",
            uploadPath, valid.Count, skippedErrors.Count, zipPath);
        Log.Info("[update-process] Next: `update-fvu` then `update-response`.");
    }

    /// <summary>Locates a customer document by its original or canonical ( NFC lower-case) file name.</summary>
    private static CustomerDocument? FindDocument(IEnumerable<CustomerDocument>? documents, string requestedName)
    {
        if (documents is null) return null;
        var canonical = SqlServerDocumentStoreBase.Canonicalize(requestedName);
        return documents.FirstOrDefault(d => string.Equals(d.OriginalFileName, requestedName, StringComparison.OrdinalIgnoreCase))
            ?? documents.FirstOrDefault(d => string.Equals(d.CanonicalFileName, canonical, StringComparison.OrdinalIgnoreCase));
    }

    private static void Exclude(List<UpdateRequest> valid, Dictionary<long, string> skippedErrors,
        UpdateRequest record, string reason)
    {
        valid.Remove(record);
        skippedErrors[record.Id] = reason;
        Log.Warn("[update-process] Excluded CKYC {Ckyc}: {Reason}", record.CkycNumber, reason);
    }

    private static int? OptionInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var value) ? value : null;
    }

    private static string? OptionValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static DateOnly? OptionDate(string[] args, string name)
    {
        if (OptionValue(args, name) is not { } value) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)) return parsed;
        throw new ArgumentException($"{name} must use yyyy-MM-dd format.");
    }
}
