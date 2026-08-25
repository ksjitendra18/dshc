# Centralized CKYC Processor — Build & Run Guide

This guide walks through building the CKYC batch-processing pipeline from scratch and
running it end to end on your own machine. It is written for anyone who has the repo
(or a copy of it) and wants to compile and run it.

---

## 0. What database does it use?

By default this pipeline uses **SQLite**:

| Key            | Value                                                                  |
|----------------|------------------------------------------------------------------------|
| Provider       | `sqlite`                                                                |
| DB file        | `runtime\ckyc.db` (created automatically on first run)                  |
| Connection     | `Data Source=D:\centralprocessing\ckyc\runtime\ckyc.db;Cache=Shared`    |
| Schema create  | `createSchemaOnStartup: true` (auto-creates tables on every start)      |

Tables created automatically: `master_record`, `kyc_record_20/30/40/50/60/70`,
`batch`, `fvu_run`, `master_record_response`, `master_record_attempt`, `activity_type`
(seeded with the retryable activity master), `status_master` (seeded with the status
master lookup), `master_record_reattempt`.

> The pipeline can also run against **SQL Server**. Set `database.provider = "sqlserver"`,
> supply a connection string, and run `scripts/sqlserver/schema.sql`. Everything else stays
> the same. SQLite is the zero-dependency default and what the E2E run uses.

---

## 1. Prerequisites

- **.NET 10 SDK** (the solution builds `net10.0`). Confirm with:
  ```powershell
  dotnet --version   # >= 10.0.302
  ```
  The ASP.NET Core runtime needed by `CKYC.Crm` ships with the SDK, so no separate install.
- **PowerShell** (the build/run scripts are `.ps1`).
- *(Optional)* a **SQLite CLI** (`sqlite3`) to inspect the DB directly (used below).
- *(Optional)* the real **FVU** tool for step 5 — `FVU_RUN_UTILITY.exe`, expected at
  `vendor\FVU_RUN_UTILITY.exe` (configured via `fvu.exePath`). Without it you can still run
  the whole pipeline using the built-in simulator (see §8).

---

## 2. Project layout

| Folder / Project          | Responsibility                                                            |
|---------------------------|---------------------------------------------------------------------------|
| `src\CKYC.Core`           | Domain models, settings, CKYC format spec, interfaces/contracts           |
| `src\CKYC.Data`           | SQLite persistence, schema bootstrap, repositories, batch/FVU journal     |
| `src\CKYC.Crm`            | Dummy CRM data, HTTP client, self-hosted Kestrel API (`crm serve`)        |
| `src\CKYC.Files`          | Writes the pipe-delimited `.UPL` + zip (`build-zip`) and file hashing     |
| `src\CKYC.Fvu`            | Real `FVU_RUN_UTILITY.exe` invocation + deterministic simulation fallback |
| `src\CKYC.Processor`      | The CLI executable (composition root, command registry, settings binding) |
| `scripts\sqlserver\`      | SQL Server schema (optional)                                              |
| `samples\`                | Example inputs (`customer.json`)                                          |
| `runtime\`                | Generated output, DB file, batch/FVU run artifacts                        |

The CLI entry point is `src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe`.

---

## 3. Building

**Recommended** (uses build.ps1, which forces single-threaded MSBuild so a restricted
sandbox doesn't block parallel worker nodes):

```powershell
cd <repo>\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

**Manual equivalent** (if you prefer calling dotnet directly):

```powershell
dotnet build src\CKYC.Processor\CKYC.Processor.csproj -c Release -p:NuGetAudit=false -m:1 -nodeReuse:false
```

Output:

```
src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe
```

### NuGet restore (important for a fresh machine)

The repo ships a `nuget.config` that points NuGet at a **local cache**
(`C:\Users\offic\.nuget\packages`) so restore works offline on the original machine. On
*your* box that path almost certainly won't exist. The only external packages are:

- `Microsoft.Data.Sqlite.Core` (10.0.11)
- `SQLitePCLRaw.bundle_e_sqlite3` (2.1.12)

Two options:

- **Simplest:** delete or neuter `nuget.config` and let `dotnet restore` pull from nuget.org:
  ```powershell
  Remove-Item nuget.config
  dotnet build src\CKYC.Processor\CKYC.Processor.csproj -c Release
  ```
- **Keep offline:** edit `nuget.config` and point `local-cache` / `globalPackagesFolder` at
  the folder where the packages already exist on your machine.

### Warnings are errors

