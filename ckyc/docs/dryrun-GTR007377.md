# Dry run — beckyc image fetch for a minor customer aged 10–17 (`GTR007377` / dockey `0871808577544332`)

Exercises the **pre-search image step** end-to-end for **one minor aged 10–17** beckyc customer,
fully offline and without touching the existing database:

```
fetch --file  ->  insert --file  ->  (image gate)  ->  documents fetch (beckyc .txt)
              ->  search-customer  ->  build-zip (+ Aadhaar / consent / declaration generation)
              ->  fvu  ->  status
```

| | |
|---|---|
| Customer id | **`GTR007377`** |
| Document key (dockey) | **`0871808577544332`** |
| Record profile | **minor, aged 10–17**: `kycType = M` (Minor Account), `minor = Y`, DOB `15-08-2012` (14 y), **no guardian** — record 60 is optional above ten |
| Run from | `D:\ckyccentral\ckyc` |
| Database | dedicated LocalDB **`CkycCentral_DryRun`** (the existing `CkycCentral` is not modified) |
| CRM | not used — `insert` fills omissions from the in-process dummy provider |
| beckyc transport | `useRealSftp = false` → reads `runtime\document-fetch\inbox\beckyc\<dockey>\*.txt` |
| Batch documents | `Photo.jpg` **fetched** from beckyc + `AdhaarAP.pdf` (Aadhaar generation), `C3.pdf` (consent generation), `D1.pdf` (static declaration) **rendered at `build-zip`** from `doc_format\` — see §1d |
| Batch sequence | `batch.sequenceStart = 65` → `…_00065`, so the under-ten run's `…_00064` is not overwritten |
| FVU | `useRealFvu = false` → deterministic local simulation; the real-FVU variant is **verified `Passed=true`** (§5) |

Everything that matters — the dockey → folder mapping, the base64 `.txt` decode, the real-extension
sniff, the `photoOfIndividual` slot repoint, the image gate (`IMP` → `SRP`), the Aadhaar photo
overlay and the Aadhaar/consent/declaration renderers — is the **real** code path. Only the fetched
document is persisted to the document store; the three generated documents are rendered into the
batch's `support_docs` at `build-zip` and are not written to the database.

## The three beckyc dry runs

| | Adult (`GTR007375`) | Minor under 10 (`GTR007376`) | Minor 10–17 (`GTR007377`) ← this guide |
|---|---|---|---|
| `kycType` / `minor` | `N` / `N` | `M` / `Y` | **`M` / `Y`** |
| DOB | 30-07-1977 | 12-06-2019 (7 y) | **15-08-2012 (14 y)** |
| Guardian (record 60) | not present | **mandatory** (`Guardian`) | **optional — sample omits it** |
| PAN | supplied + verified | none → Form 97 | **none → Form 97** |
| OVD | E (Aadhaar) | only A/E allowed — E | **only A/E allowed — E** |
| `search-customer` rows | 3 (Aadhaar, PAN, name+DOB) | 2 (Aadhaar, name+DOB) | **2 (Aadhaar, name+DOB)** |
| Batch sequence | `…_00064` | `…_00064` | **`…_00065`** |

---

## Files used

| File | What it is | Who fills it |
|---|---|---|
| `samples\dryrun-GTR007377-source.json` | Step-1 source record: customer id + **dockey** + channel | already filled in |
| `samples\dryrun-GTR007377-customer.json` | The **child's details** written by `insert` | **you** (replace with the real details) |
| `samples\settings-dryrun-GTR007377.json` | Full settings override (dedicated DB, repo-local paths, offline SFTP/FVU, clean simulation, absolute `documentGeneration.templateRoot`, **`batch.sequenceStart = 65`**) | ready to use |
| `samples\settings-dryrun-GTR007377-realfvu.json` | Same override with `fvu.useRealFvu = true` (real `FVU_RUN_UTILITY.exe`); **verified `Passed=true`** | ready to use |
| `scripts\make-dryrun-beckyc-txt.ps1` | Writes/wraps the beckyc `.txt` payload into the local inbox; pass `-CustomerId GTR007377 -Dockey 0871808577544332` | ready to use |
| `doc_format\Aadhaar_template_blank.jpg` · `Consent_template_blank.png` · `Template_1.pdf` | Templates `build-zip` fills — Aadhaar EKYC report / client consent / static declaration | shipped with the repo |
| `docs\image.md` · `docs\document-fetch.md` | Background on the beckyc layout / fetch step | reference |

> `--settings` is a **full replacement**, not a merge, so every command below passes the same
> override file; nothing else changes.
> Step 1 recreates `CkycCentral_DryRun`, so any record from earlier runs is dropped. The batch
> output folders on disk are kept apart by the per-run `sequenceStart` (`_00064` vs `_00065`).

---

## 0. One-time setup

Build once and set the two variables:

```powershell
Set-Location D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1

