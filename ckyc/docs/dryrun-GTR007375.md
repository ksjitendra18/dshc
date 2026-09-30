# Dry run — beckyc image fetch (`GTR007375` / dockey `0871808577544330`)

Exercises the **pre-search image step** end-to-end for **one** beckyc customer, fully offline and
without touching the existing database:

```
fetch --file  ->  insert --file  ->  (image gate)  ->  documents fetch (beckyc .txt)
              ->  search-customer  ->  build-zip  ->  fvu  ->  status
```

| | |
|---|---|
| Customer id | **`GTR007375`** |
| Document key (dockey) | **`0871808577544330`** |
| Run from | `D:\ckyccentral\ckyc` |
| Database | dedicated LocalDB **`CkycCentral_DryRun`** (the existing `CkycCentral` is not modified) |
| CRM | not used — `insert` fills omissions from the in-process dummy provider |
| beckyc transport | `useRealSftp = false` → reads `runtime\document-fetch\inbox\beckyc\<dockey>\*.txt` |
| FVU | `useRealFvu = false` → deterministic local simulation (no network, instant) |

Everything that matters — the dockey → folder mapping, the base64 `.txt` decode, the real-extension
sniff, the `photoOfIndividual` slot repoint, the image gate (`IMP` → `SRP`) and the Aadhaar photo
overlay — is the **real** code path.

---

## Files used

| File | What it is | Who fills it |
|---|---|---|
| `samples\dryrun-GTR007375-source.json` | Step-1 source record: customer id + **dockey** + channel | already filled in |
| `samples\dryrun-GTR007375-customer.json` | The **customer details** written by `insert` | **you** (replace with the real details) |
| `samples\settings-dryrun-GTR007375.json` | Full settings override (dedicated DB, repo-local paths, offline SFTP/FVU, clean simulation) | ready to use |
| `samples\settings-dryrun-GTR007375-realfvu.json` | Same override with `fvu.useRealFvu = true` (real `FVU_RUN_UTILITY.exe`); verified `Passed=true` | ready to use |
| `scripts\make-dryrun-beckyc-txt.ps1` | Writes/wraps the beckyc `.txt` payload into the local inbox | ready to use |
| `docs\image.md` · `docs\document-fetch.md` | Background on the beckyc layout / fetch step | reference |

> `--settings` is a **full replacement**, not a merge, so every command below passes the same
> override file; nothing else changes.

---

## 0. One-time setup

Build once and set the two variables:

```powershell
Set-Location D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1

$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
$st  = ".\samples\settings-dryrun-GTR007375.json"
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

Either write a valid placeholder, or wrap your real customer photo:

```powershell
# placeholder (valid 1x1 JPEG as a beckyc data:image/jpeg;base64,… .txt)
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1