`Directory.Build.props` sets `TreatWarningsAsErrors=true` plus
`EnableNETAnalyzers` / `AnalysisLevel=latest`. A single compiler or analyzer warning fails
the build. Keep the code clean (e.g. prefer the concrete type over `IReadOnlyList` when the
return value is always a list — CA1859).

---

## 4. Configuration — `appsettings.json`

| Section        | Key                              | Meaning                                                                 |
|----------------|----------------------------------|-------------------------------------------------------------------------|
| `database`     | `provider` / `connectionString`  | `sqlite` (default) or `sqlserver`; the connection string               |
|                | `createSchemaOnStartup`          | auto-create the schema on every start                                   |
| `source`       | `mode`                           | `generate` (make N ids deterministically) or `file` (read a file)      |
|                | `generateCount` / `generateSeed` | how many ids to generate and the seed                                  |
|                | `filePath`                       | the file used when `mode=file`                                         |
| `crm`          | `mode` / `baseUrl` / endpoints   | InProcess dummy API wiring vs a remote production CRM                  |
| `batch`        | fields                           | user id, FI code, region, client type, version, **sequenceStart**, output root |
| `fvu`          | `exePath` / `workspaceRoot`      | FVU executable + where run artifacts go                                 |
|                | `useRealFvu`                     | `true` → real exe; `false` → deterministic simulator                    |
| `simulation`   | knobs                            | deterministic `saveErrorEvery`, `saveErrorForCustomerId`, `fvuFailEvery`, CBS fetch retry knobs (`cbsFetchErrorsEnabled`, `cbsFetchFailEvery`, `cbsFetchFailForCustomerId`) |
| `retry`        | `maxAttempts` / `backoffBaseHours` / `backoffMultiplier` | default policy seeded into the `activity_type` master (3, 24h, ×2) |

> **Batch sequence note:** the batch filename uses `batch.sequenceStart`, which is a fixed
> start value (default `1`). It does **not** auto-increment, so a second run on the same date
> reuses the same sequence. In the test run I raised this to `2` so the new `RJKS2026` batch
> stayed separate from an earlier `_00001` batch instead of overwriting it.

---

## 5. Running the pipeline end to end

Every step is a separate invocation of the same exe:

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

# 1. load the daily customer ids into the master table
& $exe fetch cust

# 2. start the dummy CRM API (keep it running in a separate window / background job)
& $exe crm serve --urls http://127.0.0.1:5291

# 3. enrich + save from the CRM (every Nth save is simulated to fail to exercise retries)
& $exe store
& $exe retry

# 4. build the .UPL + zip from saved records
& $exe build-zip

# 5. submit to the FVU -> processed zip + hash
& $exe fvu

# inspect
& $exe status
```

`run.ps1` does all of this automatically (it launches the CRM in-process as a background
job, runs every step, then shuts the CRM down).

---

## 6. Providing your own customer id — `custid.json` (`fetch custid`)

Two ways to feed the source instead of the generated ids.

**A. `fetch custid` (reads `custid.json`):**

Create `custid.json` in the directory you run from (or next to the exe). The id can be given
as:

```json
{ "customerId": "RJKS2026" }
```

or a list:

```json
{ "customerIds": ["RJKS2026", "CUST123"] }
```

or simply an array:

```json
["RJKS2026", "CUST123"]
```

Then:

```powershell
& $exe fetch custid
```

This reads `custid.json` (looked up in the current directory, then the app directory) and
inserts those ids as `Pending` records in the master table. The written example uses
`{ "customerId": "RJKS2026" }`.

**B. Explicit file (`fetch cust --file`):**

```powershell
& $exe fetch cust --file .\custid.json
```

JSON and plain-text (one id per line) files are both supported; a `.json` file is parsed as
JSON, anything else is read line-by-line.

After `fetch`, run the rest as usual:
`store` → `retry` → `build-zip` → `fvu` → `status`.

---

## 7. Creating a brand-new record with your own details (`insert`)

Instead of the dummy CRM's auto-data, you can supply a full record:

```powershell
& $exe insert --file .\samples\customer.json
# or inline:
& $exe insert --customer-id CUST202608240099 --name "Ashish Kumar" --dob 15-04-1988 --gender M `
              --email ashish.kumar@yopmail.com --mobile 9876543210
```

Missing detail records (proof/address/contact/related/other) are filled with FVU-valid
defaults, so a minimal input still produces a batch. `build-zip` batches currently-`Saved`
records, so a freshly inserted record is batched on its own even when older records are
already `FvuPassed`.

> Keep values FVU-valid: country code `IN`, dates `DD-MM-YYYY`, a 20-character search key,
> and an existing referencing document file in `support_docs`.

