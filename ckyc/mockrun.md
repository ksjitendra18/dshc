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

---

## 11. Running from `retail-customer.json` while the CRM is not ready (with the provider image)

Yes — while the CRM is not prepared, feeding a record directly with
`samples\retail-customer.json` is the right path. `insert` does **not** call the CRM server:
the omitted/default fields come from the **in-process** dummy provider (`ctx.CrmData`), so you do
**not** need `fetch`, `crm serve` or `store`. This section is the exact step-by-step, and how to
hand the provider's photo so the **Aadhaar EKYC report is generated with the photo overlaid**.

### 11.1 Why this works (and the one thing to know)

- `insert` creates the master row with the default intake channel **`beckyc`**
  (`MasterRecordSourceValue.Default`). The `documentGeneration` Aadhaar document is configured for
  `channels: [ "beckyc" ]`, so **it applies** — the Aadhaar report will be rendered at `build-zip`.
- `insert` leaves the record at **`PendingSearch`**, so you **must** run `search-customer` before
  `build-zip` (which only batches `Searched` records).
- Document generation happens at `build-zip` and writes the rendered bytes straight into the batch's
  `support_docs`; it does **not** read the CRM. The only thing it needs from outside is the
  **customer photo** (overlay) — everything else is drawn from the template + record fields.

### 11.2 What `retail-customer.json` (`CUST-RETAIL-SKSS`) references

`build-zip` collects the filenames in the record via `DocumentReferences.For` and requires each one
to exist (in the store, or as a generated document). For this sample:

| Record field | File name | How it is satisfied |
|---|---|---|
| `photoOfIndividual` | `Photo.jpg` | **You must supply this** (the provider photo) — it is the image overlaid on the Aadhaar report |
| `proofs[0].copyOfOvd` (OVD `E`) | `AdhaarAP.pdf` | **Generated** at `build-zip` (Aadhaar renderer) → supersedes any stored copy |
| `other.declarationDocument` | `D1.pdf` | **Generated** at `build-zip` (static undertaking) |
| `other.clientConsent` | `C3.pdf` | **Generated** at `build-zip` (consent renderer) |
| `pan` | (no `panDocument`) | not referenced |

> `AdhaarAP.pdf`/`D1.pdf`/`C3.pdf` are produced by `documentGeneration`; `Photo.jpg` is **not** —
> it has to be imported. That is the "image from the provider".

### 11.3 Prepare a mock settings file (so it never touches the real FVU/SFTP)

Copy `appsettings.json` to `appsettings.mock.json` and set:

```jsonc
{
  "fvu": { "useRealFvu": false },     // simulated FVU (fake processed zip + hash, always passes)
  "sftp": { "enabled": false },       // `fvu` marks records Uploaded directly; sftp commands disabled
  "documentGeneration": { "enabled": true, "templateRoot": "doc_format" }   // keep generation ON
}
```

> Run from the repo root (`D:\ckyccentral\ckyc`) so `templateRoot: doc_format` resolves.
> Remember `--settings` is a **full replacement**, not a merge — that is why you copy the whole
> file and only edit the keys above.

### 11.4 Step-by-step

```powershell
cd D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

# (once) fresh DB
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "IF DB_ID('CkycCentral') IS NOT NULL BEGIN ALTER DATABASE [CkycCentral] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [CkycCentral]; END; CREATE DATABASE [CkycCentral];"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\schema.sql

# 1) put the provider photo + supporting files in the staging folder (see 11.5)
#    staging\CUST-RETAIL-SKSS\  ->  Photo.jpg (+ AdhaarAP.pdf, D1.pdf, C3.pdf)

# 2) create the record (no CRM server needed)
& $exe --settings .\appsettings.mock.json insert --file .\samples\retail-customer.json
#    -> master CUST-RETAIL-SKSS is PendingSearch (SRP)

# 3) import the referenced documents (the provider image lands here)
& $exe --settings .\appsettings.mock.json documents import --customer-id CUST-RETAIL-SKSS --dir .\staging\CUST-RETAIL-SKSS
#    -> file_content + individual_document; Photo.jpg stored as image/jpeg

# 4) customer search (SRP -> Searched)
& $exe --settings .\appsettings.mock.json search-customer --customer CUST-RETAIL-SKSS

# 5) build the batch -> renders AdhaarAP.pdf (photo overlaid), C3.pdf, D1.pdf into support_docs
& $exe --settings .\appsettings.mock.json build-zip
#    note the printed BatchKey / Upload file / Zip path

# 6) validate (simulated)
& $exe --settings .\appsettings.mock.json fvu

# 7) inspect
& $exe --settings .\appsettings.mock.json status
& $exe --settings .\appsettings.mock.json batch-find --customer CUST-RETAIL-SKSS
```