# OR your real photo (JPEG/PNG/PDF auto-detected)
powershell -ExecutionPolicy Bypass -File .\scripts\make-dryrun-beckyc-txt.ps1 -Source C:\path\to\photo.jpg
```

Result:

```
D:\ckyccentral\ckyc\runtime\document-fetch\inbox\beckyc\0871808577544330\0871808577544330.txt
```

The folder name is the **dockey**, not the customer id — that is the point of the dry run.

> Prefer a raw image? Copy `Photo.jpg` into the same folder and change the channel's `documents`
> entry in the settings file to `"pattern": "*.jpg"` (see `docs\image.md` §3B).

### 1b. The customer details (`samples\dryrun-GTR007375-customer.json`)

This file ships ready to run. Replace the placeholder values with the real customer's details.
Keep it **FVU-valid**; the fields that must be right for `build-zip` to pass are:

| Field | Rule |
|---|---|
| `customerId` | must stay `GTR007375` (matches the fetched master row + dockey) |
| `name.firstName` | letters, apostrophe and dot only; **no spaces** |
| `motherName` / `fatherName` / `spouseName` | at least **one** family name is mandatory |
| `dateOfBirth` | `DD-MM-YYYY` |
| `minor` | `Y` or `N` |
| `gender` | `M` / `F` / `T` |
| `residentialStatus` | `Resident` (or `NRI` / `PIO` / `ForeignNational`) + `residentialStatusSupportedByDocument` `Y`/`N` |
| `nationality` | `IN` + `nationalitySupportedByDocument` `Y` |
| `differentlyAbledStatus` | `Y`/`N` (`N` unless the PwD block applies) |
| `pan` | `AAAAA9999A` with the **4th character `P`** (e.g. `AVUPS9778A`) and `panVerified` `Y` |
| `photoOfIndividual` | `Photo.jpg` — the fetch repoints this slot at the file it pulls |

Everything else (proofs, permanent address, contact, attestation/`other`) is optional: when omitted
`insert` borrows FVU-valid defaults from the in-process dummy provider. References to
`AdhaarAP.pdf`, `C3.pdf` and `D1.pdf` are satisfied at `build-zip` by `documentGeneration`; the
`Photo.jpg` reference is satisfied by the **beckyc fetch**.

> If you add a `panDocument` (or any other extra document reference), place that file too — either
> stage it and `documents import`, or `build-zip` will skip the record.

---

## 2. Steps

### Step 1 — fetch the daily source (registers the dockey)

```powershell
& $exe fetch cust --file .\samples\dryrun-GTR007375-source.json --settings $st
```

Expected:

```
[fetch] Reading 1 customer source record(s) from '...\samples\dryrun-GTR007375-source.json'
[fetch] Inserted=1  Skipped=0  Total=1  CbsFailed=0
```

Master row: `GTR007375`, `DocumentKey = 0871808577544330`, `Source = beckyc`, status **`PND`**.

### Step 2 — insert the customer details

```powershell
& $exe insert --file .\samples\dryrun-GTR007375-customer.json --settings $st
```

Expected:

```
[insert] Created 'GTR007375' (Satyendra Kumar)
[insert]   Saved demographics + 1 proof(s), addresses, contact, 0 related party(ies), attestation
```

`insert` does **not** call the CRM server and **does not** touch the dockey already on the master
row. It leaves the record at `PendingSearch` (`SRP`).

### Step 3 — move to the image gate (`IMP`)

`documents fetch` only processes records at `ImagePending` / `ImageFailed`, so put the record at the
image gate first (dry-run bridge; `store` would normally do this for a channel with a source):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP' WHERE CustomerId = 'GTR007375';"
```

### Step 4 — fetch the image/document from the channel source

```powershell
& $exe documents fetch --customer GTR007375 --settings $st
```

Expected:

```
[documents] Fetching image/supporting documents for 1 record(s)...
[documents] [GTR007375] fetched 1 document(s) from beckyc: Photo.jpg -> PendingSearch
[documents] Done: Fetched=1  Failed=0  Total=1
```

The folder `x/y/z/0871808577544330/` — locally
`runtime\document-fetch\inbox\beckyc\0871808577544330\` — was read, the `.txt` decoded, the real
content type sniffed, the bytes stored and the record's `photoOfIndividual` slot repointed at
**`Photo.jpg`**. Status → **`SRP` (PendingSearch)**.

> A missing folder/file leaves the record at **`IMF`** and blocked from batching. Fix the inbox,
> then `& $exe retry --activity ImageFetch --settings $st`.

### Step 5 — pre-batch customer search

```powershell
& $exe search-customer --customer GTR007375 --settings $st
```

Expected:

```
[search-customer] [GTR007375] prepared 3 search row(s): option 1 (E^9674), option 1 (C^AVUPS9778A), option 2 (name+DOB)
[search-customer] [GTR007375] not found -> key 'ISR...' written to record 20 (Searched)
```

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
[build-zip] Batch 'I_IRA000337_IN9797_<ddmmyyyy>_00064' generated with 1 record(s).
[build-zip]   Upload file : D:\ckyccentral\ckyc\runtime\output\<BATCHKEY>\upload\I_IRA000337_IN9797_<ddmmyyyy>_00064.UPL
[build-zip]   Zip archive : D:\ckyccentral\ckyc\runtime\output\<BATCHKEY>\<BATCHKEY>.zip
[build-zip]   Skipped     : none
```

