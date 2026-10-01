# RUNBOOK — dry run `GTR007376` (minor) / dockey `0871808577544331`

Copy-paste steps. Run every command from **`D:\ckyccentral\ckyc`**.
Each step has a **Check** line — confirm it before moving on.

> This is the **minor-customer** variant of `RUNBOOK-dryrun-GTR007375.md`. The record is a child
> below ten (`kycType=M`, `minor=Y`, DOB `12-06-2019`), so a **guardian is mandatory** (record 60)
> and the only allowed OVDs are Passport (A) or Aadhaar (E). The child has **no PAN** — the record
> uses **Form 97 (erstwhile Form 60)** instead, which also verifies the writer fix described in
> `docs\dryrun-GTR007376.md` §7.
> For the **10–17 variant** (same minor rules, guardian optional) see `RUNBOOK-dryrun-GTR007377.md`.

> Inputs: `samples\dryrun-GTR007376-source.json` (customer id + dockey, done) ·
> `samples\dryrun-GTR007376-customer.json` (child + guardian details, **you fill this in**) ·
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
$st  = ".\samples\settings-dryrun-GTR007376.json"
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

> This drops the previous dry-run data (e.g. the `GTR007375` record). The normal `CkycCentral`
> database is not touched.

**Check:** last command exits `0` (no errors).

---

## Step 2 — put the beckyc payload in the local inbox

Use the placeholder, or wrap the child's real photo (JPEG/PNG/PDF auto-detected). The **dockey**,
not the customer id, names the folder:

```powershell
# placeholder (valid 1x1 JPEG)
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1 -CustomerId GTR007376 -Dockey 0871808577544331

# OR a real photo
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1 -CustomerId GTR007376 -Dockey 0871808577544331 -Source C:\path\to\photo.jpg
```

**Check:** this file exists —

```powershell
Test-Path .\runtime\document-fetch\inbox\beckyc\0871808577544331\0871808577544331.txt
```

---

## Step 3 — fill in the minor + guardian details

Open **`samples\dryrun-GTR007376-customer.json`** and replace the placeholder values. Keep these
FVU-valid (or `build-zip`/the FVU will fail):

- `customerId` = `GTR007376` (do not change)
- `kycType` = `M` (**Minor Account**), `minor` = `Y`
- `dateOfBirth` = `DD-MM-YYYY` **under 18**; for the guardian rule below keep it **under 10**
- `name.firstName` — letters only, no spaces; title `Ms`/`Mr`/`Mx`
- at least one of `motherName` / `fatherName` / `spouseName`
- `relatedParties[]` — the **guardian** (mandatory below 10):
  `{ "relatedPersonType": "Guardian", "ckycNumberOfRelatedPerson": "<14-digit CKYC of the guardian>" }`
- OVD: only `ovdType` `A` (Passport) or `E` (Aadhaar). The sample uses `E` + `modeOfAadhaarVerification` `B`
- exactly **one** of PAN / Form 97 / Form 61; the sample has **no PAN** → `form97Provided` = `Y`
  (do **not** also set `pan` or `form61Provided`)
- `gender` = `M`/`F`, `nationality`/`residentialStatus` + their `...SupportedByDocument` flags,
  `differentlyAbledStatus`
- `photoOfIndividual` = `Photo.jpg`

**Check:** the file still parses —

```powershell
Get-Content .\samples\dryrun-GTR007376-customer.json -Raw | ConvertFrom-Json | Out-Null; "JSON OK"
```

---

## Step 4 — fetch the daily source (registers the dockey)

```powershell
& $exe fetch cust --file .\samples\dryrun-GTR007376-source.json --settings $st
```

**Check:** `Inserted=1  Skipped=0  Total=1  CbsFailed=0` → status `PND`.

---

## Step 5 — insert the child + guardian details

```powershell
& $exe insert --file .\samples\dryrun-GTR007376-customer.json --settings $st
```

**Check:** `[insert] Created 'GTR007376' (Ishita Verma)` — note **`1 related party(ies)`** (the
guardian) and no validation error → status `SRP`.

---

