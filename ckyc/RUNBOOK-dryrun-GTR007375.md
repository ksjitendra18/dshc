# RUNBOOK — dry run `GTR007375` / dockey `0871808577544330`

Copy-paste steps. Run every command from **`D:\ckyccentral\ckyc`**.
Each step has a **Check** line — confirm it before moving on.

> Inputs: `samples\dryrun-GTR007375-source.json` (customer id + dockey, done) ·
> `samples\dryrun-GTR007375-customer.json` (customer details, **you fill this in**) ·
> the beckyc `.txt` payload (step 2) · the `doc_format\` templates (shipped with the repo).
> The batch carries **four** documents: `Photo.jpg` **fetched** from beckyc, plus `AdhaarAP.pdf`
> (**Aadhaar generation**), `C3.pdf` (**consent generation**) and `D1.pdf` (the **static
> declaration**) rendered from the templates at `build-zip` — see steps 9 and 12.
> Offline: local inbox + simulated FVU. Database: `CkycCentral_DryRun` (existing `CkycCentral` untouched).

---

## Step 0 — setup (once)

```powershell
Set-Location D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1

$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
$st  = ".\samples\settings-dryrun-GTR007375.json"
```

**Check:** `Test-Path $exe` → `True`, and the generation templates are present:

```powershell
Test-Path .\doc_format\Aadhaar_template_blank.jpg, .\doc_format\Consent_template_blank.png, .\doc_format\Template_1.pdf
```

→ `True  True  True`.

---

## Step 1 — create the clean dry-run database

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "IF DB_ID('CkycCentral_DryRun') IS NOT NULL BEGIN ALTER DATABASE [CkycCentral_DryRun] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [CkycCentral_DryRun]; END; CREATE DATABASE [CkycCentral_DryRun];"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -i .\scripts\sqlserver\schema.sql
```

**Check:** last command exits `0` (no errors).

---

## Step 2 — put the beckyc payload in the local inbox

Use the placeholder, or wrap your real photo:

```powershell
# placeholder (valid 1x1 JPEG)
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1

# OR real photo (JPEG/PNG/PDF auto-detected)
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1 -Source C:\path\to\photo.jpg
```

**Check:** this file exists —

```powershell
Test-Path .\runtime\document-fetch\inbox\beckyc\0871808577544330\0871808577544330.txt
```

---

## Step 3 — fill in the customer details

Open **`samples\dryrun-GTR007375-customer.json`** and replace the placeholder values with the real
customer's. Keep these FVU-valid (or `build-zip` will fail):

- `customerId` = `GTR007375` (do not change)
- `name.firstName` — letters only, no spaces
- at least one of `motherName` / `fatherName` / `spouseName`
- `dateOfBirth` = `DD-MM-YYYY`
- `minor` = `Y`/`N`, `gender` = `M`/`F`
- `residentialStatus` + `residentialStatusSupportedByDocument`, `nationality` + `nationalitySupportedByDocument`, `differentlyAbledStatus`
- `pan` = `AAAAA9999A` with the **4th char `P`** (e.g. `AVUPS9778A`) + `panVerified` = `Y`
- `photoOfIndividual` = `Photo.jpg`

**Check:** the file still parses —

```powershell
Get-Content .\samples\dryrun-GTR007375-customer.json -Raw | ConvertFrom-Json | Out-Null; "JSON OK"
```

---

## Step 4 — fetch the daily source (registers the dockey)

```powershell
& $exe fetch cust --file .\samples\dryrun-GTR007375-source.json --settings $st
```

**Check:** `Inserted=1  Skipped=0  Total=1  CbsFailed=0` → status `PND`.

---

## Step 5 — insert the customer details

```powershell
& $exe insert --file .\samples\dryrun-GTR007375-customer.json --settings $st
```

**Check:** `[insert] Created 'GTR007375' (...)` and no validation error → status `SRP`.

---

