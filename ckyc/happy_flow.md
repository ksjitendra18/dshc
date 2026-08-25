# happy_flow.md — Retail Customer Happy-Path Test (CKYC Processor)

Goal: create **one retail (individual) customer** and push it **end-to-end** through the
pipeline with **no errors** — insert → build-zip → fvu → (optional) response read → status.

- **Retail** here means an *individual* customer record (client type `I`), which is what the
  `insert` command creates and what the `build-zip`/`fvu` pipeline ships to the FVU/CERSAI.
  (Legal-entity customers are a separate code path and are not part of this flow.)
- **"No errors"** = no injected failures. This flow uses `insert` (which bypasses the
  CRM `store` save-error simulation) and the default `appsettings.json` (all simulation
  knobs that matter are off for this path). Everything passes.
- **FVU** is the *real* `FVU_RUN_UTILITY.exe` (config `fvu.useRealFvu=true`). It is the only
  external step and it takes a couple of minutes per run (it boots an embedded backend). On a
  valid record it exits `0` / `Passed=True`. "Other than FVU" = the only real external
  validator; all earlier steps succeed.

---

## 1. Files — what to prepare and where to put them

### 1a. The customer JSON — `D:\centralprocessing\ckyc\samples\retail-customer.json`

A working, FVU-valid template is already created at **`samples\retail-customer.json`**.
Edit these fields with the actual customer data you want to test:

| Field | What to change | Notes / constraints |
|-------|----------------|---------------------|
| `customerId` | your customer id | any lettered id, e.g. `CUST-RETAIL-0001` |
| `name` | title / first / middle / last | `firstName` **must contain a letter** |
| `motherName` | (or `fatherName`/`spouseName`) | **At least one of Mother/Father/Spouse is mandatory** — the shipped `samples/customer.json` has none and would be **skipped at `build-zip`**. Keep a family name. |
| `dateOfBirth` | `DD-MM-YYYY` | **mandatory** (empty DOB → skipped at `build-zip`) |
| `gender` | `M` / `F` | |
| `pan` | `ABCDE1234F` | if you provide a PAN you must also provide a `panDocument` |
| `contact` | email / mobile | run an OTP check |
| `permanentAddress.addressSupportedWithDocument` | `Y` | set to `Y` when the document evidences the present address |
| `permanentAddress.addressMatchWithOvd` | `Exact Match` | must be **Exact Match / No Match / Partial Match** — `"N"` → `ERR_191`. `Exact Match` when the document matches the present address exactly |
| `permanentAddress.copyOfOvd` | `AdhaarAP.jpg` | a doc that evidences the address (a placeholder is auto-created in `support_docs`) |
| `currentAddress` | *omit it* | **a missing current address is treated as "same as permanent"** (the writer emits `SameAsPermanent = Y` and omits the current-address text/proof block, but still writes the mandatory verification fields as `Y` — this avoids `ERR_118`, an Aadhaar current-address-proof field, and `ERR_260/262/263/264`) |
| `proofs` / `other` | as needed | any detail block you omit is auto-filled with FVU-valid defaults |

> Keep values FVU-valid: country `IN`, `DD-MM-YYYY` dates, a 20-character `searchKey`
> (`IMO26082433347192328` is exactly 20 chars), and the document filenames must match the
> files you place in the batch folder (step 1b).

### 1b. The supporting document(s)

The JSON references documents **by filename** (`Pan.pdf`, `AdhaarAP.jpg`, `D1.pdf`,
`C3.pdf`, …). Two ways to satisfy the FVU, in order of ease:

1. **Rely on the auto-placeholder (recommended for a smoke test).** `build-zip` creates the
   batch folder and auto-writes a placeholder file for every referenced document name into
   `runtime\output\<BATCHKEY>\upload\support_docs\`. The FVU only checks the file *exists*
   there, so the placeholder is enough to pass.
2. **Use your real document.** After `build-zip` (note the printed `Upload file :` path /
   `<BATCHKEY>`), copy your real file into
   `runtime\output\<BATCHKEY>\upload\support_docs\` **under the exact filename** referenced
   in the JSON (overwrite the placeholder), **before** you run `fvu`.

```
samples\retail-customer.json         ← your customer data (edit)
runtime\output\<BATCHKEY>\upload\support_docs\Pan.pdf      ← auto-placeholder (or your real doc)
```

---

## 2. Commands — the happy flow

Run everything from the project folder. First build once (only needed the first time):

```powershell
Set-Location D:\centralprocessing\ckyc
.\build.ps1