## Step 6 — move the record to the image gate

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP' WHERE CustomerId = 'GTR007376';"
```

**Check:** `(1 rows affected)` → status `IMP`.

---

## Step 7 — fetch the image from the beckyc source  ← the step under test

```powershell
& $exe documents fetch --customer GTR007376 --settings $st
```

**Check:** `[documents] [GTR007376] fetched 1 document(s) from beckyc: Photo.jpg -> PendingSearch`
and `Fetched=1  Failed=0`. Status → `SRP`.

> This step fetches **only the photo**. The record's other three documents — `AdhaarAP.pdf`
> (Aadhaar generation), `C3.pdf` (consent generation) and `D1.pdf` (static declaration) — are
> **rendered at `build-zip`** from the `doc_format\` templates; they are verified in steps 9 and 12.

> If you get `ImageFailed` (`IMF`): the inbox file is missing — redo Step 2, then
> `& $exe retry --activity ImageFetch --settings $st`.

---

## Step 8 — pre-batch customer search

```powershell
& $exe search-customer --customer GTR007376 --settings $st
```

**Check:** `prepared 2 search row(s): option 1 (E^4321), option 2 (name+DOB)` (no PAN row — the
child has no PAN) and `not found -> key 'ISR...' written to record 20 (Searched)` → status `SRD`.

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

**Note the printed `Batch` key** (e.g. `I_IRA000337_IN9797_01102026_00064`).

> If that line is missing (or `build-zip` fails with `... have not been imported: AdhaarAP.pdf,
> D1.pdf, C3.pdf`), the three documents were not rendered — check the template check in step 0
> and see `docs\dryrun-GTR007376.md` §6/§7.

---

## Step 10 — validate (simulated FVU)

```powershell
& $exe fvu --settings $st
```

**Check:** `Executed=true  ExitCode=0  Passed=true`, `files=1 success=1 failed=0` → status `UPL`.

---

## Step 11 — submit to the real FVU (same batch)

```powershell
& $exe fvu --settings .\samples\settings-dryrun-GTR007376-realfvu.json
```

**Check:** `Executed=true  ExitCode=0  Passed=true`, `files=1 success=1 failed=0` and an
`Executive_Summary_*.pdf` in `runtime\runs\<BATCHKEY>\output\`.

> **Verified** (customer `GTR007376`, batch `I_IRA000337_IN9797_01102026_00064`):
> `Passed=true`, exit code `0`, hash
> `c662221d7acf3924f0596330bab6341dddcf781af42da69d2d6d8a75b9de1cac`, ~17 s.
> A Form 97 minor record fails this step with `ERR_036` on a build **before** the writer fix in
> `src\CKYC.Files\CkycUploadWriter.cs` — see the guide's §7.

---

## Step 12 — inspect and verify

```powershell
& $exe status --settings $st
& $exe batch-find --customer GTR007376 --settings $st
```

**Check:** `[UPL] Uploaded (pending at CERSAI) : 1`.

```powershell
# master row: dockey + final status
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT CustomerId, DocumentKey, Source, StatusCode, LastError FROM master_record;"

# the fetched document (the generated Aadhaar/consent/declaration are NOT stored here)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT d.OriginalFileName, d.MediaType, d.SourceType, c.ByteLength FROM individual_document d JOIN master_record m ON m.Id = d.MasterRecordId JOIN file_content c ON c.Id = d.FileContentId WHERE m.CustomerId = 'GTR007376';"

# the guardian record 60 + the minor fields in record 20
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT r.RelatedPersonType, r.CkycNumberOfRelatedPerson FROM individual_record_60 r JOIN master_record m ON m.Id = r.MasterRecordId WHERE m.CustomerId = 'GTR007376';"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT i.KycType, i.Minor, i.Form97Provided, i.Form61Provided, i.DateOfBirth FROM individual_record_20 i JOIN master_record m ON m.Id = i.MasterRecordId WHERE m.CustomerId = 'GTR007376';"

# the batch's supporting documents (fetched + generated)
Get-ChildItem .\runtime\output -Recurse -Filter 'support_docs' -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-ChildItem $_.FullName | Select-Object Name, Length }
```

**Check:**

- master row: `GTR007376  0871808577544331  beckyc  UPL  NULL`
- document: `Photo.jpg` · `image/jpeg` · `Sftp` — only the **fetched** file lands in the document
  store; the three generated documents live inside the batch only.
- record 60: `Guardian  33720047736138`
- record 20: `M  Y  Y  NULL  12-06-2019` (KycType, Minor, Form97, Form61, DOB)
- `support_docs`: `Photo.jpg` (fetched) + `AdhaarAP.pdf`, `C3.pdf`, `D1.pdf` (generated)

Open each generated document in `runtime\output\<BATCHKEY>\upload\support_docs\`:

| File | Generation | Check inside | Record slot |
|---|---|---|---|
| `AdhaarAP.pdf` | Aadhaar | the fetched photo is overlaid in the top-left frame; name, `XXXX XXXX <last4>`, gender, DOB and permanent address match the record | `proofOvd`, `currentAddressOvd` |
| `C3.pdf` | consent | client name, relation, reporting entity, Aadhaar no, declaration date (no PAN for this record, so no PAN value/tick) | `clientConsent` |
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
`upload/support_docs/*` entries in the zip listing. After the real-FVU step the run folder also
contains `I_IRA000337_IN9797_01102026_00064.UPL.validated` and the `Executive_Summary_*.pdf`.

---

## Repeat / re-fetch

Reset to the image gate, then re-run Step 7 onward:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP', IsImageFetched = 0 WHERE CustomerId = 'GTR007376';"
& $exe documents fetch --customer GTR007376 --settings $st
```

`build-zip` re-renders `AdhaarAP.pdf` (Aadhaar), `C3.pdf` (consent) and `D1.pdf` (static
declaration) from the current record data on every run — there are no stale generated documents
to clean up. (Or recreate the dry-run DB in Step 1 for a completely clean pass.)

## Switch to the real beckyc SFTP

Edit `samples\settings-dryrun-GTR007376.json` → `documentFetch.channels.beckyc`: set
`"useRealSftp": true` and fill `host` / `username` / `password` (or `privateKeyPath`) / `basePath`.
Then reset (above) and re-run Step 7.

## All-in-one (after Step 3 is filled in)

```powershell
& $exe fetch cust --file .\samples\dryrun-GTR007376-source.json --settings $st
& $exe insert --file .\samples\dryrun-GTR007376-customer.json --settings $st
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP' WHERE CustomerId = 'GTR007376';"
& $exe documents fetch --customer GTR007376 --settings $st
& $exe search-customer --customer GTR007376 --settings $st
& $exe build-zip --settings $st
& $exe fvu --settings $st
& $exe fvu --settings .\samples\settings-dryrun-GTR007376-realfvu.json
& $exe status --settings $st
```

> Background, troubleshooting, the minor/guardian rules and the FVU Form 97 encoding fix:
> `docs\dryrun-GTR007376.md`.
