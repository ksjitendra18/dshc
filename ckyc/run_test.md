# run_test.md — Failure-Simulation Test Run for the CKYC Processor

A ready-made test drill for the centralized CKYC batch pipeline. Seed records,
settings overrides and helper scripts are shipped under `samples/failure/` so
every pipeline step can be exercised **with an injected failure** — including
the "document not available" case, both at the FVU validation gate and as a
CERSAI rejection remark.

Nothing here is executed by the seeds themselves: the files only **stage** the
data/configuration. You run the pipeline exactly as shown below and observe the
failure, the retry/recovery (or the manual-intervention report) at each step.

---

## 1. Scenario index — one failure per pipeline step

| # | Pipeline step | Command | Failure injected | Seed / settings used |
|---|---------------|---------|------------------|----------------------|
| T1 | 1. CBS customer-id fetch | `fetch` | CBS call fails for a subset of ids (retryable) | `ids.json` + `settings-fetch-cbs-fail.json` |
| T2 | 3. CRM → record save | `store` + `retry` | a targeted save always fails → retry budget exhausted → reconcile | `ids.json` + `settings-save-fail.json` |
| T3 | 2./3. CRM API | `crm serve` / `store` | CRM API not running → `store` fails with a connection error | `settings-clean.json` (no CRM started) |
| T4 | 3. CRM → record save | `store` + `retry` | every 4th save fails once, then recovers on retry | `ids.json` + `settings-save-every.json` |
| T5 | 4. Batch generation | `build-zip` | records fail pre-flight validation → excluded (`Skipped`) | `records/no-dob.json`, `pan-no-doc.json`, `no-family.json`, `address-incomplete.json` |
| T6 | 5. FVU upload | `fvu` | **supporting document not available** (deleted from the batch folder) → FVU validation fails | 5× `records/valid-*.json` |
| T7 | 6. CERSAI reply | `response read` | reply rejects records, incl. **"DOCUMENT NOT AVAILABLE"**, then `reattempt` re-push | `response/response-template.RES0` |

Status shorthand used below: `PND` Pending · `SAV` Saved · `BAT` Batched ·
`UPL` Uploaded · `FVP` FvuPassed · `FVF` FvuFailed · `FLD` Failed ·
`RCN` Reconciled · `REJ` Rejected (see `docs/status-master.md`).

---

## 2. Prerequisites

```powershell
# 0) Always run every command from the ckyc project folder.
Set-Location D:\centralprocessing\ckyc

# 1) The CLI must be built (dotnet 10 SDK required):
.\build.ps1

# 2) Convenience variable used throughout this manual:
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
$s   = ".\samples\failure"          # seed root
$sqlite3 = "D:\Programs\sqlite3\sqlite3.exe"
```

- **FVU (steps 5–6):** the real `FVU_RUN_UTILITY.exe` is used
  (`fvu.useRealFvu=true` in every settings file) and takes a couple of minutes
  per run (it boots an embedded backend). On a machine where a file sandbox
  blocks its temp extraction, run the `fvu` command outside the sandbox /
  elevated (see `README.md`). Scenario T6 **requires the real FVU** — the
  deterministic simulated runner (`useRealFvu=false`) always passes and cannot
  reproduce a missing-document failure.
- **sqlite3 CLI:** only needed for the retry “expedite” step (`expedite-retry.sql`).

---

## 3. Test hygiene — start from a clean slate

Each scenario below is written against a **fresh database** so the batch
composition and counts are predictable:

```powershell
# Stop any `crm serve` process first (Ctrl+C / Stop-Process on the window).
Remove-Item .\runtime\ckyc.db -ErrorAction SilentlyContinue   # schema is re-created on startup
Remove-Item .\runtime\output\* -Recurse -ErrorAction SilentlyContinue
Remove-Item .\runtime\runs\* -Recurse -ErrorAction SilentlyContinue
```

> Note: `fetch cust` prefers a `custid.json` sitting in the current folder or
> beside the exe and would then read only that one id. This manual always uses
> `fetch --file ...\ids.json` for a deterministic set.

---

## 4. Scenarios

### T1 — Step 1: CBS fetch failure, then retry recovery

```powershell
& $exe --settings "$s\settings-fetch-cbs-fail.json" fetch --file "$s\ids.json"
```