---

## 8. The FVU step (step 5) — what to know

`CommandLineFvuRunner`:
1. clones the generated `.UPL` + `support_docs` into a per-run folder,
2. writes an FVU `config.yaml` (input/output/log/doc folders + API contract),
3. launches `FVU_RUN_UTILITY.exe` and parses its JSON summary + exit code,
4. locates the processed output zip and extracts the file-level SHA-256 from record-10.

Exit codes: `0` success, `2` config error, `3` validation failed, `1`/other fatal.

> **Sandbox / temp-extraction caveat:** `FVU_RUN_UTILITY.exe` is a PyInstaller bundle that
> extracts its runtime to the temp folder and auto-starts an embedded Spring Boot backend. On
> machines/CI where a file sandbox blocks writes to temp, the `fvu` step can exit `-1` with an
> empty tmp and no output. To fix it, run `fvu` with broader filesystem access (or outside the
> sandbox).

**If you don't have the FVU exe**, set `fvu.useRealFvu = false`. The `SimulatedFvuRunner`
produces the same output contract (processed zip + a SHA-256 hash) deterministically, so the
whole pipeline still runs:

```json
"fvu": {
  "useRealFvu": false
}
```

---

## 9. Retries, re-push (reattempt) and reconciliation

Three commands govern failed records. Only *some* activities are retryable — the CBS
customer-id fetch is the example — per the `activity_type` master (seeded with exponential
backoff of 24 hours doubling per failure, max 3 attempts).

```powershell
# retry failed records whose exponential backoff has elapsed (budget remaining)
& $exe retry
& $exe retry --activity CbsFetch

# re-push ONE rejected record after a manual backend DB fix
& $exe reattempt --customer CUST202608240001 --reason "PAN corrected in backend"
& $exe reattempt --id 42 --reason "Name mismatch resolved"

# report records needing manual intervention to a stakeholder
& $exe reconcile                       # all
& $exe reconcile --kind retry          # retry-exhausted only
& $exe reconcile --kind cersai         # CERSAI-failed only
& $exe reconcile --out recovery.csv --stakeholder "Operations"
```

- `retry` only re-runs a record when `master_record.NextRetryAt` is due and `RetryCount < max`
  (so the 24h exponential backoff is honoured). It logs each attempt (when + outcome) to
  `master_record_attempt` and, once the budget is exhausted, flags the record for
  reconciliation.
- `reattempt` snapshot the previous response/attempt (status, ack, CKYC ref/number, rejection
  remark, read timestamp) into `master_record_reattempt`, then resets the record to `Saved`
  (clearing `IsRejected` and the retry count) so it is re-batched and re-submitted.
- `reconcile` writes a CSV report (with a stakeholder header) of records that exhausted their
  retries and/or failed at CERSAI.

---

## 10. Verifying results

```powershell
& $exe status
```

shows master-table counts by status and the last batch. To inspect the DB directly:

```powershell
sqlite3 runtime\ckyc.db "SELECT Id,CustomerId,Status,BatchFile,Remarks FROM master_record;"
sqlite3 runtime\ckyc.db "SELECT BatchKey,ExitCode,Passed,HashValue FROM fvu_run ORDER BY Id DESC LIMIT 1;"
```

Generated + processed artifacts live under:

- `runtime\output\<BatchKey>\` — the `.UPL`, `support_docs`, and zip
- `runtime\runs\<BatchKey>\output\` — the FVU-processed zip, executive summary PDF, and hash

---

## 11. Troubleshooting quick reference

| Symptom                                            | Cause / fix                                                                                                        |
|----------------------------------------------------|--------------------------------------------------------------------------------------------------------------------|
| Build fails with a `CA…` warning                   | `TreatWarningsAsErrors` + analyzers are on. Fix the analyzer hint (e.g. return the concrete `List` type).            |
| Restore fails on a fresh machine                   | `nuget.config` points at a local cache. Point it at your packages or restore from nuget.org (see §3).               |
| `fvu` exits `-1` with empty tmp                     | The PyInstaller bundle couldn't write to temp (sandbox). Run with broader access, or set `fvu.useRealFvu=false`.    |
| `fvu` exits `3`                                     | A generated `.UPL` field failed validation. Check the per-run `.ERR` file / validation errors.                       |
| No `Pending` records in `store`                    | `fetch` didn't load ids. Run `fetch cust` / `fetch custid` first.                                                    |
| A record stays `Saved` and is never batched        | `build-zip` only batches `Saved` records. Ensure the record reached `Saved` (run `store`).                           |
