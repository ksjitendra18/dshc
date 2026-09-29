# CKYC Mock Run — current hardcoded / demo flow

> Companion to [`uatworkflow.md`](uatworkflow.md). This file documents **what I have right now**:
> the as-shipped, **mock/hardcoded** pipeline — its configuration, the fake data it produces, the
> exact commands to run it end-to-end, the expected results, and the offline overrides needed to
> make it run with only the demo fixtures. It is the "before integration" baseline.
>
> Repo root: `D:\ckyccentral\ckyc` · CLI: `src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe`

---

## 1. What is mocked (the seams)

| # | Step | Mock today | Real replacement (see `uatworkflow.md`) |
|---|---|---|---|
| 1 | Daily source / CBS fetch | `DailyCustomerIdProvider` **generate** mode (`CUST<yyyyMMdd><0001..>`) or `custid.json` | real daily feed / CBS provider |
| 2 | CRM | `DummyCrmDataProvider` + `DummyCrmLegalEntityProvider`, served by `crm serve` (Kestrel at `127.0.0.1:5291`); `HttpCrmApiClient` reads it | real CRM endpoint (`crm.baseUrl`) |
| 3 | Customer search | `DummyIndividualSearchApi` (in-process, deterministic) | `searchApi.mode=Http` real search API |
| 4 | Channel image/doc | `SftpDocumentSource` simulated inbox (`useRealSftp=false`) / placeholder SFTP | real beckyc SFTP |
| 5 | FVU | `useRealFvu=false` → `SimulatedFvuRunner` (fake `.validated` + hash, always passes) | real `FVU_RUN_UTILITY.exe` |
| 6 | CERSAI SFTP | `useRealSftp=false` → `SimulatedSftpRunner` (no transfer) | real `SFTPRunner.exe` + creds |
| – | Save failures | `simulation.saveErrorsEnabled` injects a failure every Nth save | off |
| – | Supporting docs | rendered from sample templates in `doc_format\` (QuestPDF) | real templates/calibration |

Everything else (record tables, `.UPL`/`.SRC`/`.UPD` writers, response parsers, retry/reconcile) is
the real code path — the mock only replaces the **external calls** and the **source data**.

---

## 2. Shipped mock configuration (`appsettings.json`)

| Section | Shipped (mock) value |
|---|---|
| `database` | `sqlserver`, `Server=(localdb)\MSSQLLocalDB;Database=CkycCentral;Trusted_Connection=True;TrustServerCertificate=True` |
| `source` | `mode=generate`, `generateCount=12`, `generateSeed=20260824` |
| `crm` | `mode=InProcess`, `baseUrl=http://127.0.0.1:5291`, endpoints `/api/customers/{id}`, `/api/customers` |
| `batch` | `userId=IRA000337`, `fiCode=IN9797`, `regionCode=12345`, `clientType=I`, `versionNumber=V1.1`, `sequenceStart=64`, `outputRoot=D:\centralprocessing\ckyc\runtime\output` |
| `fvu` | `exePath=D:\centralprocessing\vendor\FVU_RUN_UTILITY.exe`, `workspaceRoot=D:\centralprocessing\ckyc\runtime`, `useRealFvu=true` |
| `response` | `inputRoot=D:\centralprocessing\cersai\response` |
| `simulation` | `saveErrorsEnabled=true`, `saveErrorEvery=4`, `fvuFailEvery=0`, `cbsFetchErrorsEnabled=false`, `cbsFetchFailEvery=0` |
| `retry` | `maxAttempts=3`, `backoffBaseHours=24`, `backoffMultiplier=2.0` |
| `search` | `userId=IRA000337`, `fiCode=IN9797`, `regionCode=12345`, `sequenceStart=4`, `outputRoot=D:\centralprocessing\ckyc\runtime\search` |
| `searchApi` | `enabled=true`, `mode=InProcess`, `baseUrl=http://127.0.0.1:5292`, `searchEndpoint=/api/search`, `simulateFoundEvery=3` |
| `documentGeneration` | `enabled=true`, `templateRoot=doc_format`, 3 docs: `D1.pdf`(static), `AdhaarAP.pdf`(Aadhaar), `C3.pdf`(consent) |
| `documentFetch` | `enabled=true`, beckyc `useRealSftp=true`, `host=sftp-beckyc.example.in`, empty user/pass, `basePath=x/y/z`, remote `image.jpg` |
| `sftp` | `enabled=true`, `exePath=D:\ckyccentral\...\SFTPRunner.exe`, `host=https://sftp-preprod.ckycindia.dev`, `username=IAU010054`, `password=""`, `fiCode=IN0031`, `useRealSftp=true` |