$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
$st  = ".\samples\settings-dryrun-GTR007377.json"
```

Create the isolated dry-run database and apply the schema (the only DDL; it does **not** touch
`CkycCentral`):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "IF DB_ID('CkycCentral_DryRun') IS NOT NULL BEGIN ALTER DATABASE [CkycCentral_DryRun] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [CkycCentral_DryRun]; END; CREATE DATABASE [CkycCentral_DryRun];"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -i .\scripts\sqlserver\schema.sql
```

> `schema.sql` already includes the document-fetch objects (`master_record.DocumentKey`, statuses
> `15 IMP` / `16 IMF`, the `ImageFetch` activity). On an *existing* database apply
> `scripts\sqlserver\migrations\20260928_add_document_fetch.sql` instead.

---

## 1. What you fill in

### 1a. The beckyc payload (the dockey folder)

Either write a valid placeholder, or wrap the child's real photo:

```powershell
# placeholder (valid 1x1 JPEG as a beckyc data:image/jpeg;base64,… .txt)
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1 -CustomerId GTR007377 -Dockey 0871808577544332

# OR a real photo (JPEG/PNG/PDF auto-detected)
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1 -CustomerId GTR007377 -Dockey 0871808577544332 -Source C:\path\to\photo.jpg
```

Result:

```
D:\ckyccentral\ckyc\runtime\document-fetch\inbox\beckyc\0871808577544332\0871808577544332.txt
```

The folder name is the **dockey**, not the customer id — that is the point of the dry run.

> Prefer a raw image? Copy `Photo.jpg` into the same folder and change the channel's `documents`
> entry in the settings file to `"pattern": "*.jpg"` (see `docs\image.md` §3B).

### 1b. The customer details (`samples\dryrun-GTR007377-customer.json`)

This file ships ready to run. Replace the placeholder values with the real child's details. Keep it
**FVU-valid**; the fields that matter for a minor are:

| Field | Rule |
|---|---|
| `customerId` | must stay `GTR007377` (matches the fetched master row + dockey) |
| `searchKey` | any 20-character key; `search-customer` rewrites it with the `ISR…` key it returns |
| `kycType` | **`M`** for a Minor Account (the FVU rejects `N` for an under-18 person — `ERR_360`) |
| `minor` | **`Y`** (the writer derives it from the DOB when omitted, so keep it consistent) |
| `dateOfBirth` | `DD-MM-YYYY`; **under 18** for a minor; **10 or older** avoids the mandatory guardian (or keep a guardian anyway — see §1c) |
| `name.firstName` | letters, apostrophe and dot only; **no spaces**; title `Ms` / `Mr` / `Mx` |
| `motherName` / `fatherName` / `spouseName` | at least **one** family name is mandatory |
| `gender` | `M` / `F` / `T` |
| `genderProvidedInOvd`, `genderMatchWithOvd`, `dateOfBirthMatchWithOvd`, `nameMatchWithOvd`, `photoProvidedMatchWithOvd` | `Y`/`N` (all `Y` in the sample) |
| `residentialStatus` | `Resident` (or `NRI` / `PIO` / `ForeignNational`) + `residentialStatusSupportedByDocument` `Y`/`N` |
| `nationality` | `IN` + `nationalitySupportedByDocument` `Y` |
| `differentlyAbledStatus` | `Y`/`N` (`N` unless the PwD block applies) |
| OVD (`proofs[].ovdType`) | **only `A` (Passport) or `E` (Aadhaar/VID) is allowed for KYC type M**. The sample uses `E` with `modeOfAadhaarVerification = B`, `lengthOfAadhaar = A`, a 4-digit masked `idNumber` and `copyOfOvd = AdhaarAP.pdf` |
| PAN / Form 97 / Form 61 | **exactly one** of the three. The child has no PAN → `"form97Provided": "Y"`; do **not** also set `pan` or `form61Provided` (the FVU rejects two with `ERR_044`) |
| `relatedParties[]` | optional above ten — the sample omits it (no record 60); see §1c |
| `photoOfIndividual` | `Photo.jpg` — the fetch repoints this slot at the file it pulls |