## Step 6 — move the record to the image gate

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP' WHERE CustomerId = 'GTR007375';"
```

**Check:** `(1 rows affected)` → status `IMP`.

---

## Step 7 — fetch the image from the beckyc source  ← the step under test

```powershell
& $exe documents fetch --customer GTR007375 --settings $st
```

**Check:** `[documents] [GTR007375] fetched 1 document(s) from beckyc: Photo.jpg -> PendingSearch`
and `Fetched=1  Failed=0`. Status → `SRP`.

> This step fetches **only the photo**. The record's other three documents — `AdhaarAP.pdf`
> (Aadhaar generation), `C3.pdf` (consent generation) and `D1.pdf` (static declaration) — are
> **rendered at `build-zip`** from the `doc_format\` templates; they are verified in steps 9 and 12.

> If you get `ImageFailed` (`IMF`): the inbox file is missing — redo Step 2, then
> `& $exe retry --activity ImageFetch --settings $st`.

---

## Step 8 — pre-batch customer search

```powershell
& $exe search-customer --customer GTR007375 --settings $st
```

**Check:** `not found -> key 'ISR...' written to record 20 (Searched)` → status `SRD`.

---

## Step 9 — generate the batch (.UPL + zip) ← also renders the Aadhaar / consent / declaration

```powershell
& $exe build-zip --settings $st
```

**Check:** the first line says the three supporting documents were rendered, and `Skipped : none`
→ status `BAT`:

```
[build-zip] Generated 3 supporting document(s) for this batch.
[build-zip] Batch 'I_IRA000337_IN9797_<ddmmyyyy>_00064' generated with 1 record(s).
[build-zip]   Skipped     : none
```

`Generated 3 supporting document(s)` = **Aadhaar generation** (`AdhaarAP.pdf` — the EKYC report
with the fetched photo overlaid), **consent generation** (`C3.pdf` — the Annexure-1 client
consent) and the **static declaration** (`D1.pdf` — attached unchanged). They are rendered from
the templates in `doc_format\` straight into `support_docs`; there is nothing to stage by hand.

**Note the printed `Batch` key** (e.g. `I_IRA000337_IN9797_<ddmmyyyy>_00064`).

> If that line is missing (or `build-zip` fails with `... have not been imported: AdhaarAP.pdf,
> D1.pdf, C3.pdf`), the three documents were not rendered — check the template check in step 0
> and see `docs\dryrun-GTR007375.md` §6.

---

## Step 10 — validate (simulated FVU)

```powershell
& $exe fvu --settings $st
```

**Check:** `Executed=true  ExitCode=0  Passed=true`, `files=1 success=1 failed=0` → status `UPL`.

---

## Step 11 — inspect

```powershell
& $exe status --settings $st
& $exe batch-find --customer GTR007375 --settings $st
```

**Check:** `[UPL] Uploaded (pending at CERSAI) : 1`.

---

## Step 12 — verify the data, the fetched photo and the generated documents

```powershell
# master row: dockey + final status
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT CustomerId, DocumentKey, Source, StatusCode, LastError FROM master_record;"

# the fetched document (the generated Aadhaar/consent/declaration are NOT stored here)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT d.OriginalFileName, d.MediaType, d.SourceType, c.ByteLength FROM individual_document d JOIN master_record m ON m.Id = d.MasterRecordId JOIN file_content c ON c.Id = d.FileContentId WHERE m.CustomerId = 'GTR007375';"