> This is a **hybrid** shipped state: the CRM and search are pure mocks, but the FVU and SFTP flags
> are `true` against placeholder paths/credentials, and beckyc is a placeholder host. Section 4
> gives the overrides that make the whole thing run as a **pure mock**.

---

## 3. Hardcoded mock data you'll see

### Generated source ids (mode=generate)

`CUST<yyyyMMdd>0001 … CUST<yyyyMMdd>0012` for the business date (today by default).
`generateSeed` is declared but unused.

### `DummyCrmDataProvider` (individual)

Deterministic per customer id (SHA-256 of the id picks the values):

- 12 fixed names, e.g. `Mr. Amrish Puri`, `Ms. Priya Sharma`, `Mr. Rahul Kumar Verma`, …
- Mobile `98########` (8 digits from the hash); email `<first>.<last>@yopmail.com`
- Search key `IMO` + 17 digits (exactly 20 chars); PAN `ABCP#####A`
- DOB derived from the hash (`DD-MM-1970..2009`); gender `M`; Aadhaar masked to 4 digits
- Fixed permanent address: `B-109 Man Deep CHS LTD / Navghar Road / Saibaba Nagar, Bhayandar, MH, 401106` (district `225`)
- Current address `ABC / CCC / CCC … 401107`
- Related party: `Assignee` with a generated 14-digit CKYC number
- **Record 70 constants:** `EmployeeName=Anusaya`, `EmployeeCode=A236`, `EmployeeBranch=Kamlamills`,
  `InstitutionName=PhonePe_Limited`, `InstitutionCode=IN0238`, `Place=Mumbai`,
  `DeclarationDocument=D1.pdf`, `ClientConsent=C3.pdf`; dates = today

### `DummyIndividualSearchApi`

- Every **3rd** customer (by stable hash, `simulateFoundEvery=3`) is `Found` with a fake reference
  `R` + 13 digits → record ends at `SearchFound`.
- Everyone else is `NotFound` with a synthetic key `ISR` + 17 digits → written to record 20 → `Searched`.

### `DummyCrmLegalEntityProvider` (legal)

`Meridian Legal Entity <n>`, PAN 4th char derived from the constitution, `contact<n>@meridiansoft.com`,
Mumbai/Pune addresses, fixed proof-document filenames, same record-70 constants as above.

### Sample fixtures

| File | Customer id(s) | Use |
|---|---|---|
| `custid.json` | `RJKS2026` (dockey `9f2c7a4e81b3d6f0a5c2`) | `fetch custid` |
| `samples\retail-customer.json` | `CUST-RETAIL-SKSS` | `insert --file` (fully specified) |
| `samples\customer.json` | `CUST202608240099` | `insert --file` (no family name → skipped at batch) |
| `samples\document-fetch-source.json` | `RJKS2026`, `CUST202608240001/2` (+dockeys) | `fetch cust --file` with beckyc source |
| `samples\legal-entity-create.json` | `ENT-LEGAL-DEMO-001` | `insert-legal` |
| `samples\search_customer.json` | `CUST-0001..3` | `search-load` |
| `samples\update-individual.json` / `update-legal.json` | `CUST202608240001/2`, `CUST-LEGAL-ACME` | `update-load` |
| `staging\CUST-RETAIL-SKS[ S ]\` | – | `AdhaarAP.pdf`, `C3.pdf`, `D1.pdf`, `Photo.jpg` for `documents import` |
| `staging\ENT-LEGAL-PROP-001\` | – | legal docs for `documents import` |

---

## 4. Overrides to run as a **pure mock** (no network)

Because the shipped FVU/SFTP/beckyc flags point at real/placeholder tooling, a clean offline mock run
needs these edits in `appsettings.json` (keep a copy as `appsettings.mock.json` and pass
`--settings appsettings.mock.json`, remembering it is a **full replacement**):

```jsonc
{
  "fvu":    { "useRealFvu": false, "exePath": "", "workspaceRoot": "runtime" },
  "sftp":   { "enabled": false },
  "documentFetch": { "enabled": false },          // OR use the simulated inbox (5b)
  "simulation": { "saveErrorsEnabled": true, "saveErrorEvery": 4 },
  "batch":  { "outputRoot": "runtime/output", "sequenceStart": 64 },
  "search": { "outputRoot": "runtime/search" },
  "documentFetch.downloadRoot": "runtime/document-fetch"
}
```

Notes:

- With `documentFetch.enabled=false`, `store` leaves records at **`PendingSearch`** (no image gate).
- With `sftp.enabled=false`, `fvu` marks records **`Uploaded`** directly (legacy routing) and the
  `sftp` commands refuse to run.
- Anything still pointing at `D:\centralprocessing\...` writes outside the repo — repoint to
  `runtime\...` or `D:\ckyccentral\...`. See `uatworkflow.md` §B12.

### 4b. Simulated beckyc inbox (if you want to exercise the image step offline)

```jsonc
"documentFetch": {
  "enabled": true,
  "channels": { "beckyc": { "enabled": true, "useRealSftp": false, "basePath": "x/y/z",
    "documents": [ { "remote": "image.jpg", "target": "Photo.jpg", "slot": "photoOfIndividual" } ] } }
}
```

Place a file at `runtime\document-fetch\inbox\beckyc\<dockey>\image.jpg` (for `RJKS2026` the dockey is
`9f2c7a4e81b3d6f0a5c2`), then `store` → `IMP` → `documents fetch` → `PendingSearch`.

---

## 5. Build + provision (once)

```powershell
cd D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "IF DB_ID('CkycCentral') IS NOT NULL BEGIN ALTER DATABASE [CkycCentral] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [CkycCentral]; END; CREATE DATABASE [CkycCentral];"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\schema.sql
```

---

## 6. Mock run recipes

### Recipe A — one-shot script (`run.ps1`)

Starts the mock CRM as a background job, runs 8 steps, stops it.

```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