Expected — 3 of the 9 ids fail the CBS call (the targeted
`CUST-CBS-FAIL-01` plus every 3rd id) and are parked as `Failed` (FLD) with a
retry schedule; `exit code 1`:

```
[fetch] Reading 9 customer id(s) from '...\ids.json'
[fetch] [CUST-CBS-FAIL-01] CBS fetch FAILED (retry 1) -> next retry 2026-...Z
[fetch] [CUST-FLOW-0003]   CBS fetch FAILED (retry 1) -> next retry 2026-...Z
[fetch] [CUST-FLOW-0006]   CBS fetch FAILED (retry 1) -> next retry 2026-...Z
[fetch] Inserted=6  Skipped=0  Total=6  CbsFailed=3
```

Verify with `& $exe status` → `Pending : 6`, `Failed : 3`.

The backoff is seeded at **24 h** (hard-coded in `src/CKYC.Data/Schema/Ddl.cs`),
so before retrying we make the failed records due immediately:

```powershell
& $sqlite3 .\runtime\ckyc.db ".read $s\scripts\expedite-retry.sql"

& $exe --settings "$s\settings-fetch-cbs-fail.json" retry --activity CbsFetch
```

Expected — every CBS-failed record is re-attempted and succeeds:

```
[retry]   CbsFetch: 3 record(s) due for retry...
[retry]   CUST-CBS-FAIL-01: CBS fetch re-attempted and succeeded -> Pending
[retry]   CUST-FLOW-0003:   CBS fetch re-attempted and succeeded -> Pending
[retry]   CUST-FLOW-0006:   CBS fetch re-attempted and succeeded -> Pending
[retry] Done: Attempted=3  Succeeded=3  PermanentFailed=0  Skipped/DueLater=0
```

`status` → `Pending : 9`. The pipeline can continue with `store`.

---

### T2 — Step 3: targeted save failure → budget exhausted → reconcile

Retries only fire when the backoff has elapsed; here the targeted record
`TEST-SAVE-FAIL-01` fails **every** attempt, so it burns all 3 attempts and
lands in the manual-intervention report.

```powershell
& $exe --settings "$s\settings-save-fail.json" fetch --file "$s\ids.json"

# Start the dummy CRM API (keep it running — separate window / background job):
& $exe crm serve --urls http://127.0.0.1:5291
#   (health-check: Invoke-WebRequest http://127.0.0.1:5291/health)

& $exe --settings "$s\settings-save-fail.json" store
```

Expected — 8 saved, `TEST-SAVE-FAIL-01` fails (retry 1):

```
[store] [TEST-SAVE-FAIL-01] FAILED (retry 1) [Store]: Simulated database save error (training scenario) -> next retry ...
[store] Done: Succeeded=8  Failed=1  Total=9
```

Now run the retry cycle 3× (expedite the backoff before each attempt):

```powershell
& $sqlite3 .\runtime\ckyc.db ".read $s\scripts\expedite-retry.sql"
& $exe --settings "$s\settings-save-fail.json" retry          # attempt 2 -> fails again (DueLater)
& $sqlite3 .\runtime\ckyc.db ".read $s\scripts\expedite-retry.sql"
& $exe --settings "$s\settings-save-fail.json" retry          # attempt 3 -> exhausted
```

Expected on attempt 3:

```
[retry]   Store: 1 record(s) due for retry...
[retry]   TEST-SAVE-FAIL-01 FAILED (retry 3) [Store]: ... -> flagged for reconciliation
[retry] Done: Attempted=1  Succeeded=0  PermanentFailed=1  Skipped/DueLater=0
```

Verify the manual-intervention report:

```powershell
& $exe --settings "$s\settings-save-fail.json" reconcile --kind retry
```

Expected — one row with `retry=3` and the last error:

```
[reconcile] 1 record(s) need manual intervention -> ...\reconciliation_....csv
  [<id>] TEST-SAVE-FAIL-01  Failed  retry=3  reattempt=0  exhausted retries (last error: Simulated database save error (training scenario))
```

---

### T3 — Step 2: CRM API down → `store` fails

Do **not** start `crm serve`. The enrich step calls the CRM over HTTP
(`crm.baseUrl`), so the save run crashes with a connection error and no record
is marked saved:

```powershell
& $exe --settings "$s\settings-clean.json" fetch --file "$s\ids.json"
& $exe --settings "$s\settings-clean.json" store
```