`Skipped : none` is the success condition. Status → **`BAT` (Batched)**.

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
& $exe batch-find --customer GTR007375 --settings $st
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

**Record + dockey + status:**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT CustomerId, DocumentKey, Source, StatusCode, LastError FROM master_record;"
```

**The fetched document landed in the store** (content length lives on `file_content`):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -h -1 -W -Q "SET NOCOUNT ON; SELECT d.OriginalFileName, d.MediaType, d.SourceType, c.ByteLength FROM individual_document d JOIN master_record m ON m.Id = d.MasterRecordId JOIN file_content c ON c.Id = d.FileContentId WHERE m.CustomerId = 'GTR007375';"
```

Expect `Photo.jpg` with `SourceType = Sftp` and a media type of `image/jpeg` (sniffed from the
decoded payload).

**The batch contents** — open `runtime\output\<BATCHKEY>\upload\support_docs\`:

| File | Source |
|---|---|
| `Photo.jpg` | **fetched from beckyc** (the `.txt` decoded + sniffed) |
| `AdhaarAP.pdf` | generated — Aadhaar EKYC report, with the fetched photo overlaid into the frame |
| `C3.pdf` | generated — Annexure-1 client consent |
| `D1.pdf` | generated — undertaking |

Open `AdhaarAP.pdf`: the customer photo should sit in the top-left frame and the printed values
should match the record.

---

## 5. Repeat, or switch to the real beckyc SFTP

**Repeat the offline run:** reset the record to the image gate, then re-run step 4 onward.

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral_DryRun -b -Q "UPDATE master_record SET Status = 15, StatusCode = 'IMP', IsImageFetched = 0 WHERE CustomerId = 'GTR007375';"
& $exe documents fetch --customer GTR007375 --settings $st
```

(Or recreate the dry-run DB in §0 for a completely clean pass.)

**Against the real server:** edit
`samples\settings-dryrun-GTR007375.json` → `documentFetch.channels.beckyc` and set the connection
fields you know:

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
read from. Once the fetch is confirmed, flip `fvu.useRealFvu` to `true` for a real validation.

---

## 6. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `documents fetch` → `No individual record awaiting an image/document` | The record is not `IMP`. Run the step-3 bridge; confirm `documentFetch.enabled=true` and beckyc `enabled=true`. |
| Record stuck at **`IMF`** | Inbox folder/file missing. Check `runtime\document-fetch\inbox\beckyc\0871808577544330\` contains a `.txt`, then `& $exe retry --activity ImageFetch --settings $st`. |
| `...the base64 payload... could not be decoded` | The `.txt` is not `data:<mime>;base64,…` / valid base64 — regenerate it with the helper script. |
| `The content signature does not match image/jpeg` | A raw (non-`.txt`) file whose extension disagrees with its bytes. Use the `.txt` wrapper, or match the pattern to the content. |
| `build-zip` → `Skipped : 1` with `... has not been imported: X` | A referenced document is missing. Either remove the reference from the customer JSON, or stage + `documents import` that file. |
| `build-zip` → validation errors (`Minor is mandatory`, `Mother / Father / Spouse Name`, `PAN must match …`) | The customer JSON is not FVU-valid — re-check §1b. (This is also why the guide uses `insert` rather than the CRM `store` path: the dummy CRM's hardcoded record predates the current validator.) |
| `insert` says `Save failed` / `Validation failed` | A required field is missing or malformed — see §1b. |
| `fetch` recorded the customer but `documents fetch` used the customer id as the folder | `DocumentKey` was empty on the master row — the source JSON must carry `"documentKey"`. Verify with the §4 SQL. |
| `fvu` runs for minutes / needs temp access | You are hitting the **real** FVU. The dry-run settings use `useRealFvu=false`; confirm you passed `--settings $st`. |