Flow: `fetch cust` → `crm serve` → `store` → `retry` → `documents fetch` → `search-customer` →
`build-zip` → `fvu` → `status`.

### Recipe B — manual full mock (sample `RJKS2026`)

```powershell
# 1. source -> master (Pending)
& $exe fetch custid

# 2. mock CRM (keep running in a separate window / background job)
& $exe crm serve --urls http://127.0.0.1:5291

# 3. enrich + save (every 4th save simulates a failure)
& $exe store
& $exe retry                       # recovers the simulated save failures

# 4. image step (only if documentFetch is on; otherwise already PendingSearch)
& $exe documents fetch --customer RJKS2026

# 5. customer search -> Searched or SearchFound
& $exe search-customer

# 6. batch + validate
& $exe build-zip
& $exe fvu

# 7. inspect
& $exe status
& $exe batch-find --customer RJKS2026
```

### Recipe C — insert-only smoke (no CRM needed for the record itself)

```powershell
& $exe insert --file .\samples\retail-customer.json        # CUST-RETAIL-SKSS -> PendingSearch
& $exe documents import --customer-id CUST-RETAIL-SKSS --dir .\staging\CUST-RETAIL-SKSS
& $exe search-customer
& $exe build-zip
& $exe fvu
& $exe status
```

Inline variant:

```powershell
& $exe insert --customer-id CUST-RETAIL-0001 --name "Ashish Kumar" --dob 15-04-1988 --gender M `
              --email ashish.kumar@yopmail.com --mobile 9876543210
& $exe search-customer
& $exe build-zip
& $exe fvu
```

### Recipe D — generator source (12 mock customers)

```powershell
& $exe fetch cust                  # CUST<today>0001..0012 -> Pending
& $exe crm serve --urls http://127.0.0.1:5291
& $exe store
& $exe retry
& $exe search-customer
& $exe build-zip
& $exe fvu
& $exe status
```

### Recipe E — search / update / legal / response mock flows

```powershell
# .SRC bulk search
& $exe search-load .\samples\search_customer.json
& $exe search-process
& $exe search-fvu
& $exe search-response

# .UPD bulk update
& $exe update-load .\samples\update-individual.json
& $exe update-load .\samples\update-legal.json
& $exe update-process
& $exe update-fvu
& $exe update-response

# legal entity create
& $exe insert-legal --file .\samples\legal-entity-create.json
& $exe build-zip-legal
& $exe fvu