Expected:

```
[store] Processing 9 pending master record(s) through the CRM...
ERROR: ... (System.Net.Http.HttpRequestException: ... 'Connection refused' ...)
```

(exit code 1; with `saveErrorsEnabled:false` only the one-line error is shown —
set it to `true` to also print the full stack). Records stay `Pending`; start
the CRM (`& $exe crm serve --urls http://127.0.0.1:5291`) and re-run `store`
to confirm recovery.

---

### T4 — Step 3: simulated save errors (every Nth) recover on retry

The classic "error-saving" scenario: every 4th save fails once, and a single
retry recovers it.

```powershell
& $exe --settings "$s\settings-save-every.json" fetch --file "$s\ids.json"
# (CRM must be running — see T2)
& $exe --settings "$s\settings-save-every.json" store
```

Expected — 2 of 9 fail (positions 4 and 8 → `CUST-FLOW-0004`,
`CUST-FLOW-0008`):

```
[store] [CUST-FLOW-0004] FAILED (retry 1) [Store]: Simulated database save error (training scenario) -> next retry ...
[store] [CUST-FLOW-0008] FAILED (retry 1) [Store]: Simulated database save error (training scenario) -> next retry ...
[store] Done: Succeeded=7  Failed=2  Total=9
```

Recover them:

```powershell
& $sqlite3 .\runtime\ckyc.db ".read $s\scripts\expedite-retry.sql"
& $exe --settings "$s\settings-save-every.json" retry
```

Expected — the re-run of a single record does not hit the "every Nth" rule, so
both succeed:

```
[retry]   Store: 2 record(s) due for retry...
[store] [CUST-FLOW-0004] saved: ...
[store] [CUST-FLOW-0008] saved: ...
[retry] Done: Attempted=2  Succeeded=2  PermanentFailed=0  Skipped/DueLater=0
```

---

### T5 — Step 4: build-zip pre-flight validation skips

Records that fail the conditional-mandatory rules are **excluded from the
batch** (they stay `Saved`) and reported — this is the pipeline refusing to
ship a broken `.UPL`. `insert` itself succeeds for all of them (insert only
requires a lettered name + an id); the rules are enforced at `build-zip`.

```powershell
# Fresh DB (section 3). Inserts need no CRM.
& $exe --settings "$s\settings-clean.json" insert --file "$s\records\no-dob.json"
& $exe --settings "$s\settings-clean.json" insert --file "$s\records\pan-no-doc.json"
& $exe --settings "$s\settings-clean.json" insert --file "$s\records\no-family.json"
& $exe --settings "$s\settings-clean.json" insert --file "$s\records\address-incomplete.json"
foreach ($n in 1..5) { & $exe --settings "$s\settings-clean.json" insert --file "$s\records\valid-000$n.json" }

& $exe --settings "$s\settings-clean.json" build-zip
```

Expected — 5 valid records batched, 4 skipped with their exact rule violations:

```
[build-zip] Batch 'I_IAU010441_IN0238_..._0000X' generated with 5 record(s).
[build-zip]   Skipped     : 4 record(s) failed validation and were excluded:
    ! TEST-FAIL-NODOB-01 (Rakesh Mehta)
        - [20/Date of Birth] Date of Birth is mandatory.
    ! TEST-FAIL-PAN-01 (Vijay Malhotra)
        - [20/PAN Document] PAN supporting document is mandatory when PAN is provided.
    ! TEST-FAIL-FAM-01 (Divya Rao)
        - [20/Mother / Father / Spouse Name] At least one of Mother Name, Father Name or Spouse Name must be provided.
    ! TEST-FAIL-ADDR-01 (Sachin Deshmukh)
        - [40/Permanent Address State / UT] ... (6 errors, one per missing address field)
```

`status` → `Saved : 4` (the skipped ones stay Saved — fix the seed and re-run
`build-zip` to include them), `Batched : 5`.

---

### T6 — Step 5: supporting document **not available** (FVU rejects the batch)

The batch generator auto-creates a placeholder for every referenced document
(`Pan.pdf`, `AdhaarAP.jpg`, `D1.pdf`, `C3.pdf`, …). Simulating the
"document not available" failure means removing one from the batch's
`support_docs` **before** the FVU run — the FVU's SupportDocPath check then
fails validation and the records land in `FvuFailed` (FVF).

