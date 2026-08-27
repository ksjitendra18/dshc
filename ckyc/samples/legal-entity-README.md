# Legal-entity create — sample data for `insert-legal --file`

Feeds a legal-entity record (client type **L**) into the CKYC processor and takes it
through batch creation exactly per `vendor/legal-format-create.xlsx`
(File_Format_Upload_LegalEntity: records 10/20/30/40/50/60/70).

## Files

| File | Purpose |
|---|---|
| `legal-entity-create.json` | Full Private Limited Company (`entityConstitution`: "D") — every field explicit |
| `legal-entity-create-minimal.json` | Trust ("H") with only id + name + constitution; every omitted detail is filled from the dummy CRM defaults |
| `legal-docs/*.pdf` | Placeholder supporting documents referenced by both samples |

## Run

```powershell
cd ckyc
dotnet build -c Release src\CKYC.Processor
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

# 1. insert the legal entity
& $exe insert-legal --file .\samples\legal-entity-create.json
#    (or the minimal trust record:)
& $exe insert-legal --file .\samples\legal-entity-create-minimal.json

# 2. import its supporting documents (use the customerId you inserted)
& $exe documents import --customer-id ENT-LEGAL-DEMO-001 --dir .\samples\legal-docs

# 3. build the legal batch (.UPL + zip) and validate with the FVU
#    (settings must carry "versionNumber": "V1.1" and the FI code that matches the
#     Institution Code in the record — both samples use IN0238)
& $exe build-zip-legal
& $exe fvu
& $exe status
```

Notes:

* Every field in `legal-entity-create.json` follows the create workbook:
  PAN `AAAAA9999A` with the fourth character matching the constitution
  (C for companies, T for trusts, F for firms — FVU ERR_180), GST `22AAAAA0000A1Z5`,
  CIN `U50500MH2015PLC123456`, dates `DD-MM-YYYY`, 14-digit CKYC ids for related
  parties, a mandatory Beneficial Owner whose controlling interest / ownership
  percentage only appears on Beneficial Owner rows (ERR_111/ERR_258), and the
  record-70 Institution Code must equal the batch FI code from settings (ERR_395).
* Records are re-validated against the same spec by `LegalEntityRecordValidator`
  before batching, and the emitted `.UPL` layout is pinned by `CKYC.SpecChecks`
  (header 10→11 fields; details 20→25, 40→31, 50→12, 60→12, 70→21 and
  constitution-specific record 30 — company/LLP/others 11, trust/unincorporated 10,
  where the last column of each detail line is the empty Hash Value placeholder the
  FVU fills). Batch `versionNumber` in appsettings must be `V1.1` (ERR_172).
* A batch can contain at most 10 legal entities (`MaxLegalEntityBatchRecords`).