$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
```

> The CLI is `CKYC.Processor.exe`; `build.ps1` outputs it to
> `src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe`. No `crm serve` is required
> (the `insert` command fills missing detail blocks from the in-process dummy CRM).

### Step 1 — create the retail customer

```powershell
& $exe insert --file .\samples\retail-customer.json
```

Expected (happy):

```
[insert] Created 'CUST-RETAIL-0001' (Ashish Kumar)
[insert]   [Saved] <record tables written>
[insert] Next: `build-zip` then `fvu` to validate and process.
```

> Or fully inline (no JSON file) — same result, defaults borrow from the dummy CRM:
> ```powershell
> & $exe insert --customer-id CUST-RETAIL-0001 --name "Ashish Kumar" --dob 15-04-1988 --gender M --email ashish.kumar@yopmail.com --mobile 9876543210
> ```

### Step 2 — generate the batch

```powershell
& $exe build-zip
```

Expected (happy, `Skipped : none`):

```
[build-zip] Batch 'I_IAU010441_IN0238_<ddmmyyyy>_0000X' generated with 1 record(s).
[build-zip]   Upload file : D:\centralprocessing\ckyc\runtime\output\<BATCHKEY>\upload\I_IAU010441_IN0238_<ddmmyyyy>_0000X.UPL
[build-zip]   Zip archive : D:\centralprocessing\ckyc\runtime\output\<BATCHKEY>\<BATCHKEY>.zip
[build-zip]   Skipped     : none
[build-zip] Run `fvu` to submit this batch to the File Validation Utility.
```

Note the `<BATCHKEY>` printed here — it names the folder that holds the `.UPL`, the zip and
`upload\support_docs\` (where you drop your real document).

### Step 3 — submit to the FVU (the real validator)

```powershell
& $exe fvu
```

Expected (happy → `Passed=True`, exit code `0`):

```
[fvu] Submitting batch '<BATCHKEY>' (...\upload\I_IAU010441_IN0238_<ddmmyyyy>_0000X.UPL) to the FVU...
[fvu] Executed=True  ExitCode=0  Passed=True
[fvu]   files=1 success=1 failed=0 summaryPdf=<...>
[fvu]   output zIp  : ...\runs\<BATCHKEY>\output\processed.zip
[fvu]   hash        : <64 hex sha-256>
```

On pass the record advances to **Uploaded** (uploaded & pending at CERSAI). The hash is the
file-level SHA-256 the FVU wrote back into the record-10 header.

> **If you see `ExitCode=-1` / `error : FVU fatal error`:** the FVU is a PyInstaller bundle that
> extracts itself to the system temp folder. That failed here because the file sandbox blocks
> writes to the system temp — **it is not a record-validation failure** (the batch already passed
> `build-zip` with `Skipped : none`). Run `fvu` outside the sandbox / elevated (see gotcha #4).

### Step 4 — inspect the pipeline state

```powershell
& $exe status
```

Expected:

```
=== CKYC master-table status (current stage per record) ===
  ...
  Saved : 0
  Batched : 0
  Uploaded : 1
  ...
=== Last batch ===
  key=<BATCHKEY>  file=I_IAU010441_IN0238_<ddmmyyyy>_0000X.UPL  records=1
  upload=...
  zip=...
  Next: `fvu` to submit, then `response read` to ingest the CERSAI reply.
```

### Step 5 — (optional) read the CERSAI reply

In production CERSAI returns a `<upload>.UPL.RESm` file over time. In a local happy test there
is no real reply, so **`response read` is optional** — it simply finds nothing and reports 0
files unless you drop a seeded `.RES` file in `runtime\runs\<BATCHKEY>\output\` or pass `--file`.
Use the `response read` step only if you have a reply file to ingest.

```powershell
& $exe response read   # optional; 0 files until a CERSAI reply exists
```

---

## 3. What "success" looks like

- Step 1 `insert` → exit `0`, record **Saved**
- Step 2 `build-zip` → exit `0`, **`Skipped : none`**
- Step 3 `fvu` → exit `0`, **`Passed=True`**, `success=1 failed=0`, a `hash` printed
- Step 4 `status` → **`Uploaded : 1`** (nothing `Failed`/`FvuFailed`/`Rejected`)

If you get a skip at `build-zip` or a `Passed=False` at `fvu`, the record data is
FVU-invalid — re-check section 1a (family name, DOB, PAN + PAN doc, valid country/dates).

---

## 4. Gotchas / notes

- **Clean DB per run.** `build-zip` batches **every** currently-`Saved` record. If you run the
  happy flow twice without resetting, the second run re-batches older records. For a clean
  single-customer test, delete the DB first:
  ```powershell
  Remove-Item .\runtime\ckyc.db -ErrorAction SilentlyContinue
  ```
- **Don't run `store` for this test.** `store` (the CRM enrich path) uses the save-error
  simulation (`appsettings.json` has `saveErrorsEnabled:true`) and would inject failures. The
  happy flow uses `insert`, which never hits that knob. If you ever run `store` on this DB,
  add `--settings .\samples\failure\settings-clean.json` (all simulation off).
- **Family name is mandatory.** Without `motherName`/`fatherName`/`spouseName` the record is
  excluded at `build-zip`. The template includes `motherName` on purpose.
- **FVU needs temp access.** `FVU_RUN_UTILITY.exe` is a PyInstaller bundle that extracts to the
  system temp. On a machine where a file sandbox blocks that, run the `fvu` command outside the
  sandbox / elevated.
- **Record-40 / address fix (code).** Two code changes were applied (driven by the
  `individual-format-create.xlsx` spec):
  - `src\CKYC.Files\CkycUploadWriter.cs` — when the current address equals the permanent address it now
    emits `Same as permanent address = Y`, omits the current-address text/proof block, but still writes
    the **mandatory** verification fields (`Remote Geo Tagging`, `Positive verification`, `Physical
    verification by Third Party / RE Official`) as `Y` (ERR_260/262/263/264 were these being blank).
    It also only emits `Presence of Document in Repository` for Passport/Voter/DL/NREGA/NPR proof
    types (not Aadhaar — ERR_118).
  - `src\CKYC.Processor\Commands\InsertCommand.cs` — when **no current address** is supplied, it is
    defaulted to a copy of the permanent address ("same as permanent") instead of a different CRM default.
  Rebuild (`.\build.ps1`) so the running CLI picks them up.
- **`addressMatchWithOvd` must be a valid enum.** `"N"` → `ERR_191`; use `Exact Match` (doc matches the
  present address exactly) or `No Match`.
- **First build.** `build.ps1` uses `-m:1 -nodeReuse:false` (single-threaded) so it works under a
  restricted sandbox; it needs the .NET 10 SDK (present: `10.0.302`).