```powershell
# Fresh DB. Insert 5 valid records and batch them (no CRM needed).
foreach ($n in 1..5) { & $exe --settings "$s\settings-clean.json" insert --file "$s\records\valid-000$n.json" }
& $exe --settings "$s\settings-clean.json" build-zip
#   note the batch key printed, e.g. I_IAU010441_IN0238_24082026_00003

# Stage the failure: make Pan.pdf unavailable to the FVU run.
Remove-Item ".\runtime\output\<BATCHKEY>\upload\support_docs\Pan.pdf"

& $exe --settings "$s\settings-clean.json" fvu
```

Expected — the FVU exits non-zero (exit code `3` = validation failed) and the
batch's records become `FvuFailed`:

```
[fvu] Executed=True  ExitCode=3  Passed=False
[fvu]   files=1 success=0 failed=1 ... 
[fvu]   error       : One or more input files failed validation
[fvu]     ! record=20 ... <document / support-doc error reported by the FVU build> ...
```

(exit code 1). The exact error wording/error-code comes from the FVU release;
observe it in the `.ERR` / JSON errors it prints. Then:

```powershell
& $exe status                          # FvuFailed(FVF) : 5
& $exe --settings "$s\settings-clean.json" reconcile --kind cersai
```

Expected — all 5 listed as "failed at CERSAI" (FVU-side failures surface in
the CERSAI reconciliation feed by design).

> The simulated runner (`useRealFvu=false`) always passes and cannot produce
> this failure — the real FVU EXE is required (see section 2).

---

### T7 — Step 6: CERSAI reply rejects records — **"DOCUMENT NOT AVAILABLE"** + re-push

A seeded reply file (`response/response-template.RES0`, format records 90/100)
is read back like the real `*.UPL.RESm` CERSAI output: two records reconcile
(status `01`/`02`) and three are **rejected with remarks**, including
**DOCUMENT NOT AVAILABLE**.

```powershell
# Fresh DB. 5 valid records -> batch -> FVU (docs intact) -> records become Uploaded.
foreach ($n in 1..5) { & $exe --settings "$s\settings-clean.json" insert --file "$s\records\valid-000$n.json" }
& $exe --settings "$s\settings-clean.json" build-zip     # note <BATCHKEY> and the .UPL path
& $exe --settings "$s\settings-clean.json" fvu           # passes -> Uploaded (UPL)
```

The reply must point at the right customers, so fill the template with the
batch's **record-20 line numbers** (the values `build-zip` stored as
`master_record.BatchRecordLine`) — one per customer, in batch order:

```powershell
.\samples\failure\scripts\print-upl-20-lines.ps1 -Path ".\runtime\output\<BATCHKEY>\upload\<UPL>.UPL"
# e.g.  (file line 2)  record20 line 1    name Ashish Kumar
#       (file line 8)  record20 line 7    name Priya Sharma  ... etc.
```

Edit `samples\failure\response\response-template.RES0`: replace the five
`<R20LINE>` placeholders with the printed `record20 line` values, in order (1st
detail ← 1st printed customer, … 5th detail ← 5th printed customer). Then read
the reply:

```powershell
& $exe --settings "$s\settings-clean.json" response read --file "$s\response\response-template.RES0"
```

Expected:

```
[response] Reading 1 response file(s) for batch '...' (upload: ...UPL)...
[response]   response-template.RES0 (resp #0) header: total=5 processed=3 pending=2 failed=0 ts=...
[response]     TEST-VALID-0001 reconciled status=02 ack=ACK-00001 ...
[response]     TEST-VALID-0002 reconciled status=01 ack=ACK-00002 ...
[response]     TEST-VALID-0003 REJECTED: DOCUMENT NOT AVAILABLE
[response]     TEST-VALID-0004 REJECTED: PAN MISMATCHED WITH PAN DATABASE
[response]     TEST-VALID-0005 REJECTED: NAME MISMATCHED WITH OVD
[response] Done: files=1 details=5 matched=5 unmatched=0 reconciled=2 rejected=3
```

Verify and serve the manual-intervention report:

```powershell
& $exe status                             # Reconciled : 2, Rejected : 3
& $exe --settings "$s\settings-clean.json" reconcile --kind cersai
```

Expected — 3 rows, each carrying its CERSAI rejection remark (including
`DOCUMENT NOT AVAILABLE`).

