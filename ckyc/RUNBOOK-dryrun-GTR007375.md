# RUNBOOK — dry run `GTR007375` / dockey `0871808577544330`

Copy-paste steps. Run every command from **`D:\ckyccentral\ckyc`**.
Each step has a **Check** line — confirm it before moving on.

> Inputs: `samples\dryrun-GTR007375-source.json` (customer id + dockey, done) ·
> `samples\dryrun-GTR007375-customer.json` (customer details, **you fill this in**) ·
> the beckyc `.txt` payload (step 2).
> Offline: local inbox + simulated FVU. Database: `CkycCentral_DryRun` (existing `CkycCentral` untouched).

---

## Step 0 — setup (once)

```powershell
Set-Location D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1

$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
$st  = ".\samples\settings-dryrun-GTR007375.json"
```

**Check:** `Test-Path $exe` → `True`.

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

> If you get `ImageFailed` (`IMF`): the inbox file is missing — redo Step 2, then
> `& $exe retry --activity ImageFetch --settings $st`.

---

## Step 8 — pre-batch customer search

```powershell
& $exe search-customer --customer GTR007375 --settings $st
```

**Check:** `not found -> key 'ISR...' written to record 20 (Searched)` → status `SRD`.

---

## Step 9 — generate the batch (.UPL + zip)

```powershell
& $exe build-zip --settings $st
```

**Check:** `Skipped : none` → status `BAT`. **Note the printed `Batch` key** (e.g.
`I_IRA000337_IN9797_<ddmmyyyy>_00064`).

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

## Step 12 — verify the data and the batch

```powershell
# master row: dockey + final status
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT CustomerId, DocumentKey, Source, StatusCode, LastError FROM master_record;"

# the fetched document
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT d.OriginalFileName, d.MediaType, d.SourceType, c.ByteLength FROM individual_document d JOIN master_record m ON m.Id = d.MasterRecordId JOIN file_content c ON c.Id = d.FileContentId WHERE m.CustomerId = 'GTR007375';"

# the batch's supporting documents
Get-ChildItem .\runtime\output -Recurse -Filter 'support_docs' -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-ChildItem $_.FullName | Select-Object Name, Length }
```

**Check:**

- master row: `GTR007375  0871808577544330  beckyc  UPL  NULL`
- document: `Photo.jpg` · `image/jpeg` · `Sftp`
- `support_docs`: `Photo.jpg` (fetched) + `AdhaarAP.pdf`, `C3.pdf`, `D1.pdf` (generated)

Open `runtime\output\<BATCHKEY>\upload\support_docs\AdhaarAP.pdf` — the fetched photo should be
overlaid in the top-left frame.

---

## Repeat / re-fetch

Reset to the image gate, then re-run Step 7 onward:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP', IsImageFetched = 0 WHERE CustomerId = 'GTR007375';"
& $exe documents fetch --customer GTR007375 --settings $st
```

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