Everything else (current address, contact, attestation/`other`) is optional: when omitted `insert`
borrows FVU-valid defaults from the in-process dummy provider. The record's document references are
satisfied in the batch like this:

| Reference | Record slot | Satisfied by |
|---|---|---|
| `Photo.jpg` | `photoOfIndividual` | **beckyc fetch** (step 4 — the `.txt` decoded + sniffed) |
| `AdhaarAP.pdf` | `proofOvd`, `currentAddressOvd` | **Aadhaar generation** at `build-zip` (§1d) |
| `C3.pdf` | `clientConsent` | **consent generation** at `build-zip` (§1d) |
| `D1.pdf` | `declarationDocument` | **static declaration** at `build-zip` (§1d) |

> If you add a `panDocument` (or any other extra document reference), place that file too — either
> stage it and `documents import`, or `build-zip` will skip the record.

### 1c. Guardian (record 60) — optional in this age band

The FVU format makes **guardian details mandatory only for a client below 10** years of age. For a
10–17 minor they are optional, and this sample deliberately omits them:

```jsonc
// under ten (see docs\dryrun-GTR007376.md) — required:
"relatedParties": [
  { "relatedPersonType": "Guardian", "ckycNumberOfRelatedPerson": "<14-digit CKYC of the guardian>" }
]
```

* Above ten, omitting `relatedParties` is valid — the `.UPL` simply carries **no record 60** and
  `insert` reports `0 related party(ies)`.
