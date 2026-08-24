# Centralized CKYC Processor

A production-ready reference implementation of a centralized **CKYC (Central KYC Record
Registry)** batch-processing pipeline. It is a `.NET 10` solution invoked as a single CLI
executable (`CKYCProcessor.exe`) with independent, deployable sub-commands — one per
pipeline step — so every stage can be run on its own and orchestrated by a scheduler:

```
CKYCProcessor.exe fetch cust     # 1. source customer ids   -> master table (CBS fetch; retryable)
CKYCProcessor.exe insert         #    create a NEW customer record (manual details)
CKYCProcessor.exe crm serve      # 2. dummy CRM API         (replace with production)
CKYCProcessor.exe store          # 3. CRM -> record tables  (with simulated error saving)
CKYCProcessor.exe retry          #    retry failed records (exponential backoff, max 3 tries)
CKYCProcessor.exe reattempt      #    re-push a single rejected record after a backend DB fix
CKYCProcessor.exe build-zip      # 4. saved records -> .UPL file + zip
CKYCProcessor.exe fvu            # 5. batch -> FVU -> processed zip + hash
CKYCProcessor.exe response read  # 6. CERSAI reply (.UPL.RESm) -> response table + master summary
CKYCProcessor.exe reconcile      #    manual-intervention report (retry-exhausted + CERSAI-failed)
CKYCProcessor.exe status         #    pipeline snapshot (current stage per record)
```

The pipeline was verified **end-to-end against the real CERSAI `FVU_RUN_UTILITY.exe`**:
a generated 12-record batch validated successfully (exit code `0`) and produced the
processed `.zip` plus the file-level SHA-256 hash.

---

## Architecture

| Project           | Responsibility                                                                 |
|-------------------|--------------------------------------------------------------------------------|
| `CKYC.Core`       | Domain models (`Individual`, record types 20/30/40/50/60/70), settings, CKYC file-format spec, contracts (repositories / CRM / batch / FVU / hashing). |
| `CKYC.Data`       | SQLite persistence — schema bootstrap, master-table repository, record-table repository, batch/FVU audit journal. |
| `CKYC.Crm`        | Dummy CRM: `DummyCrmDataProvider` (deterministic fake data), `HttpCrmApiClient`, and a self-hosted Kestrel API (`CrmServer`). |
| `CKYC.Files`      | `CkycUploadWriter` (pipe-delimited .UPL per the validated field layout), `CkycBatchGenerator` (writes .UPL + supporting docs + zip), hashing. |
| `CKYC.Fvu`        | `FvuConfigGenerator` (writes the FVU `config.yaml`), `CommandLineFvuRunner` (subprocess integration + JSON/exit-code parsing + hash extraction), deterministic simulation fallback. |
| `CKYC.Processor`  | CLI host, composition root, command registry, `appsettings.json` binding.      |

### Independent processes
Every command is a separate invocation of the same executable, so each pipeline step is an
independently deployable unit. The CRM API (`crm serve`) runs as its own long-lived process
and is reached over HTTP, so swapping in the production CRM is a one-line config change.

---

## Data model