# CERSAI reply (mock)
& $exe response read
```

---

## 7. Expected state after each step (mock)

| Step | Master status | Notes |
|---|---|---|
| `fetch` (generate/custid) | `PND` (Pending) | 12 generated ids, or `RJKS2026` |
| `store` success | `SRP` (PendingSearch) — or `IMP` if a channel source is configured | simulated save failures → `FLD` |
| `retry` | back to `SRP` (or `SAV`/`SRD`) | honours 24h×2 backoff; use the drill below to force it |
| `documents fetch` | `SRP` | `IMF` if the file is missing/failed |
| `search-customer` | `SRD` (Searched) **or** `SRF` (SearchFound) | key in record 20, or CKYC reference |
| `build-zip` | `BAT` (Batched) | `.UPL` + `support_docs` zip under `batch.outputRoot\<BatchKey>` |
| `fvu` (mock) | `UPL` (Uploaded) when `sftp.enabled=false`; `FVP` when `sftp.enabled=true` | simulated processed zip + hash |
| `sftp push`/`pull` | `UPL` | needs `sftp.enabled=true` |
| `response read` | `RCN` / `REJ` | from `.UPL.RESm` replies |
| insert | `SRP` (individual) / `SAV` (legal) | individual needs `search-customer` before batch |

Artifacts: `runtime\output\<BatchKey>\` (`.UPL`, `support_docs`, zip), `runtime\runs\<BatchKey>\output\`
(FVU output + summary), `runtime\search\`, `runtime\update\`, `runtime\sftp\*`, logs in `logs\`.

---

## 8. Failure drills (mock)

Fixtures in `samples\failure\`:

| File | Purpose |
|---|---|
| `ids.json` | 9 ids including `CUST-CBS-FAIL-01`, `CUST-FLOW-0002..0008`, `TEST-SAVE-FAIL-01` |
| `settings-clean.json` | baseline: no simulation (note `versionNumber=V1.0` — **would fail FVU ERR_172**; raise to `V1.1`) |
| `settings-fetch-cbs-fail.json` | `cbsFetchErrorsEnabled=true`, `cbsFetchFailEvery=3`, `CbsFailForCustomerId=CUST-CBS-FAIL-01` |
| `settings-save-every.json` | simulated save error every Nth record |
| `settings-save-fail.json` | a specific customer id always fails the save |
| `settings-search-fail.json` | simulated search API failure |
| `records\valid-0001..0005.json` | valid seeds |
| `records\{no-dob,no-family,pan-no-doc,address-incomplete}.json` | records that should be rejected/skipped |
| `response\response-template.RES0` | CERSAI reply template with `<R20LINE>` placeholder |
| `scripts\expedite-retry.sql` | zeroes the 24h backoff so `retry` is due immediately (re-run between attempts) |
| `scripts\print-upl-20-lines.ps1` | prints record-20 line numbers from a batch |

**CBS-fetch failure drill:**

```powershell
& $exe --settings .\samples\failure\settings-fetch-cbs-fail.json fetch cust --file .\samples\failure\ids.json
& $exe --settings .\samples\failure\settings-fetch-cbs-fail.json store
& $exe --settings .\samples\failure\settings-fetch-cbs-fail.json retry --activity CbsFetch
```

**Retry drill (expedite the backoff):**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\samples\failure\scripts\expedite-retry.sql
& $exe retry
# re-run expedite-retry.sql between attempts to force the next one due
```

> **Watch out:** the `settings-*.json` overrides are **full replacements** and carry
> `versionNumber=V1.0` plus `D:\centralprocessing\...` paths. Bump to `V1.1` and fix the roots if you
> intend to batch/validate with them.

---

## 9. Mock SQL shortcuts

```sql
-- inspect stage
SELECT Id, CustomerId, ClientType, Status, StatusCode, BatchFile, DocumentKey, LastError, RetryCount, NextRetryAt
FROM master_record ORDER BY Id;

-- force-search / rese
UPDATE master_record SET Status = 12, StatusCode = 'SRP' WHERE CustomerId = 'RJKS2026';

-- bulk-skip search for all pending individuals -> batchable
UPDATE master_record SET Status = 13, StatusCode = 'SRD' WHERE ClientType = 'I' AND Status = 12;

-- list a batch's record-20 lines
SELECT CustomerId, BatchFile, Record20Line FROM master_record WHERE BatchFile IS NOT NULL;

-- FVU history
SELECT TOP (5) BatchKey, ExitCode, Passed, HashValue FROM fvu_run ORDER BY Id DESC;
```

---

## 10. Known mock limitations (what it does NOT prove)

- The mock search never calls an external CKYCR; `SearchFound` is synthetic.
- The mock CRM serves fixed names/addresses and **hardcodes record-70 attestation values**
  (`Anusaya`/`A236`/`Kamlamills`/`PhonePe_Limited`/`IN0238`/`Mumbai`).
- The simulated FVU always passes and injects a fake hash; it does **not** validate `.UPL` fields.
- The simulated SFTP performs no transfer; `Uploaded` is bookkeeping only.
- There is **no legal-entity CRM `store`** — legal entities only come from `insert-legal`.
- `insert`/`insert-legal` default-fill from the dummy providers (concrete classes), so their
  "defaults" are mock-only.
- `crm.mode` is not read by code — the CRM client is always HTTP; the mock comes from `crm serve`.

For the real-integration to-do list, see [`uatworkflow.md`](uatworkflow.md) Part B.