* If the real customer does name a guardian, add the block; the writer then emits one record `60`
  (`Guardian` / `Assignee` / `Authorized Representative` + the guardian's 14-character CKYC number).
* `insert` and `build-zip` enforce the below-ten rule automatically; nothing extra is needed here.

### 1d. The generated supporting documents — Aadhaar · consent · static declaration

`documentGeneration` (enabled in both dry-run settings files) renders three documents at
`build-zip` and injects them straight into the batch's `support_docs`. **Yes — all of them are
generated on every run**; nothing needs to be staged or imported:

| File | Generation | Template | Slots | Channels |
|---|---|---|---|---|
| `AdhaarAP.pdf` | **Aadhaar** | `Aadhaar_template_blank.jpg` | `proofOvd`, `currentAddressOvd` | `beckyc` |
| `C3.pdf` | **consent** | `Consent_template_blank.png` | `clientConsent` | `beckyc` |
| `D1.pdf` | **static declaration** | `Template_1.pdf` | `declarationDocument` | every channel |

* The Aadhaar renderer prints the record's name, masked Aadhaar (`XXXX XXXX <last4>`), gender, DOB
  and permanent address onto the blank report, and overlays the customer's photo — read from the
  record's `photoOfIndividual` (i.e. the file fetched in step 4) — inside the printed frame. The
  rendered PDF therefore contains **two image objects** (the blank report + the photo) and the
  drawn text.
* The consent renderer prints the client name, relation (father → mother → spouse name), reporting
  entity, Aadhaar no, PAN and declaration date onto the Annexure-1 template (the Aadhaar/PAN ticks
  are drawn only when those values are present — a Form 97 minor has **no PAN**, so the PAN value
  and tick stay blank).
* The static declaration is the template attached **byte-for-byte unchanged** (`D1.pdf` is
  identical to `doc_format\Template_1.pdf`).
* Generation is deterministic and in-memory: the bytes are **not** written to the document store,
  the stored record rows are not modified, and every `build-zip` re-renders from the current
  record data. A generated file **supersedes** a stored/imported copy with the same name.
* `templateRoot` must point at the `doc_format` folder. Both dry-run settings pin it to the
  absolute `D:\ckyccentral\ckyc\doc_format`, so `build-zip` works from any working directory.
* `build-zip` logs `Generated 3 supporting document(s) for this batch.` on success. If it cannot
  render them (template missing, `enabled=false`, or the record's channel is not in the document's
  `channels`), the three references stay unsatisfied and the record is **skipped** — the batch
  fails with `... supporting documents have not been imported: AdhaarAP.pdf, D1.pdf, C3.pdf`.
* The four documents together are far below the 500 KB per-customer supporting-document limit; a
  real photo or template that pushes the total over it fails the same document check.

---

## 2. Steps

### Step 1 — fetch the daily source (registers the dockey)

```powershell
& $exe fetch cust --file .\samples\dryrun-GTR007377-source.json --settings $st
```

Expected:

```
[fetch] Reading 1 customer source record(s) from '...\samples\dryrun-GTR007377-source.json'
[fetch] Inserted=1  Skipped=0  Total=1  CbsFailed=0
```

Master row: `GTR007377`, `DocumentKey = 0871808577544332`, `Source = beckyc`, status **`PND`**.

### Step 2 — insert the child's details

```powershell
& $exe insert --file .\samples\dryrun-GTR007377-customer.json --settings $st
```

Expected:

```
[insert] Created 'GTR007377' (Ananya Verma)
[insert]   Saved demographics + 1 proof(s), addresses, contact, 0 related party(ies), attestation
```

`insert` does **not** call the CRM server and **does not** touch the dockey already on the master
row. It saves the demographics, the one proof (Aadhaar), the addresses, the contact, **no related
parties** (above ten none are required) and the attestation, then leaves the record at
`PendingSearch` (`SRP`).

### Step 3 — move to the image gate (`IMP`)

`documents fetch` only processes records at `ImagePending` / `ImageFailed`, so put the record at the
image gate first (dry-run bridge; `store` would normally do this for a channel with a source):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP' WHERE CustomerId = 'GTR007377';"
```

### Step 4 — fetch the image/document from the channel source

```powershell
& $exe documents fetch --customer GTR007377 --settings $st
```

Expected:

```
[documents] Fetching image/supporting documents for 1 record(s)...
[documents] [GTR007377] fetched 1 document(s) from beckyc: Photo.jpg -> PendingSearch
[documents] Done: Fetched=1  Failed=0  Total=1
```

The folder `x/y/z/0871808577544332/` — locally
`runtime\document-fetch\inbox\beckyc\0871808577544332\` — was read, the `.txt` decoded, the real
content type sniffed, the bytes stored and the record's `photoOfIndividual` slot repointed at
**`Photo.jpg`**. Status → **`SRP` (PendingSearch)**.

> A missing folder/file leaves the record at **`IMF`** and blocked from batching. Fix the inbox,
> then `& $exe retry --activity ImageFetch --settings $st`.

### Step 5 — pre-batch customer search

```powershell
& $exe search-customer --customer GTR007377 --settings $st
```

Expected:

```
[search-customer] [GTR007377] prepared 2 search row(s): option 1 (E^8765), option 2 (name+DOB)
[search-customer] [GTR007377] not found -> key 'ISR51673812646064768' written to record 20 (Searched)
[search-customer] Done: Found=0  NotFound=1  Failed=0  Total=1
```

Only **two** rows are prepared — Aadhaar (`E^<last4>`) and the name+DOB fallback — because the
child has no PAN, so there is no `C^<pan>` row like the adult `GTR007375` run produced.

The dry-run settings use `searchApi.simulateFoundEvery = 0`, so the simulator always returns
"not found" → status **`SRD` (Searched)**, ready to batch. Set it to `3` to also exercise the
"already exists" branch (that ends the record at `SRF` and it is **not** batched).

### Step 6 — generate the batch (.UPL + zip)

```powershell
& $exe build-zip --settings $st
```

Expected:

```
[build-zip] Generated 3 supporting document(s) for this batch.
[build-zip] Batch 'I_IRA000337_IN9797_<ddmmyyyy>_00065' generated with 1 record(s).
[build-zip]   Upload file : D:\ckyccentral\ckyc\runtime\output\<BATCHKEY>\upload\I_IRA000337_IN9797_<ddmmyyyy>_00065.UPL
[build-zip]   Zip archive : D:\ckyccentral\ckyc\runtime\output\<BATCHKEY>\<BATCHKEY>.zip
[build-zip]   Skipped     : none
```

`Skipped : none` is the success condition. Status → **`BAT` (Batched)**. The first line —
`Generated 3 supporting document(s)` — is the Aadhaar / consent / static-declaration render (§1d);
it is what fills the three generated references. If it is absent, the record is skipped with a
missing-document error instead (see §6).

The `.UPL` carries the Form 97 shape of record 20 (`f32` blank, `f33=Y`, `f34` blank — see §7) and,
for this age band, **no record 60**.

### Step 7 — validate (simulated FVU)

```powershell
& $exe fvu --settings $st
```

Expected:

```
[fvu] Executed=true  ExitCode=0  Passed=true
[fvu]   files=1 success=1 failed=0 summaryPdf=NULL
[fvu]   output zIp  : ...\runtime\runs\<BATCHKEY>\output\...UPL.zip
[fvu]   hash        : <64 hex sha-256>
```

Status → **`UPL` (Uploaded)**. (With `sftp.enabled=false` the FVU step is the upload. Set
`"sftp": { "enabled": true }` to stop at `FVP` and use the `sftp push` step instead.)

### Step 8 — inspect

```powershell
& $exe status --settings $st
& $exe batch-find --customer GTR007377 --settings $st
```

---

## 3. Expected state after each step

| Step | `master_record.Status` | Code |
|---|---|---|
| 1. `fetch` | Pending | `PND` |
| 2. `insert` | PendingSearch | `SRP` |
| 3. bridge | ImagePending | **`IMP`** |
| 4. `documents fetch` | PendingSearch | `SRP` |
| 5. `search-customer` | Searched | `SRD` |
| 6. `build-zip` | Batched | `BAT` |
| 7. `fvu` | Uploaded | `UPL` |

Any other value (`IMF`, `FLD`, `FVF`) is a failure — see §6.

---

## 4. Verify

**Record + dockey + minor flags:**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT CustomerId, DocumentKey, Source, StatusCode, LastError FROM master_record;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT i.KycType, i.Minor, i.Form97Provided, i.Form61Provided, i.DateOfBirth FROM individual_record_20 i JOIN master_record m ON m.Id = i.MasterRecordId WHERE m.CustomerId = 'GTR007377';"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM individual_record_60 r JOIN master_record m ON m.Id = r.MasterRecordId WHERE m.CustomerId = 'GTR007377';"
```

Expect:

```
GTR007377  0871808577544332  beckyc  UPL  NULL
M  Y  Y  NULL  15-08-2012
0
```

**The fetched document landed in the store** (content length lives on `file_content`):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT d.OriginalFileName, d.MediaType, d.SourceType, c.ByteLength FROM individual_document d JOIN master_record m ON m.Id = d.MasterRecordId JOIN file_content c ON c.Id = d.FileContentId WHERE m.CustomerId = 'GTR007377';"
```

Expect `Photo.jpg` with `SourceType = Sftp` and a media type of `image/jpeg` (sniffed from the
decoded payload), e.g. `Photo.jpg  image/jpeg  Sftp  631`.

**The batch contents** — open `runtime\output\<BATCHKEY>\upload\support_docs\`:

| File | Source | What to check |
|---|---|---|
| `Photo.jpg` | **fetched from beckyc** (the `.txt` decoded + sniffed) | the only document in the store (`individual_document`, `SourceType = Sftp`) |
| `AdhaarAP.pdf` | generated — Aadhaar EKYC report | fetched photo overlaid in the top-left frame; name, masked Aadhaar, gender, DOB and address match the record |
| `C3.pdf` | generated — Annexure-1 client consent | client name, relation, reporting entity, Aadhaar no and declaration date (no PAN value/tick for this record) |
| `D1.pdf` | generated — static declaration (template attached unchanged) | matches `doc_format\Template_1.pdf` byte-for-byte |

All four names are referenced in the `.UPL` and packed in `<BATCHKEY>.zip`. Generation details and
the exact templates/slots for the last three are in §1d.

```powershell
# the .UPL document references
$upl = Get-ChildItem .\runtime\output -Recurse -Filter '*.UPL' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
([regex]::Matches([IO.File]::ReadAllText($upl.FullName), '[^|]+\.(?:pdf|jpg|jpeg|png)') | ForEach-Object Value) | Sort-Object -Unique

# the zip contents
$zip = Get-ChildItem .\runtime\output -Recurse -Filter '*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
$archive.Entries | Select-Object FullName, Length | Format-Table -AutoSize
$archive.Dispose()
```

**The minor flags survived into the file** — check the emitted record 20 (and the absence of
record 60):

```powershell
$upl = Get-ChildItem .\runtime\output -Recurse -Filter '*.UPL' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content $upl.FullName | Where-Object { $_ -match '^20\|' } | ForEach-Object { ($_ -split '\|')[0..3] -join '|' }
(Get-Content $upl.FullName | Where-Object { $_ -match '^60\|' }).Count
```

Expect `20|1|ISR…|M` (KYC type `M`) and `0` record-60 lines.

After the real-FVU step (§5) the run folder also contains
`I_IRA000337_IN9797_01102026_00065.UPL.validated` and the `Executive_Summary_*.pdf`.

---

## 5. Repeat, real FVU, or switch to the real beckyc SFTP

**Repeat the offline run:** reset the record to the image gate, then re-run step 4 onward.

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP', IsImageFetched = 0 WHERE CustomerId = 'GTR007377';"
& $exe documents fetch --customer GTR007377 --settings $st
```

(Or recreate the dry-run DB in §0 for a completely clean pass.)

`build-zip` re-renders `AdhaarAP.pdf`, `C3.pdf` and `D1.pdf` from the current record data on every
run, so repeat runs never pick up a stale generated document.

**Validate with the real FVU (already verified):**

A ready-made variant with `fvu.useRealFvu = true` ships as
`samples\settings-dryrun-GTR007377-realfvu.json`. Run the same steps with
`$st = ".\samples\settings-dryrun-GTR007377-realfvu.json"`, or validate the last batch directly:

```powershell
& $exe fvu --settings .\samples\settings-dryrun-GTR007377-realfvu.json
```

**Verified result** (customer `GTR007377`, batch `I_IRA000337_IN9797_01102026_00065`):

```
[fvu] Executed=true  ExitCode=0  Passed=true
[fvu]   files=1 success=1 failed=0 summaryPdf=...\Executive_Summary_20261001_085029.pdf
[fvu]   hash        : 4b49299bf5d39ec291ea6608d9dbf77bb0a074c90253455810e9b46e989fca89
```

exit code `0`, ~20 s. Unlike the simulator, the real FVU emits the `Executive_Summary_*.pdf` and
writes its own file hash, so a `Passed=true` here is real CERSAI-format validation — KYC type M for
an under-18 person, the Form 97 record-20 shape (§7) and, for this age band, no record 60.

**Against the real server:** edit `samples\settings-dryrun-GTR007377.json` →
`documentFetch.channels.beckyc` and set the connection fields you know:

```jsonc
"beckyc": {
  "enabled": true,
  "kind": "Sftp",
  "useRealSftp": true,                 // <-- real SFTP now
  "host": "<real beckyc host>",
  "port": 22,
  "username": "<user>",
  "password": "<password>",            // or "privateKeyPath": "C:\\keys\\beckyc.pem"
  "basePath": "<real base path>",      // e.g. x/y/z
  "folderPattern": "{dockey}",         // folder = <basePath>/<dockey>
  "timeoutSeconds": 60,
  "documents": [
    { "pattern": "*.txt", "target": "Photo.jpg", "slot": "photoOfIndividual" }
  ]
}
```

Then reset the record (§5) and re-run `documents fetch` — the only change is where the `.txt` is
read from.

---

## 6. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `documents fetch` → `No individual record awaiting an image/document` | The record is not `IMP`. Run the step-3 bridge; confirm `documentFetch.enabled=true` and beckyc `enabled=true`. |
| Record stuck at **`IMF`** | Inbox folder/file missing. Check `runtime\document-fetch\inbox\beckyc\0871808577544332\` contains a `.txt`, then `& $exe retry --activity ImageFetch --settings $st`. |
| `...the base64 payload... could not be decoded` | The `.txt` is not `data:<mime>;base64,…` / valid base64 — regenerate it with the helper script. |
| `The content signature does not match image/jpeg` | A raw (non-`.txt`) file whose extension disagrees with its bytes. Use the `.txt` wrapper, or match the pattern to the content. |
| `build-zip` → validation error `Only Passport (A) or Aadhaar/VID (E) is allowed for a Minor Account (M).` | The OVD is not A/E while `kycType=M`. Change `proofs[].ovdType` (the sample uses `E`). |
| Real FVU → `ERR_360 Person is under 18, must be marked as Minor.` | An under-18 record was written with `kycType=N` (or `minor=N`). Use `kycType=M` + `minor=Y`. |
| Real FVU → `ERR_036 ... PAN / Form 97 ... Mandatory` | A record with no PAN must select Form 97 (or Form 61) — and the writer must leave the *unselected* form indicators blank, not `"N"`. Fixed in §7. |
| Real FVU → `ERR_044 Only share one from PAN, Form 97 ..., or Form 61` | More than one of PAN / Form 97 / Form 61 is set. Keep exactly one. |
| `insert` → `Validation failed: Guardian details are mandatory for a client below 10 years of age.` | The DOB is under ten and `relatedParties` is empty. Add the guardian (see `docs\dryrun-GTR007376.md` §1c). |
| `build-zip` → `Skipped : 1` with `... has not been imported: X` (a document the record references) | A referenced document is missing. Either remove the reference from the customer JSON, or stage + `documents import` that file. |
| `build-zip` → `... have not been imported: AdhaarAP.pdf, D1.pdf, C3.pdf` | The **generated** documents were not rendered. Look for the warning `[build-zip] document generation: Document template not found: '<path>'` — `documentGeneration.templateRoot` did not resolve (a relative value resolves against the working directory). The dry-run settings pin it to `D:\ckyccentral\ckyc\doc_format`; if you edited it, restore it, or run from `D:\ckyccentral\ckyc`. Also confirm `documentGeneration.enabled=true` and the Aadhaar/consent entries still list `"channels": [ "beckyc" ]` (§1d). |
| `AdhaarAP.pdf` is generated but **without the photo** | `photoOfIndividual` is empty (step 4 did not fetch `Photo.jpg`), the stored file's media type is not `image/*`, or `overlayPhoto=false`. |
| A generated file is missing while `build-zip` still succeeds | The document is disabled, or the record's channel is not in that document's `channels`; check the `documentGeneration.documents[]` entries (§1d). |
| `build-zip` → `Supporting documents total N bytes; the per-customer limit is 500 KB` | The fetched files plus the generated documents exceed the 500 KB per-customer limit. Use the shipped size-optimised templates and a smaller photo. |
| `build-zip` → validation errors (`Mother / Father / Spouse Name`, `PAN must match …`) | The customer JSON is not FVU-valid — re-check §1b. |
| `insert` says `Save failed` / `Validation failed` | A required field is missing or malformed — see §1b. |
| `fetch` recorded the customer but `documents fetch` used the customer id as the folder | `DocumentKey` was empty on the master row — the source JSON must carry `"documentKey"`. Verify with the §4 SQL. |
| `fvu` runs for minutes / needs temp access | You are hitting the **real** FVU. The dry-run settings use `useRealFvu=false`; confirm you passed `--settings $st`. |

---

## 7. The Form 97 encoding fix verified by these dry runs

The first real-FVU submission of the under-ten minor batch (`GTR007376`) **failed**:

```
record=20 line=1 field=PAN / Form 97 (erstwhile form 60) / Form 61
value=  [ERR_036] Atleat provide one from PAN, Form 97 (erstwhile form 60), or Form 61 it's Mandatory
```

Record 20 contained `Form 97 = Y` and no PAN — i.e. the rule *was* satisfied — but the writer also
emitted `Form 61 = N` (and would emit `Form 97 = N` when Form 61 was selected). Isolated FVU
experiments showed the utility treats an explicit `N` on the unselected flag as a conflict:

| Record 20 encoding | Real FVU |
|---|---|
| PAN + `Form97=N` + `Form61=N` | **pass** (the `GTR007375` adult run) |
| PAN + both form flags blank | **pass** |
| `Form97=Y` + `Form61=N` | **fail** `ERR_036` |
| `Form97=Y` + `Form61` blank | **pass** (this run's batch) |
| `Form61=Y` + `Form97` blank | **pass** |
| PAN + `Form97=Y` | **fail** `ERR_044` (only one may be set) |

The fix in `src\CKYC.Files\CkycUploadWriter.cs` emits **only the selected indicator** — the
unselected Form 97 / Form 61 fields are left blank:

```csharp
f[33] = form97 ? "Y" : "";
f[34] = form61 ? "Y" : "";
```

The `CKYC.SpecChecks` suite pins both shapes (PAN record → both flags blank; Form 97 record →
`Y` / blank), so the PAN path that previously passed is unaffected. All three dry runs now validate
with the real FVU: adult `GTR007375` (PAN), under-ten minor `GTR007376` (Form 97 + guardian),
10–17 minor `GTR007377` (Form 97, no guardian).

> If a checkout predates this fix, a Form 97 record (or any no-PAN record) will fail the real FVU
> with `ERR_036`. Apply the two-line change above before running this guide's real-FVU step.