The schema uses **length-only** column definitions (plus an identity primary key) and
deliberately **no NOT NULL / UNIQUE / CHECK / FK constraints** — as requested ("length
validation yes, other validation no"). All tables are created on startup.

- `master_record` — step 1: daily source customer ids + a **single current-stage** `Status`
  (Pending → CrmFetched → Saved → Batched → Uploaded → ResponseRead → Reconciled/Rejected),
  per-stage `Is*`/`*At` flags and timestamps, `Remarks`, `RetryCount` / `LastError` /
  `LastAttemptAt`, the batch file + record-20 line, the latest CERSAI reply summary
  (`LastResponse*`), and reconciliation fields (`ReconStatus`/`ReconRemarks`).
- `master_record_response` — one row per (record, response-file-number): every CERSAI reply
  detail read, with ack number, record status, CKYC reference/number, rejection remark,
  read-at, and the raw line.
- `master_record_attempt` — audit trail of every stage attempt (CRM fetch, store, batch,
  FVU upload, response read, reconciliation), so retries at any stage are traceable. Each row
  is anchored to an `activity_type` row and records when the attempt was processed, the
  attempted/outcome, and the next exponential-backoff time.
- `activity_type` — the **activity type master**: which processes can be retried (only some,
  e.g. the CBS customer-id fetch) and the retry policy (exponential backoff from 24 hours,
  doubling per failure, max 3 attempts).
- `status_master` — the **status master**: maps the `master_record.Status` integer (0–10) to a
  short 2–3 char code (`PND`, `SAV`, `FVP`, …), the enum name, and a readable description, so
  reports can show a compact code and description without changing the numeric storage.
- `master_record_reattempt` — one row per **manual re-push (reattempt)** of a rejected record,
  snapshotting the previous response (status, ack, CKYC ref/number, rejection remark and the
  read date/timestamp) together with the reset flag state.
- `kyc_record_20` — demographics (record type 20).
- `kyc_record_30` — proof of identity & address (record type 30).
- `kyc_record_40` — address, permanent + current (record type 40).
- `kyc_record_50` — contact (record type 50).
- `kyc_record_60` — related party (record type 60).
- `kyc_record_70` — other details & attestation (record type 70).
- `batch`, `fvu_run` — audit trail of generated batches and FVU runs.

The SQL Server equivalent is in `scripts/sqlserver/schema.sql` (set `database.provider`
= `sqlserver` and supply a matching connection string).

---

## CKYC file format

The bulk-upload `.UPL` is a pipe-delimited file of records identified by a leading number:

| Record | Meaning                | Field count (individual) |
|--------|------------------------|--------------------------|
| `10`   | header                 | 11 (FVU appends version + hashes → 13) |
| `20`   | demographics           | 56 |
| `30`   | proof of identity/addr | 22 |
| `40`   | address                | 46 |
| `50`   | contact                | 10 |
| `60`   | related party          | 6 |
| `70`   | other / attestation    | 23 |

The writer (`CkycUploadWriter`) reproduces the reference field positions from the CERSAI
sample and injects the CRM-derived fields (search key, name, DOB, address, contact, etc.),
keeping the FVU-validated defaults for flag/count positions. File naming follows:
`<ClientType>_<UserID>_<FICODE>_<DDMMYYYY>_<nnnnn>.UPL`.

---

## FVU integration (step 5)

`CommandLineFvuRunner`:
1. clones the generated `.UPL` and its `support_docs` into a per-run input folder,
2. writes the FVU `config.yaml` (input/output/log/doc folders + API contract),
3. launches `FVU_RUN_UTILITY.exe` as a child process, capturing stdout/stderr,
4. parses the JSON summary (`totalFiles/success/failed/summaryPdf`) and the exit code
   (`0` success, `2` config, `3` validation failed, `1` fatal),
5. locates the processed output `.zip` and extracts the **file-level hash** from the
   validated record-10 header (field `[12]`),
6. returns a `FvuRunResult` (executed, exit code, passed, summary, output zip, hash, errors).

The FVU is fully self-contained — it bundles and auto-starts its own Spring Boot backend
JAR and embedded JDK 21, so no separate backend is required. In environments where the
EXE is unavailable, set `fvu.useRealFvu=false` to use the deterministic `SimulatedFvuRunner`
that produces the same output contract.

> **Note:** `FVU_RUN_UTILITY.exe` is a PyInstaller bundle. On machines/CI where a file
> sandbox blocks writes to the temp extraction folder, run the `fvu` command with access
> to the system temp (or in a non-sandboxed context).

---

## CERSAI response processing (step 6)

After a batch is submitted, CERSAI returns one or more **response (reply) files** named
`<upload>.UPL.RESm` where `m` is the response-file number (0, 1, 2, …), so a batch can
produce several responses over time.

```
response read [--batch <key>] [--dir <folder>] [--file <path>]
```

- Defaults to the **last generated batch** and its FVU output folder
  (`<fvu.workspaceRoot>/runs/<batchKey>/output`).
- The reply format (record `90` header + record `100` detail) is documented in the
  `Upload_response` sheet of `File_Format_Upload_Individual_1.0.xlsx`.
- Each reply detail is matched back to its master record by the record-20 line number, then:
  1. written to `master_record_response` (idempotent — re-reading a file updates in place),
  2. mirrored onto the master row's `LastResponse*` columns and `Status` →
     `ResponseRead`,
  3. advanced to `Reconciled` (status `01`/`02`) or `Rejected` (rejection remark present).

Every response read is also recorded in `master_record_attempt` with a stage of `Response`,
so the full history of "what happened in response 0 / 1 / 2, at what time, with what remarks"
is available from the two tables.

---

## Retries, re-push (reattempt) and reconciliation

Three related mechanisms keep failed work moving and auditable.

### Retries (`retry`)

Only **some** processes can be retried — the nightly **CBS customer-id fetch** is the
canonical example. The `activity_type` master lists each process with its policy:

| Activity  | Retryable | Max attempts | Backoff           |
|-----------|-----------|--------------|-------------------|
| `CbsFetch`| yes       | 3            | exponential 24h×2 |
| `Crm`/`Store` | yes  | 3            | exponential 24h×2 |
| `BuildZip`/`FvuUpload`/`Response`/`Reconciliation` | no | — | — |

A failed retryable attempt computes the next attempt as
`now + baseHours × multiplier^(attempt−1)` (24h, then 48h, before the 3rd try) and stores it in
`master_record_attempt.NextRetryAt` and the master row. `retry` only picks up records whose
backoff has elapsed, so nothing is hammered.

```powershell
& $exe retry                        # all retryable activities due now
& $exe retry --activity CbsFetch    # only the CBS fetch
```

Once a record exhausts its 3 attempts it is flagged `NeedsReconcile` and surfaces in the
`reconcile` report.

### Re-push / reattempt (`reattempt`)

When CERSAI rejects a record for a **minor** issue (e.g. a PAN typo) and you fix it directly
in the backend database, re-push that **single** record:

```powershell
& $exe reattempt --customer CUST202608240001 --reason "PAN corrected in backend"
# or by master-record id:
& $exe reattempt --id 42 --reason "Name mismatch resolved"
```

The processor (1) snapshots the **previous response/attempt** — status, ack, CKYC ref/number,
rejection remark and the exact read date/timestamp — into `master_record_reattempt`, then
(2) flips the record back to `Saved` (clearing `IsRejected` and the retry budget) so it flows
through `build-zip` → `fvu` → `response read` again.

### Reconciliation (`reconcile`)

Records that have **exhausted their retry attempts** (and those that **failed at CERSAI**)
need manual intervention. Serve them as a report to the respective stakeholder:

```powershell
& $exe reconcile                                     # all records needing intervention
& $exe reconcile --kind retry                        # retry-exhausted only
& $exe reconcile --kind cersai                       # CERSAI-failed only
& $exe reconcile --out recovery.csv --stakeholder "Operations"
```

The report is written to a CSV (one row per record with its status, retry/attempt history and
latest CERSAI reply) and printed to the console.

---

## Configuration

Configuration lives in `appsettings.json` (JSON). Key sections:

- `database` — provider (`sqlite`/`sqlserver`) + connection string.
- `source` — daily customer-id source (`generate` seed/count or a `file` of ids).
- `crm` — CRM base URL + endpoints (point at production to replace the dummy).
- `batch` — user id, FI code, region, client type (`I`), version (`V1.0`), output root.
- `fvu` — FVU executable path, workspace root, API contract, `useRealFvu`.
- `simulation` — deterministic knobs: `saveErrorsEnabled`, `saveErrorEvery` (every Nth save
  fails to exercise the retry path), `saveErrorForCustomerId`, `fvuFailEvery`, plus the CBS
  fetch retry simulation (`cbsFetchErrorsEnabled`, `cbsFetchFailEvery`,
  `cbsFetchFailForCustomerId`) that makes the fetch fail for a subset of ids.
- `retry` — default retry policy used to seed `activity_type`: `maxAttempts` (3),
  `backoffBaseHours` (24), `backoffMultiplier` (2.0).

---

## Building

Requires the .NET 10 runtime/SDK. The build is single-threaded here to avoid the sandbox
blocking MSBuild's parallel worker nodes; `build.ps1` encodes the flags.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The CLI output is `src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe`.

> The repo ships a `nuget.config` that points NuGet at the local package cache so restore
> works offline (the only external package is `Microsoft.Data.Sqlite.Core` + bundle).

---

## Running the pipeline

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

# 1. fetch daily customer ids -> master table
& $exe fetch cust

# 2. start the dummy CRM API (keep running in a separate window / background job)
& $exe crm serve --urls http://127.0.0.1:5291

# 3. enrich + save; every Nth save is simulated to fail to exercise retries
& $exe store
& $exe retry

# 4. generate the batch (.UPL + zip) from saved records
& $exe build-zip

# 5. submit to the FVU -> processed zip + hash (records become Uploaded / pending at CERSAI)
& $exe fvu

# 6. read the CERSAI reply (records advance to ResponseRead / Reconciled / Rejected)
& $exe response read

# inspect
& $exe status
```

`run.ps1` runs the whole flow (it launches the CRM in-process as a background job,
orchestrates every step, then shuts it down).

## Create a new customer record (`insert`)

To add a customer with your **own** details (rather than the dummy CRM's auto-data), step by
step:

1. Make a copy of `samples/customer.json` and edit the fields you care about (name, DOB,
   email, mobile, address, etc.). Missing detail records (proof / address / contact /
   related party / other) are auto-filled with FVU-valid defaults, so a minimal input still
   works.
2. (Optional) start the CRM once so `insert` can borrow the valid defaults, or supply every
   detail record in the JSON so no CRM is needed:
   ```powershell
   $exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
   & $exe crm serve --urls http://127.0.0.1:5291   # only if you rely on defaults
   ```
3. Insert the record:
   ```powershell
   & $exe insert --file .\samples\customer.json
   # or fully inline:
   & $exe insert --customer-id CUST202608240099 --name "Ashish Kumar" --dob 15-04-1988 --gender M --email ashish.kumar@yopmail.com --mobile 9876543210
   ```
4. Generate the batch and submit to the FVU:
   ```powershell
   & $exe build-zip
   & $exe fvu
   ```
5. Inspect:
   ```powershell
   & $exe status
   ```

Notes:
- The inserted record becomes `Saved`; `build-zip` batches **currently-Saved** records, so a
  freshly inserted record is batched on its own even when older records are already
  `FvuPassed`.
- Keep values FVU-valid: country code `IN`, dates `DD-MM-YYYY`, a 20-character search key,
  and an existing referencing document file in `support_docs`.

---

## Swapping in production

- **CRM** — point `crm.baseUrl` at the real endpoint; `HttpCrmApiClient` and the `Individual`
  contract stay unchanged.
- **Database** — set `database.provider=sqlserver` + connection string and run
  `scripts/sqlserver/schema.sql`.
- **FVU** — keep `fvu.useRealFvu=true`; point `fvu.exePath` and `fvu.workspaceRoot` at the
  deployment folders. The FVU integration code is already production-oriented.
