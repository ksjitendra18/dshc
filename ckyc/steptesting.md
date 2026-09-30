# steptesting.md — Step-by-Step Testing Guide (from Step 1) & Running on Another Machine

This is a practical, run-it-yourself guide for the centralized CKYC pipeline in this repo.
It covers two things:

1. **How to fetch complete details starting from Step 1** — run the pipeline from the very
   first command and then pull **every detail** (source → CRM → record 20/30/40/50/60/70 →
   documents → search → batch → FVU → CERSAI reply) out of the database.
2. **How to run it on another machine** — what to copy, what to install, which hard-coded
   paths must be repointed, and how to go from a clean checkout to a green run.

Every command below is a separate invocation of the same executable, so you can stop after
any step and inspect. Copy-paste blocks are PowerShell.

---

## 0. What "Step 1" is (and what "complete details" means)

Step 1 is the **CBS daily customer-id fetch**: `fetch`. It pulls the day's customer ids from
the source (plus each customer's `documentKey` and intake `source`/channel) and writes one
`Pending` row per customer into `master_record`. Everything after Step 1 enriches that row.

"Complete details" is the union of these tables, all keyed to one customer:

| Where the detail lands | Table(s) | Filled by |
|---|---|---|
| Source id + dockey + channel | `master_record` | **step 1 `fetch`** |
| CRM demographics/POI/address/contact/other | `individual_record_20` … `individual_record_70` | step 3 `store` |
| Supporting/derived document bytes | `file_content` + `individual_document` | `documents import` / `documents fetch` |
| Pre-batch CKYCR search result | `individual_search` (+ `individual_record_20.SearchKey`) | step 4 `search-customer` |
| Batch file + line number | `master_record` / `batch` / `master_record_batch` | step 5 `build-zip` |
| FVU result + hash | `fvu_run` | step 6 `fvu` |
| CERSAI reply per file | `master_record_response` / `upload_response_file` | step 7 `response read` |
| Every stage attempt / retry | `master_record_attempt` | all steps |
| Manual re-push snapshot | `master_record_reattempt` | `reattempt` |

The pipeline order:

```
fetch  →  crm serve  →  store  →  (documents fetch / documents import)  →  search-customer
       →  build-zip  →  fvu  →  response read  →  status
```

---

## 1. Running on another machine — one-time setup

### 1.1 What to copy

Copy the repo **root**, keeping this shape (the vendor tools sit beside the project):

```
<repo>\ckyc\      ← the .NET solution + scripts + samples
<repo>\vendor\    ← FVU_RUN_UTILITY.exe, SFTP_Utility_windows\SFTPRunner.exe, format workbooks
<repo>\cersai\    ← (optional) CERSAI response drop folder
```

> The repo is self-contained; the only large/binary pieces are under `vendor\`. If you do
> **not** need the real FVU yet, you can skip `vendor\FVU_RUN_UTILITY.exe` and run the FVU
> step in simulator mode (see §1.5).

### 1.2 What to install

| Tool | Why | Check |
|---|---|---|
| **.NET 10 SDK** | builds `net10.0` | `dotnet --version` → `>= 10.0.302` |
| **PowerShell** | the `.ps1` scripts | `$PSVersionTable.PSVersion` |
| **SQL Server** (LocalDB or full) + **`sqlcmd`** | the pipeline database | `sqlcmd -?` |
| *(Optional)* **FVU** `FVU_RUN_UTILITY.exe` | step 6 real validation | lives in `vendor\` |
| *(Optional)* **SFTP** `SFTPRunner.exe` | step 6b/6c + `documents fetch` | lives in `vendor\SFTP_Utility_windows\` |

The app runs on **SQL Server** via EF Core 10. LocalDB is the easiest dev target:
`(localdb)\MSSQLLocalDB`. Full SQL Server works too — just change the connection string.

### 1.3 Repoint the hard-coded paths (the #1 "it worked on my machine" trap)

`appsettings.json` still contains **absolute paths from another layout**
(`D:\centralprocessing\...`). On a fresh machine they are wrong and the FVU/batch/document
steps will write to a non-existent folder or fail. Open `ckyc\appsettings.json` and fix:

| Setting | What it points at | Change it to |
|---|---|---|
| `database.connectionString` | SQL Server instance/DB | your instance, e.g. `Server=(localdb)\MSSQLLocalDB;Database=CkycCentral;Trusted_Connection=True;TrustServerCertificate=True` |
| `batch.outputRoot` | generated `.UPL`/zip root | `<repo>\ckyc\runtime\output` |
| `fvu.exePath` | the FVU executable | `<repo>\vendor\FVU_RUN_UTILITY.exe` |
| `fvu.workspaceRoot` | FVU run artifacts | `<repo>\ckyc\runtime` |
| `response.inputRoot` | CERSAI reply drop | `<repo>\cersai\response` (or any folder you control) |
| `search.outputRoot` | `.SRC` search files | `<repo>\ckyc\runtime\search` |
| `documentFetch.downloadRoot` | local inbox for offline doc fetch | `<repo>\ckyc\runtime\document-fetch` |
| `sftp.exePath` | SFTP runner | `<repo>\vendor\SFTP_Utility_windows\SFTPRunner.exe` |

Example (adjust the drive/folder to your machine):

```powershell
# From the ckyc folder — quick, safe edits (back up first)
Copy-Item .\appsettings.json .\appsettings.json.bak
$repo = "D:\ckyccentral"          # <-- change this to where you copied the repo
(Get-Content .\appsettings.json) `
  -replace 'D:\\\\centralprocessing\\\\ckyc',   "$repo\ckyc" `
  -replace 'D:\\\\centralprocessing\\\\vendor', "$repo\vendor" `
  -replace 'D:\\\\centralprocessing\\\\cersai', "$repo\cersai" `
  | Set-Content .\appsettings.json
# then open appsettings.json and double-check EVERY absolute path (see the table above)
```

Then confirm `fvu.exePath` and `sftp.exePath` exist:

```powershell
Test-Path .\..\vendor\FVU_RUN_UTILITY.exe
Test-Path .\..\vendor\SFTP_Utility_windows\SFTPRunner.exe
```

### 1.4 NuGet restore (fresh machine)

The repo ships `nuget.config` pointing at a **local** package cache (offline build). On a new
machine that cache does not exist. Pick one:

```powershell
# A) simplest — restore from nuget.org instead
Remove-Item .\nuget.config
dotnet restore .\src\CKYC.Processor\CKYC.Processor.csproj

# B) or keep offline — edit nuget.config's local-cache/globalPackagesFolder
#    to point at the folder where the packages already exist on this machine
```

### 1.5 Offline-friendly switches (recommended for first run)

The shipped config talks to **real** endpoints that will not exist on a fresh box. For a
self-contained step-test, make a full copy of `appsettings.json` and flip these:

```powershell
Copy-Item .\appsettings.json .\samples\settings-step-local.json
```

Edit `samples\settings-step-local.json`:

```jsonc
{
  "documentFetch": { "enabled": false },   // skip the beckyc SFTP image gate (records go straight to search)
  "crm":    { "mode": "InProcess" },        // use the dummy CRM instead of the real baseUrl
  "fvu":    { "useRealFvu": false },         // deterministic simulator; no FVU exe needed
  "sftp":   { "useRealSftp": false },        // no outbound SFTP
  "simulation": { "saveErrorsEnabled": false } // no forced save failures while you learn the flow
}
```

> `--settings <file>` **replaces the whole config** (it is a full deserialize, not a merge), so
> the override file must be a complete `appsettings.json` copy — that is why we copy it. Keep
> `useRealFvu: true` only when `FVU_RUN_UTILITY.exe` is present and can write to temp.

### 1.6 Provision the database

The application validates the schema at startup and **does not create production DDL** — you
apply it once.

```powershell
$server = "(localdb)\MSSQLLocalDB"
$database = "CkycCentral"

# create the database (drop first if re-running on a dirty box)
sqlcmd -S $server -d master -b -Q "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END; CREATE DATABASE [$database];"

# apply the authoritative schema
sqlcmd -S $server -d $database -b -i .\scripts\sqlserver\schema.sql

# apply the incremental migrations the schema script does not fold in
sqlcmd -S $server -d $database -b -i .\scripts\sqlserver\migrations\20260910_add_individual_search.sql
sqlcmd -S $server -d $database -b -i .\scripts\sqlserver\migrations\20260910_add_master_record_source.sql
sqlcmd -S $server -d $database -b -i .\scripts\sqlserver\migrations\20260928_add_document_fetch.sql
# (see scripts\sqlserver\migrations\ for the full list — apply, in name/date order, any you need)
```

> If `sqlcmd` is missing, install the SQL Server command-line tools, or run `schema.sql` from
> SSMS/Azure Data Studio against the same database.

### 1.7 Build

```powershell
cd <repo>\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1
# manual equivalent:
# dotnet build src\CKYC.Processor\CKYC.Processor.csproj -c Release -p:NuGetAudit=false -m:1 -nodeReuse:false
```

Output: `src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe`.

> `Directory.Build.props` sets `TreatWarningsAsErrors=true`; any analyzer warning fails the
> build. That is intentional — fix the hint, don't lower the bar.

### 1.8 Smoke test

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
& $exe help
& $exe status
```

`status` should print the (empty) stage counts. You are ready for §2.

---

## 2. Step-by-step: fetch complete details from Step 1

Run everything from the `ckyc` project folder so relative paths resolve.

```powershell
Set-Location <repo>\ckyc
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
$s   = ".\samples"                       # seed root
$sqlserver = "(localdb)\MSSQLLocalDB"
$sql = "CkycCentral"
# optional offline-friendly config from §1.5 (otherwise use the default appsettings.json)
$cfg = ".\samples\settings-step-local.json"
```

### Step 0 — clean slate (optional but recommended while testing)

```powershell
Remove-Item .\runtime\output\* -Recurse -ErrorAction SilentlyContinue
Remove-Item .\runtime\runs\*   -Recurse -ErrorAction SilentlyContinue
# see §1.6 to recreate the DB
```

### Step 1 — `fetch` (the source fetch)

Three equivalent ways to choose the ids. Pick **one**.

**A. Generated (default)** — `source.mode = generate` makes `generateCount` deterministic ids
`CUST<yyyyMMdd>0001…`:

```powershell
& $exe --settings $cfg fetch cust
# expected: [fetch] Source customer ids for <date>: 12
#           [fetch] Inserted=12  Skipped=0  Total=12  CbsFailed=0
#           [fetch] Master table rows now in Pending state -> run `store` to enrich from the CRM.
```

**B. A single `custid.json`** (repo ships one with `RJKS2026` + a dockey). Looked up in the
current folder, then next to the exe:

```json
{ "customerId": "RJKS2026", "documentKey": "9f2c7a4e81b3d6f0a5c2" }
```

```powershell
& $exe --settings $cfg fetch custid
```

**C. An explicit file `--file`** (JSON array/object, or plain text `id,docKey,source` per line):

```powershell
& $exe --settings $cfg fetch cust --file .\samples\document-fetch-source.json
# also accepts: fetch --file <path>   (arg order does not matter)
```

A plain-text list is `customerId[,documentKey[,source]]`, separators `, \t | ;`:

```
RJKS2026,9f2c7a4e81b3d6f0a5c2,beckyc
CUST-B
```

**What Step 1 stored** — this *is* the step-1 "complete detail":

```sql
SELECT Id, CustomerId, Source, DocumentKey, BusinessDate, Status, StatusCode, CreatedAt
FROM master_record
ORDER BY Id;
```

| Column | Meaning |
|---|---|
| `CustomerId` | the id from the source |
| `DocumentKey` | the `dockey` that locates the channel image (may be null in generate mode) |
| `Source` | intake channel (`beckyc` default / `app` / …) |
| `BusinessDate` | the fetch business date (`--date yyyy-MM-dd`, default today) |
| `Status` / `StatusCode` | `Pending` / `PND` after a clean fetch; `Failed` / `FLD` if the CBS call failed |

If the CBS simulation is on (`simulation.cbsFetchErrorsEnabled=true`), a subset of ids is
parked as `Failed` with a retry schedule and the command exits `1`. Recover them with the
retry flow (§3).

### Step 2 — `crm serve` (dummy CRM API)

Start it once and leave it running in its own window / background job. `store` calls it over
HTTP.

```powershell
& $exe --settings $cfg crm serve --urls http://127.0.0.1:5291
# health check in another shell:
# Invoke-WebRequest http://127.0.0.1:5291/health -UseBasicParsing
```

With the default config `crm.baseUrl` points at the real bank endpoint — that is why §1.5 sets
`crm.mode=InProcess` for offline runs. (`InProcess` here means "use the built-in dummy data
provider / self-hosted API".)

### Step 3 — `store` (CRM → record tables)

```powershell
& $exe --settings $cfg store
# expected: [store] Done: Succeeded=12  Failed=0  Total=12
```

The CRM payload is split into the record tables. **Verify the complete record details:**

```sql
-- demographics / identity (record 20)
SELECT CustomerId, SearchKey, NameFirst, NameLast, DateOfBirth, Gender, Pan, PhotoOfIndividual
FROM individual_record_20 ORDER BY Id;

-- proof of identity/address (record 30)
SELECT * FROM individual_record_30 ORDER BY Id;
-- address (40), contact (50), related party (60), other/attestation (70)
SELECT * FROM individual_record_40 ORDER BY Id;
SELECT * FROM individual_record_50 ORDER BY Id;
SELECT * FROM individual_record_60 ORDER BY Id;
SELECT * FROM individual_record_70 ORDER BY Id;
```

> **Status after `store` depends on the channel:**
> * channel has an active `documentFetch` source → **`ImagePending (IMP)`** (waiting for step 3b);
> * no source (or `documentFetch.enabled=false`, as in §1.5) → **`PendingSearch (SRP)`**.
>
> If you left `documentFetch` enabled and pointing at the fake beckyc SFTP host, records will
> sit at `IMP` and a `documents fetch` against that fake host fails them to `IMF`, blocking
> them from batching. For a first run, use `documentFetch.enabled=false` or the local inbox.

### Step 3b — documents (only if `documentFetch` is enabled)

Two ways to get bytes into the document store:

```powershell
# (a) pull from the channel source (beckyc by dockey), advances IMP -> SRP
& $exe --settings $cfg documents fetch
& $exe --settings $cfg documents fetch --customer RJKS2026       # one record
& $exe --settings $cfg retry --activity ImageFetch                # re-attempt IMF records

# (b) import from a local/staging folder for one customer
& $exe --settings $cfg documents import --customer-id CUST-RETAIL-SKSS --dir .\staging\CUST-RETAIL-SKSS
```

Offline mode: with `documentFetch.channels.beckyc.useRealSftp=false`, `documents fetch` reads
the same layout from a local inbox:

```
<downloadRoot>\inbox\beckyc\<dockey>\<file>.txt
```

**Verify the stored document bytes:**

```sql
SELECT d.Id, d.MasterRecordId, m.CustomerId, d.OriginalFileName, d.CanonicalFileName,
       d.MediaType, d.SourceType, d.SourceReference, c.ByteLength, c.Sha256
FROM individual_document d
JOIN master_record m ON m.Id = d.MasterRecordId
JOIN file_content  c ON c.Id = d.FileContentId
ORDER BY d.Id;
```

### Step 4 — `search-customer` (pre-batch CKYCR search)

```powershell
& $exe --settings $cfg search-customer
# or: --limit 1000 / --customer <id>
```

Resolution:

* **match found** → reference stored on the master row, status **`SearchFound (SRF)`** (terminal, not batched);
* **no match** → 20-char key written to `individual_record_20.SearchKey`, status **`Searched (SRD)`**, ready to batch.

**Verify the search detail:**

```sql
SELECT s.CustomerId, s.Outcome, s.SearchKey, s.CkycReferenceNumber, s.Remark, s.RequestedAt, s.RespondedAt
FROM individual_search s ORDER BY s.Id;

SELECT CustomerId, SearchKey FROM individual_record_20 WHERE SearchKey IS NOT NULL;
```

If `searchApi.mode=InProcess`, the deterministic simulator marks every
`searchApi.simulateFoundEvery`-th customer as "found". To skip the step entirely, set
`searchApi.enabled=false` (records pass `SRP → SRD` with no API call) — a ready example is
`samples\settings-search-skip.json`.

### Step 5 — `build-zip` (records → `.UPL` + zip)

```powershell
& $exe --settings $cfg build-zip
# expected: [build-zip] Batch '<batchKey>' generated with N record(s).
```

Only `Searched` records are batched. Records that fail validation are **excluded and reported**
(they stay `Saved`). Note the printed batch key.

**Verify:**

```sql
SELECT BatchKey, UploadFileName, RecordCount, UploadFilePath, ZipPath
FROM batch ORDER BY Id DESC;

SELECT CustomerId, BatchFile, BatchRecordLine, StatusCode
FROM master_record WHERE BatchFile IS NOT NULL ORDER BY BatchRecordLine;
```

Artifacts: `runtime\output\<batchKey>\upload\` (the `.UPL` + `support_docs`) and the batch `.zip`.

### Step 6 — `fvu` (validate the batch)

```powershell
& $exe --settings $cfg fvu
# real FVU:  [fvu] Executed=True  ExitCode=0  Passed=True
# simulator: (useRealFvu=false) same contract, deterministic pass
```

Exit codes: `0` success, `2` config error, `3` validation failed, `1`/other fatal.
On success records become **`Uploaded (UPL)`** (or `FvuPassed` for legal), and the processed
zip + file-level SHA-256 land in `runtime\runs\<batchKey>\output\`.

```sql
SELECT TOP (1) BatchKey, ExitCode, Passed, HashValue, OutputZipPath, RunAt
FROM fvu_run ORDER BY Id DESC;
```

> `FVU_RUN_UTILITY.exe` is a PyInstaller bundle that unpacks to the system temp. If a sandbox
> blocks that, `fvu` exits with an empty temp and `-1`. Run it with broader filesystem access,
> or set `useRealFvu=false`.

### Step 7 — `response read` (CERSAI reply → response tables)

```powershell
& $exe --settings $cfg response read                          # last batch + configured inputRoot
& $exe --settings $cfg response read --batch <batchKey>
& $exe --settings $cfg response read --file .\path\file.UPL.RES0
```

Reply files are named `<upload>.UPL.RESm`. Each detail is matched by the record-20 line
number, written to `master_record_response`, mirrored to the master row, and the record moves
to `Reconciled (RCN)` (status `01`/`02`) or `Rejected (REJ)`.

```sql
SELECT m.CustomerId, r.ResponseFileNumber, r.RecordStatus, r.AckNumber,
       r.CkycReferenceNumber, r.CkycNumber, r.RejectionRemark, r.ReadAt
FROM master_record_response r
JOIN master_record m ON m.Id = r.MasterRecordId
ORDER BY r.Id;
```

### Inspect — `status` and the audit trails

```powershell
& $exe --settings $cfg status
```

That prints the current-stage counts and the last batch. The full history lives in:

```sql
SELECT * FROM master_record_attempt  ORDER BY Id;   -- every stage attempt + retry schedule
SELECT * FROM master_record_reattempt ORDER BY Id;  -- manual re-push snapshots
SELECT * FROM master_record_response  ORDER BY Id;  -- every CERSAI reply detail
```

---

## 3. Retry, reattempt, reconcile (the failure paths)

Only retryable activities are re-driven; the master policy is exponential backoff from 24 h,
doubling, max 3 attempts (`activity_type` / `retry` settings).

```powershell
& $exe --settings $cfg retry                       # all retryable records due now
& $exe --settings $cfg retry --activity CbsFetch    # only the CBS fetch
& $exe --settings $cfg retry --activity ImageFetch  # blocked document fetches

# re-push ONE rejected record after a backend DB fix
& $exe --settings $cfg reattempt --customer CUST... --reason "PAN corrected in backend"
& $exe --settings $cfg reattempt --id 42 --reason "Name mismatch resolved"

# manual-intervention report
& $exe --settings $cfg reconcile
& $exe --settings $cfg reconcile --kind retry
& $exe --settings $cfg reconcile --kind cersai --out recovery.csv --stakeholder "Operations"
```

> The 24 h backoff means a `retry` run right after a failure usually prints "none due". For
> testing, make failures due immediately with
> `samples\failure\scripts\expedite-retry.sql` (test-only).

---

## 4. One-shot run

Once the machine is set up, `run.ps1` runs the whole chain (fetch → CRM in a background job →
store → retry → documents fetch → search-customer → build-zip → fvu → status) and shuts the
CRM down at the end:

```powershell
cd <repo>\ckyc
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

Note `run.ps1` uses the default `appsettings.json` (so it hits the real endpoints unless you
edit that file). For an offline self-contained run, either edit `appsettings.json` per §1.5,
or run the steps manually with `--settings .\samples\settings-step-local.json`.

---

## 5. Full-detail extraction — "give me everything for one customer"

Master row + record 20 + document inventory + search + latest response + attempt history:

```sql
DECLARE @cust NVARCHAR(50) = 'RJKS2026';   -- <-- your customer id

-- 1. master / source detail
SELECT Id, CustomerId, ClientType, Source, DocumentKey, BusinessDate, Status, StatusCode,
       Remarks, RetryCount, LastError, BatchFile, BatchRecordLine,
       LastResponseStatus, LastResponseCkycNumber, LastResponseRejectionRemark, ReconStatus
FROM master_record WHERE CustomerId = @cust;

-- 2. demographics (record 20)
SELECT * FROM individual_record_20 WHERE CustomerId = @cust;

-- 3. supporting documents
SELECT d.OriginalFileName, d.CanonicalFileName, d.MediaType, d.SourceType, d.SourceReference,
       c.ByteLength, c.Sha256
FROM individual_document d
JOIN master_record m ON m.Id = d.MasterRecordId
JOIN file_content  c ON c.Id = d.FileContentId
WHERE m.CustomerId = @cust;

-- 4. pre-batch search
SELECT Outcome, SearchKey, CkycReferenceNumber, Remark, RequestedAt, RespondedAt
FROM individual_search WHERE CustomerId = @cust;

-- 5. CERSAI replies
SELECT r.* FROM master_record_response r
JOIN master_record m ON m.Id = r.MasterRecordId
WHERE m.CustomerId = @cust;

-- 6. attempt / retry history
SELECT Stage, Status, Success, Error, AttemptedAt, NextRetryAt
FROM master_record_attempt WHERE CustomerId = @cust ORDER BY AttemptedAt;
```

To get the other records (30/40/50/60/70) swap the table name in step 2.

---

## 6. Troubleshooting (fresh-machine edition)

| Symptom | Cause / fix |
|---|---|
| Build fails with a `CA…` warning | `TreatWarningsAsErrors` + analyzers are on. Fix the analyzer hint. |
| `dotnet restore` fails on a new box | `nuget.config` points at a local cache. Delete it (restore from nuget.org) or repoint it. |
| `store` errors with connection refused | CRM API is not running / `crm.mode` still points at the real URL. Start `crm serve`, or use `crm.mode=InProcess`. |
| Records stuck at `IMP` / `IMF` | `documentFetch` is enabled and pointing at the fake beckyc SFTP. Set `documentFetch.enabled=false` (or `useRealSftp=false` + local inbox). |
| `build-zip` batches nothing | No `Searched` records. Run `search-customer` (or set `searchApi.enabled=false`); confirm records are `SRD`. |
| `fvu` exits `-1` with empty temp | PyInstaller temp extraction blocked (sandbox). Broaden access or set `fvu.useRealFvu=false`. |
| `fvu` exits `3` | A `.UPL` field failed validation — check the per-run `.ERR`/JSON errors. |
| `response read` finds no files | Wrong `response.inputRoot`, no `--file`, or the reply isn't named `<upload>.UPL.RESm`. |
| A record stays `Saved` and never batches | Individuals must be `Searched` first. Run `search-customer` after `store`. |
| `retry` prints "none due" | The 24 h backoff hasn't elapsed — run `expedite-retry.sql` for tests. |
| Database errors at startup | Schema not applied or migrations missing — re-run §1.6. |
| Output lands in `D:\centralprocessing\...` | The absolute paths in `appsettings.json` were not repointed — redo §1.3. |

---

## 7. Quick reference

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
$cfg = ".\samples\settings-step-local.json"   # optional full-config override

# setup (once)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "IF DB_ID('CkycCentral') IS NULL CREATE DATABASE [CkycCentral];"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\schema.sql
.\build.ps1

# step 1 onward
& $exe --settings $cfg fetch cust                 # or: fetch custid | fetch --file <path>
& $exe --settings $cfg crm serve --urls http://127.0.0.1:5291   # separate window
& $exe --settings $cfg store
& $exe --settings $cfg documents fetch             # only when documentFetch is enabled
& $exe --settings $cfg search-customer
& $exe --settings $cfg build-zip
& $exe --settings $cfg fvu
& $exe --settings $cfg response read
& $exe --settings $cfg status
```

Related docs: `README.md` (full feature reference), `BUILD_GUIDE.md` (build/config deep dive),
`run_test.md` + `samples\failure\` (per-step failure drills), `docs\document-fetch.md` and
`docs\image.md` (channel image fetch), `docs\status-master.md` (status codes).