Finally, demonstrate the re-push: simulate that the backend was fixed (here:
the supporting document is now attached), snapshot the previous CERSAI reply
into `master_record_reattempt` and flip the record back to `Saved` (SAV) so it
flows through `build-zip` → `fvu` → `response read` again:

```powershell
& $exe --settings "$s\settings-clean.json" reattempt --customer TEST-VALID-0003 --reason "Resubmitting with supporting document attached"
```

Expected:

```
[reattempt] Re-pushing TEST-VALID-0003 (record #<id>)
[reattempt]   prior status=Rejected reconStatus=... retryCount=0
[reattempt]   prior rejection remark: DOCUMENT NOT AVAILABLE
[reattempt]   previous response snapshotted to master_record_reattempt; record reset to Saved.
```

Now `& $exe build-zip` re-batches it (only that record is `Saved`), re-run
`fvu` and `response read` with a fresh `.RES` to complete the loop.

---

## 5. Known gotchas (read before you panic)

- **Record-20 family name rule.** The validator requires at least one of
  Mother / Father / Spouse name. The dummy CRM data **and the shipped
  `samples/customer.json` carry none**, so with the current build those records
  are excluded at `build-zip` (the `TEST-FAIL-FAM-01` seed demonstrates
  exactly this). Use the `valid-*.json` seeds (they include a `motherName`)
  for anything that must reach the batch.
- **24 h retry backoff.** `activity_type` is seeded with backoff
  `24h × 2^(attempt-1)`, max 3 attempts (`src/CKYC.Data/Schema/Ddl.cs`), so a
  virgin `retry` run right after a failure prints "none due". Re-run
  `expedite-retry.sql` before each retry attempt to make everything due
  immediately (test-only; it also zeroes the backoff for subsequent attempts).
- **`fetch cust` vs `custid.json`.** `fetch cust` reads a `custid.json` found
  in the current folder or next to the exe (the repo ships one with
  `RJKS2026`). Always use `fetch --file samples\failure\ids.json` in this
  manual.
- **`insert` guardrails.** `insert` refuses a record whose first name has no
  letter and requires a source customer id — finer-grained rules are only
  enforced at `build-zip` (that is why the failing seeds above are inserted
  fine but `Skipped` later).
- **FVU output location.** `response read` without `--file` scans
  `runtime\runs\<batchKey>\output\` for files named `<upload>.UPL.RESm`; the
  `--file` form used here works with any path (the `.RESm` suffix still gives
  the response-file number).
- **Fresh DB per scenario group.** `build-zip` batches all currently-`Saved`
  records, so mixing scenarios without a reset merges datasets into one batch.

---

## 6. Seed inventory (`samples/failure/`)

| Path | Purpose |
|------|---------|
| `ids.json` | 9 customer ids for `fetch --file` (targeted CBS/save failure ids included) |
| `settings-fetch-cbs-fail.json` | full config; CBS simulation on: every 3rd id + `CUST-CBS-FAIL-01` fail |
| `settings-save-fail.json` | full config; `TEST-SAVE-FAIL-01` always fails at `store` |
| `settings-save-every.json` | full config; every 4th `store` save fails (recovers on retry) |
| `settings-clean.json` | full config; all failure simulations off (control runs) |
| `records/valid-0001.json` … `valid-0005.json` | valid records (incl. motherName) for batch/FVU/response scenarios |
| `records/no-dob.json` | `build-zip` skip — Date of Birth missing |
| `records/pan-no-doc.json` | `build-zip` skip — PAN given, PAN document missing |
| `records/no-family.json` | `build-zip` skip — no Mother/Father/Spouse name |
| `records/address-incomplete.json` | `build-zip` skip — permanent address without State/District/City/Pin |
| `response/response-template.RES0` | CERSAI reply template: 2 reconciled + 3 rejected (`DOCUMENT NOT AVAILABLE`, `PAN MISMATCHED…`, `NAME MISMATCHED…`) |
| `scripts/print-upl-20-lines.ps1` | prints the record-20 line numbers needed to fill the `.RES` template |
| `scripts/expedite-retry.sql` | makes failed retryable records due immediately (backoff = 0) |

All settings files are full `appsettings.json` copies, so a `--settings <path>`
override replaces the whole configuration — point `fvu.exePath`/`workspaceRoot`
at your deployment folders if they differ.