### 11.5 How to give the provider image so the Aadhaar is generated properly

The Aadhaar EKYC report is a **blank template** (`doc_format\Aadhaar_template_blank.jpg`,
1095 × 1549 px) onto which the record's values are printed, and the customer's **photo is overlaid**
into the printed photo frame. The renderer
(`src\CKYC.Files\Documents\AadhaarDocumentRenderer.cs`) resolves the photo like this:

1. `documentGeneration.enabled = true`, the document `kind = Aadhaar`, and the record's channel is
   `beckyc` → applies.
2. `overlayPhoto = true` (default) and `record.PhotoOfIndividual` is non-empty.
3. `DocumentGenerationService.ResolvePhotoAsync` looks up that **file name in the document store**
   for the master record and uses it **only if its media type starts with `image/`**.

So the provider image must end up in the document store under the name the record references.

**Do this:**

1. Take the **customer photograph** from the provider (a JPEG is preferred; PNG also works if you
   rename the reference — see below).
2. Save it into the staging folder using the **exact file name** the record references:
   ```
   staging\CUST-RETAIL-SKSS\Photo.jpg      <- provider photo (content = JPEG)
   ```
   The repo already ships the other referenced files in `staging\CUST-RETAIL-SKSS\`
   (`AdhaarAP.pdf`, `D1.pdf`, `C3.pdf`); keep them there so `documents import` reports no missing
   files. Optionally replace `AdhaarAP.pdf` with the provider's real Aadhaar scan — the rendered
   report still supersedes it in the batch.
3. Run `documents import` (step 3 above). This stores `Photo.jpg` as `image/jpeg` for the record.
4. Run `search-customer` → `build-zip`. The generated `AdhaarAP.pdf` in the batch's `support_docs`
   now has the provider photo inside the frame at template coords `(100, 214)` size `209 × 207`.

**If the provider photo is a PNG (or the name differs):**

- The extension **must match the content** (a PNG byte stream stored as `.jpg` fails the signature
  check). Either convert it to JPEG and name it `Photo.jpg`, **or** name it `Photo.png` and change
  the record before insert:
  ```jsonc
  // samples\retail-customer.json
  "photoOfIndividual": "Photo.png"
  ```
  then stage `Photo.png` and import.
- Any of `.jpg`, `.jpeg`, `.png` are accepted by the document store; `.pdf` is accepted but is **not**
  an image, so it will **not** be overlaid (the template photo is kept).

**Verify the photo was stored (optional):**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -Q "SELECT d.OriginalFileName, d.MediaType, d.ByteLength FROM individual_document d JOIN master_record m ON m.Id = d.MasterRecordId WHERE m.CustomerId = 'CUST-RETAIL-SKSS';"
```

**Verify the generated Aadhaar:** open
`runtime\output\<BatchKey>\support_docs\AdhaarAP.pdf` (also inside the batch zip) and confirm the
photo sits in the top-left frame and the printed values (name, masked Aadhaar `XXXX XXXX <last4>`,
gender, DOB, address) match the record.

### 11.6 Troubleshooting this path

| Symptom | Cause / fix |
|---|---|
| `AdhaarAP.pdf` not in `support_docs` | Channel is not `beckyc` (inserted records default to `beckyc`, so this usually means a different source), or `documentGeneration.enabled=false`, or the document's `channels` no longer includes `beckyc`. |
| Warning `Document template not found: '...doc_format\Aadhaar_template_blank.jpg'` | Ran the exe from outside the repo root; run from `D:\ckyccentral\ckyc` or set `templateRoot` to an absolute path. |
| Aadhaar generated but **no photo** | `photoOfIndividual` empty, or the stored file's media type is not `image/*` (e.g. a PDF), or `overlayPhoto=false`. |
| Record skipped at `build-zip` | `Photo.jpg` (or another reference) was never imported; run `documents import` again and check the "missing referenced file" lines. |
| `documents import` exits `1` with "Missing referenced file" | Every referenced file must be staged. The referenced set is `Photo.jpg`, `AdhaarAP.pdf`, `D1.pdf`, `C3.pdf` — stage all of them (the repo ships them). It still imports whatever is present. |
| `The content signature does not match image/jpeg` | The file's extension does not match its bytes (e.g. PNG data named `.jpg`). Rename to match the content, or convert. |
| Record stuck at `PendingSearch` and not batched | `search-customer` was not run (or `--customer` had no eligible record). `build-zip` batches only `Searched`. |

> Note: `.UPL` output goes to `batch.outputRoot`. If your copy of `appsettings.json` still points at
> `D:\centralprocessing\ckyc\runtime\output`, repoint it (or set `batch.outputRoot`) so the batch
> lands inside the repo. See `uatworkflow.md` §B12.