# the batch's supporting documents (fetched + generated)
Get-ChildItem .\runtime\output -Recurse -Filter 'support_docs' -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-ChildItem $_.FullName | Select-Object Name, Length }
```

**Check:**

- master row: `GTR007375  0871808577544330  beckyc  UPL  NULL`
- document: `Photo.jpg` · `image/jpeg` · `Sftp` — only the **fetched** file lands in the document
  store; the three generated documents live inside the batch only.
- `support_docs`: `Photo.jpg` (fetched) + `AdhaarAP.pdf`, `C3.pdf`, `D1.pdf` (generated)

Open each generated document in `runtime\output\<BATCHKEY>\upload\support_docs\`:

| File | Generation | Check inside | Record slot |
|---|---|---|---|
| `AdhaarAP.pdf` | Aadhaar | the fetched photo is overlaid in the top-left frame; name, `XXXX XXXX <last4>`, gender, DOB and permanent address match the record | `proofOvd`, `currentAddressOvd` |
| `C3.pdf` | consent | client name, relation, reporting entity, Aadhaar no, PAN and declaration date; the Aadhaar/PAN ticks (`X`) are drawn | `clientConsent` |
| `D1.pdf` | static | the declaration is the template attached unchanged (`doc_format\Template_1.pdf`) | `declarationDocument` |

The four file names are also referenced by the `.UPL` and packed in the batch zip — verify with:

```powershell
$upl = Get-ChildItem .\runtime\output -Recurse -Filter '*.UPL' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$upl.FullName
([regex]::Matches([IO.File]::ReadAllText($upl.FullName), '[^|]+\.(?:pdf|jpg|jpeg|png)') | ForEach-Object Value) | Sort-Object -Unique

$zip = Get-ChildItem .\runtime\output -Recurse -Filter '*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
$archive.Entries | Select-Object FullName, Length | Format-Table -AutoSize
$archive.Dispose()
```

Expect the four names (`Photo.jpg`, `AdhaarAP.pdf`, `C3.pdf`, `D1.pdf`) and all four
`upload/support_docs/*` entries in the zip listing.

---

## Repeat / re-fetch

Reset to the image gate, then re-run Step 7 onward:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP', IsImageFetched = 0 WHERE CustomerId = 'GTR007375';"
& $exe documents fetch --customer GTR007375 --settings $st
```

`build-zip` re-renders `AdhaarAP.pdf` (Aadhaar), `C3.pdf` (consent) and `D1.pdf` (static
declaration) from the current record data on every run — there are no stale generated documents
to clean up.

## Switch to the real beckyc SFTP

Edit `samples\settings-dryrun-GTR007375.json` → `documentFetch.channels.beckyc`: set
`"useRealSftp": true` and fill `host` / `username` / `password` (or `privateKeyPath`) / `basePath`.
Then reset (above) and re-run Step 7.

## Validate with the real FVU (already verified)

A ready-made variant with `fvu.useRealFvu = true` ships as
`samples\settings-dryrun-GTR007375-realfvu.json`. Run the same steps with
`$st = ".\samples\settings-dryrun-GTR007375-realfvu.json"` (or just run Step 10 with it against the
last batch):

```powershell
& $exe fvu --settings .\samples\settings-dryrun-GTR007375-realfvu.json
```

**Verified result** (customer `GTR007375`, batch `I_IRA000337_IN9797_29092026_00064`):

```
[fvu] Executed=true  ExitCode=0  Passed=true
[fvu]   files=1 success=1 failed=0 summaryPdf=...\Executive_Summary_<ts>.pdf
[fvu]   hash        : 0065ba697baa34d776a59fd7d54b1ecc26f06d36ed41549080f6173eb6cf3113
```

exit code `0`, ~23 s. Unlike the simulator, the real FVU emits the `Executive_Summary_*.pdf` and
writes its own file hash, so a `Passed=true` here is real CERSAI-format validation.

---

## All-in-one (after Step 3 is filled in)

```powershell
& $exe fetch cust --file .\samples\dryrun-GTR007375-source.json --settings $st
& $exe insert --file .\samples\dryrun-GTR007375-customer.json --settings $st
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP' WHERE CustomerId = 'GTR007375';"
& $exe documents fetch --customer GTR007375 --settings $st
& $exe search-customer --customer GTR007375 --settings $st
& $exe build-zip --settings $st
& $exe fvu --settings $st
& $exe status --settings $st
```

> Background, troubleshooting and the "why" for each step: `docs\dryrun-GTR007375.md`.
