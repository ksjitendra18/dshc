# CKYC UAT Workflow & Production Integration Guide

> Audience: the engineer performing **real UAT** of the Centralized CKYC Processor before/while
> swapping the demo stubs for the real integrations.
>
> This document has two parts:
>
> - **Part A — Running the pipeline today (with stubs):** what every command is, how to
>   install/build, how to provision the database, the complete command reference, and a
>   step-by-step walkthrough with a sample customer id (fetch → store → search → batch → FVU →
>   response), plus the legal-entity, bulk-search (`.SRC`), bulk-update (`.UPD`) and SFTP flows.
> - **Part B — What is stubbed and what to replace for real UAT:** every dummy/simulated/hardcoded
>   thing, the exact file to edit, and what to add. The **CRM integration is the biggest one** and
>   gets its own deep-dive section.
>
> Repo root for everything below: `D:\ckyccentral\ckyc`
> CLI executable: `src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe`

---

## Contents

- [Part A — Running the pipeline (with stubs)](#part-a--running-the-pipeline-with-stubs)
  1. [What this system is](#a1-what-this-system-is)
  2. [Prerequisites](#a2-prerequisites)
  3. [Build & install](#a3-build--install)
  4. [Database provisioning](#a4-database-provisioning)
  5. [Configuration overview (`appsettings.json`)](#a5-configuration-overview-appsettingsjson)
  6. [Command reference](#a6-command-reference)
  7. [End-to-end walkthrough with a sample customer id](#a7-end-to-end-walkthrough-with-a-sample-customer-id)
  8. [Legal-entity flow](#a8-legal-entity-flow)
  9. [Bulk search `.SRC` flow](#a9-bulk-search-src-flow)
  10. [Bulk update `.UPD` flow](#a10-bulk-update-upd-flow)
  11. [SFTP push / pull flow](#a11-sftp-push--pull-flow)
  12. [Verifying results](#a12-verifying-results)
  13. [Troubleshooting](#a13-troubleshooting)
- [Part B — Production integration (replace the stubs)](#part-b--production-integration-replace-the-stubs)
  - [B0. UAT readiness matrix](#b0-uat-readiness-matrix)
  - [B1. CRM integration (the biggest one)](#b1-crm-integration-the-biggest-one)
  - [B2. Customer search API](#b2-customer-search-api)
  - [B3. Daily customer-id source (CBS fetch)](#b3-daily-customer-id-source-cbs-fetch)
  - [B4. FVU integration](#b4-fvu-integration)
  - [B5. CERSAI SFTP transport](#b5-cersai-sftp-transport)
  - [B6. Channel image/document fetch (beckyc)](#b6-channel-imagedocument-fetch-beckyc)
  - [B7. Database](#b7-database)
  - [B8. FI identifiers & file naming](#b8-fi-identifiers--file-naming)
  - [B9. Simulation knobs to switch off](#b9-simulation-knobs-to-switch-off)
  - [B10. Supporting-document generation](#b10-supporting-document-generation)
  - [B11. Hardcoded sample data to scrub](#b11-hardcoded-sample-data-to-scrub)
  - [B12. Environment paths & tooling](#b12-environment-paths--tooling)
  - [B13. UAT execution checklist and test cases](#b13-uat-execution-checklist-and-test-cases)

---

# Part A — Running the pipeline (with stubs)

## A1. What this system is

A centralized **CKYC (Central KYC Record Registry)** batch-processing pipeline. It is a `.NET 10`
solution invoked as a single CLI executable (`CKYCProcessor.exe`) with **independent
sub-commands — one per pipeline step** — so every stage can be run on its own and orchestrated by
a scheduler. It talks to a (dummy, for now) **CRM API** and to **CERSAI** through the vendor
**FVU** validation utility and the vendor **SFTP** utility.

| Project | Responsibility |
|---|---|
| `src/CKYC.Core` | Domain models (`Individual`, `LegalEntity`), record types 20/30/40/50/60/70, settings, CKYC file-format spec, contracts (repositories / CRM / batch / FVU / hashing). |
| `src/CKYC.Data` | EF Core 10 / SQL Server persistence, repositories, document store, batch/FVU audit journal. |
| `src/CKYC.Crm` | **Dummy CRM** (`DummyCrmDataProvider`, `DummyCrmLegalEntityProvider`), the HTTP CRM client (`HttpCrmApiClient`), the in-process customer-search simulator (`DummyIndividualSearchApi`), the HTTP search client (`HttpIndividualSearchApiClient`), and the self-hosted Kestrel API (`CrmServer`, run by `crm serve`). |
| `src/CKYC.Files` | `.UPL`/`.SRC`/`.UPD` writers, batch generator, response parsers, and per-channel supporting-document rendering (QuestPDF). |
| `src/CKYC.Fvu` | Real `FVU_RUN_UTILITY.exe` invocation + deterministic simulation fallback. |
| `src/CKYC.Sftp` | Real `SFTPRunner.exe` integration + deterministic simulation; the `beckyc` SFTP document source. |
| `src/CKYC.Processor` | CLI host, composition root (`AppContext`), command registry, `appsettings.json` binding. |

### The pipeline at a glance

```
fetch cust        # 1. daily customer ids  -> master table (CBS fetch; retryable)
insert            #    create a NEW customer record (manual details)
documents import  #    supporting files -> database (manual/staging)
crm serve         # 2. dummy CRM API (replace with production)
store             # 3. CRM -> record tables (channel gate -> ImagePending or PendingSearch)
documents fetch   # 3b. per-channel image/document (beckyc SFTP) -> PendingSearch
search-customer   # 4. per-customer search API (match -> SearchFound; else record-20 key -> Searched)
retry             #    retry failed records (exponential backoff, max 3 tries)
reattempt         #    re-push a single rejected record after a backend DB fix
build-zip         # 5. Searched records -> .UPL + support_docs zip
fvu               # 6. batch -> FVU -> processed zip + hash
sftp push         # 6b. validated .UPL zip -> CERSAI SFTP (marks records Uploaded)
sftp pull         # 6c. CERSAI SFTP -> processed response files (download only)
response read     # 7. CERSAI reply (.UPL.RESm) -> response table + master summary
reconcile         #    manual-intervention report (retry-exhausted + CERSAI-failed)
status            #    pipeline snapshot (current stage per record)
```

Separate flows: `.SRC` bulk search (`search-load/process/fvu/response`), `.UPD` bulk update
(`update-load/process/fvu/response`), and `.DWN.RES` download-response ingestion
(`download-response`).

---

## A2. Prerequisites

| # | Requirement | Notes |
|---|---|---|
| 1 | **.NET 10 SDK** (`net10.0`) | `dotnet --version` ≥ `10.0.302`. The ASP.NET Core runtime needed by `CKYC.Crm` ships with the SDK. |
| 2 | **PowerShell** | `build.ps1` / `run.ps1`. |
| 3 | **SQL Server or SQL Server LocalDB + `sqlcmd`** | Demo uses `(localdb)\MSSQLLocalDB` / `CkycCentral`. |
| 4 | **CERSAI FVU utility** `FVU_RUN_UTILITY.exe` | Expected at `D:\ckyccentral\vendor\FVU_RUN_UTILITY.exe`. Optional: set `fvu.useRealFvu=false` to simulate. |
| 5 | **CERSAI SFTP utility** `SFTPRunner.exe` | At `D:\ckyccentral\vendor\SFTP_Utility_windows\SFTPRunner.exe`. Optional: `sftp.useRealSftp=false`. |
| 6 | Document templates | `doc_format\` (`Template_1.pdf`, `Aadhaar_template_blank.jpg`, `Consent_template_blank.png`). Only when `documentGeneration.enabled=true`. |
| 7 | NuGet packages | Centrally pinned in `Directory.Packages.props`: EF Core SqlServer 10.0.10, NLog 6.1.4, QuestPDF 2025.1.0, SSH.NET 2025.1.0. |

> **Fresh-machine NuGet note:** `nuget.config` points at a **local offline cache**
> (`C:\Users\offic\.nuget\packages`) and clears remote sources. On another machine either delete
> `nuget.config` (restore from nuget.org) or repoint `local-cache`/`globalPackagesFolder`.

> **Warnings are errors:** `Directory.Build.props` sets `TreatWarningsAsErrors=true` plus
> `EnableNETAnalyzers`. A single compiler/analyzer warning fails the build.

---

## A3. Build & install

```powershell
cd D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Manual equivalent (what the script does):

```powershell
dotnet build src\CKYC.Processor\CKYC.Processor.csproj -c Release -p:NuGetAudit=false -m:1 -nodeReuse:false
```

Output:

```
src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe
```

For the rest of this document:

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
```

> **Run from the repo root (`D:\ckyccentral\ckyc`).** `appsettings.json` is resolved from the
> current working directory first, then the exe directory; and `doc_format`, `staging`, `runtime`
> paths are relative to the working directory. If you run from elsewhere, pass
> `--settings <absolute path>`.

> **`--settings` is a full replacement, not a merge.** `SettingsLoader.Load` starts from built-in
> C# defaults and, when a file is supplied, **replaces** the whole object with that file. A partial
> override such as `samples\settings-search-skip.json` therefore also reverts every other section
> to code defaults (e.g. `batch.outputRoot` → `output`, `fvu.exePath` →
> `D:\centralprocessing\...`). Prefer copying `appsettings.json` and editing the copy.

---

## A4. Database provisioning

The database is **database-first**: `scripts\sqlserver\schema.sql` owns all DDL and seed rows; the
app only **verifies** the schema at startup and never runs DDL (`createSchemaOnStartup` is
compatibility-only).

Clean local re-provision:

```powershell
# drop + recreate
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "IF DB_ID('CkycCentral') IS NOT NULL BEGIN ALTER DATABASE [CkycCentral] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [CkycCentral]; END; CREATE DATABASE [CkycCentral];"

# apply the authoritative schema (tables + seeds)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\schema.sql
```

Migrations under `scripts\sqlserver\migrations\` are only needed for an **older** database
(all guarded/idempotent):

| Migration | Adds |
|---|---|
| `20260827_fix_address_match_width.sql` | Widens record-40 match columns to `NVARCHAR(13)`. |
| `20260910_add_master_record_source.sql` | `master_record.Source` (default `beckyc`). |
| `20260910_rename_search_download_tables_to_bulk.sql` | Renames `search_*`/`download_response_*` → `bulk_*`. |
| `20260910_add_individual_search.sql` | `individual_search` table, statuses 12/13/14, `Search` activity. |
| `20260924_add_sftp_activity_types.sql` | `SftpUpload`, `SftpDownload` activities. |
| `20260928_add_document_fetch.sql` | `master_record.DocumentKey`, `IsImageFetched`/`ImageFetchedAt`, statuses 15/16, `ImageFetch` activity. |

Startup schema verification returns a clear `THROW` (codes `50000`–`50006`) naming the missing
migration if the DB is stale — every command fails until the schema matches.

---

## A5. Configuration overview (`appsettings.json`)

| Section | Key fields | Meaning |
|---|---|---|
| `database` | `provider` (`sqlserver`), `connectionString`, `createSchemaOnStartup` | Persistence. Only `sqlserver` is supported. |
| `source` | `mode` (`generate`/`file`), `generateCount`, `generateSeed`, `filePath`, `documentKeyProperty` | Step-1 daily customer-id source. |
| `crm` | `mode`, `baseUrl`, `customersEndpoint`, `listEndpoint`, `timeoutSeconds` | CRM wiring. |
| `batch` | `userId`, `fiCode`, `regionCode`, `clientType`, `versionNumber`, `sequenceStart`, `outputRoot` | `.UPL` file naming + output. |
| `fvu` | `exePath`, `workspaceRoot`, `apiBaseUrl`, `apiEndpoint`, `triggerSource`, `maxRetries`, `useRealFvu` | Step-6 FVU validation. |
| `response` | `inputRoot` | Folder scanned by `response read`. |
| `simulation` | `saveErrorsEnabled`, `saveErrorEvery`, `saveErrorForCustomerId`, `fvuFailEvery`, `cbsFetchErrorsEnabled`, `cbsFetchFailEvery`, `cbsFetchFailForCustomerId` | Deterministic failure injection. |
| `retry` | `maxAttempts`, `backoffBaseHours`, `backoffMultiplier` | Seeds `activity_type` (24h × 2, max 3). |
| `search` | `userId`, `fiCode`, `regionCode`, `versionNumber`, `sequenceStart`, `claimTimeoutMinutes`, `outputRoot` | `.SRC` bulk-search file naming/output. |
| `searchApi` | `enabled`, `mode` (`InProcess`/`Http`), `baseUrl`, `searchEndpoint`, `timeoutSeconds`, `claimTimeoutMinutes`, `simulateFoundEvery`, `simulateErrors*` | Pre-batch customer search. |
| `documentGeneration` | `enabled`, `templateRoot`, `documents[]` | Rendered supporting docs at `build-zip`. |
| `documentFetch` | `enabled`, `downloadRoot`, `channels.<channel>` (`kind`, `useRealSftp`, `host`, `port`, `username`, `password`, `basePath`, `folderPattern`, `documents[]`) | Pre-search channel image/doc fetch. |
| `sftp` | `enabled`, `exePath`, `workspaceRoot`, `outboundRoot`, `downloadPath`, `host`, `username`, `password`, `fiCode`, `proxy`, `useRealSftp`, `archiveUploadedFiles` | CERSAI transport. |

`update` is a valid section (defaults only; not present in the shipped file): `userId`, `fiCode`,
`regionCode`, `versionNumber`, `sequenceStart`, `claimTimeoutMinutes`, `outputRoot` (`runtime/update`).

> In the shipped `appsettings.json` several roots still point at the original
> `D:\centralprocessing\...` tree (`batch.outputRoot`, `fvu.exePath`, `fvu.workspaceRoot`,
> `response.inputRoot`, `search.outputRoot`, `documentFetch.downloadRoot`). Fix these to
> `D:\ckyccentral\...` before a local run — see [B12](#b12-environment-paths--tooling).

---

## A6. Command reference

The first argument selects the command (case-insensitive). `--settings <path>` may appear anywhere
and is stripped before dispatch. Exit codes: `0` success, `1` error, `130` cancelled. `help` /
`--help` / no args prints the registry help.

### Status model (master record `Status`)

| Value | Code | Name | Meaning |
|---|---|---|---|
| 0 | PND | Pending | fetched, awaiting CRM enrichment |
| 1 | CRM | CrmFetched | CRM data fetched |
| 2 | SAV | Saved | detail records saved |
| 3 | BAT | Batched | included in a generated `.UPL` |
| 4 | FVP | FvuPassed | FVU-validated, awaiting SFTP push |
| 5 | FVF | FvuFailed | FVU validation failed |
| 6 | FLD | Failed | a stage failed (retry bookkeeping) |
| 7 | UPL | Uploaded | uploaded to CERSAI |
| 8 | RSP | ResponseRead | CERSAI reply read |
| 9 | RCN | Reconciled | accepted/reconciled |
| 10 | REJ | Rejected | rejected by CERSAI |
| 11 | DTF | DataFetchFailed | source/CBS data fetch failed |
| 12 | SRP | PendingSearch | awaiting pre-batch customer search |
| 13 | SRD | Searched | search done, record-20 search key set → batchable |
| 14 | SRF | SearchFound | already exists in CKYC (terminal, not batched) |
| 15 | IMP | ImagePending | awaiting channel image/document fetch |
| 16 | IMF | ImageFailed | channel image/document fetch failed (retryable) |

Retryable activities (`activity_type`): `CbsFetch`, `Crm`, `Store`, `Search`, `ImageFetch`.
Non-retryable (surface in `reconcile`): `BuildZip`, `FvuUpload`, `SftpUpload`, `SftpDownload`,
`Response`, `Reconciliation`.

### Command summary

| Command | Usage (verbatim) |
|---|---|
| `fetch` | `CKYCProcessor.exe fetch cust [--file custid.json] [--date yyyy-MM-dd] [--count N]` |
| `insert` | `CKYCProcessor.exe insert --file <customer.json>` / inline flags |
| `insert-legal` | `CKYCProcessor.exe insert-legal --file <entity.json>` / inline flags |
| `documents` | `documents import --customer-id <id> --dir <path>` \| `documents fetch [--limit N] [--customer <id>] [--channel <name>]` |
| `crm` | `CKYCProcessor.exe crm serve [--urls http://127.0.0.1:5291]` |
| `store` | `CKYCProcessor.exe store [--limit N]` |
| `search-customer` | `CKYCProcessor.exe search-customer [--limit N] [--customer <customerId>]` |
| `retry` | `CKYCProcessor.exe retry [--activity <code>] [--limit N]` |
| `reattempt` | `CKYCProcessor.exe reattempt --id <recordId> \| --customer <customerId> [--reason "..."]` |
| `build-zip` | `CKYCProcessor.exe build-zip [--limit N]` |
| `build-zip-legal` | `CKYCProcessor.exe build-zip-legal [--limit N]` |
| `batch-find` | `CKYCProcessor.exe batch-find --customer <customerId>` |
| `fvu` | `CKYCProcessor.exe fvu [--batch <key>]` |
| `response` | `CKYCProcessor.exe response read [--batch <key>] [--dir <folder>] [--file <path>]` |
| `reconcile` | `CKYCProcessor.exe reconcile [--kind retry\|cersai] [--out <path>] [--stakeholder <name>]` |
| `status` | `CKYCProcessor.exe status` |
| `search-load` | `CKYCProcessor.exe search-load [search_customer.json]` |
| `search-process` | `CKYCProcessor.exe search-process [--limit N] [--date yyyy-MM-dd]` |
| `search-fvu` | `CKYCProcessor.exe search-fvu [--file ...SRC]` |
| `search-response` | `CKYCProcessor.exe search-response [--path runtime\search\response]` |
| `update-load` | `CKYCProcessor.exe update-load [updates.json]` |
| `update-process` | `CKYCProcessor.exe update-process [--limit N] [--date yyyy-MM-dd] [--client I\|L]` |
| `update-fvu` | `CKYCProcessor.exe update-fvu [--file ...UPD]` |
| `update-response` | `CKYCProcessor.exe update-response [--path runtime\update\response]` |
| `download-response` | `CKYCProcessor.exe download-response --path <file-or-folder>` |
| `sftp` | `CKYCProcessor.exe sftp <push\|pull>` |

### Per-command notes

- **`fetch`** — reads the daily customer-id set: `--file <path>` (JSON or one-id-per-line text),
  else the `custid` token (reads `custid.json` from the CWD, then the app dir), else the configured
  `source` provider (`generate`/`file`). Inserts `Pending` master rows (`documentKey` stored on
  `master_record.DocumentKey`). With the CBS simulation on, a deterministic subset is recorded
  `Failed` with retry bookkeeping. (*`--count` appears in the usage string but is not implemented.*)
- **`insert`** — creates an individual with your own details; omitted proof/address/contact/other
  fields are filled from the **dummy CRM provider** so a minimal input still validates. Ends at
  `PendingSearch`.
- **`insert-legal`** — same for a legal entity; ends at `Saved` (there is no CRM `store` step for
  legal entities).
- **`documents import`** — imports referenced PDF/JPG/JPEG bytes from a staging directory into
  `file_content` + the document table (SHA-256 dedup; signature/size validated). Every referenced
  file must be staged.
- **`documents fetch`** — for records at `ImagePending`, resolves the channel source (only `beckyc`
  is implemented), pulls the configured file(s) by `dockey`, imports them, repoints the record
  slot(s) and advances to `PendingSearch`. Missing file → `ImageFailed` (`retry --activity ImageFetch`).
- **`crm serve`** — starts the bundled dummy CRM Kestrel API (`GET /api/customers`,
  `GET /api/customers/{id}`, `GET /health`) and blocks. Not needed once pointed at a real CRM.
- **`store`** — for each `Pending` record: `GET` the CRM record; save the detail tables; then
  `ImagePending` (channel has a source) or `PendingSearch`. Simulated save errors are injected per
  the `simulation` knobs.
- **`search-customer`** — builds `individual_search` candidate rows (one option-1 row per identity
  document + a name/DOB/gender/relation option-2 fallback), calls the search API until one matches:
  match → `SearchFound` (terminal); no match → 20-char search key written to record 20 → `Searched`.
  `searchApi.enabled=false` makes it a pass-through to `Searched`.
- **`retry`** — re-runs failed records whose exponential backoff has elapsed (only retryable
  activities). Exhausted records are flagged for reconciliation.
- **`reattempt`** — snapshots the previous response into `master_record_reattempt`, then resets a
  rejected record (`→ Saved`/`PendingSearch`) so it re-batches and re-submits.
- **`build-zip`** — batches `Searched` individuals: renders per-channel generated docs, writes the
  pipe-delimited `.UPL` + `support_docs` zip, marks records `Batched` and stamps the record-20 line.
- **`fvu`** — clones the batch, writes the FVU `config.yaml`, runs the FVU, extracts the processed
  zip + SHA-256. With `sftp.enabled=true` records become `FvuPassed` and the validated `.UPL.zip`
  lands in the SFTP outbound folder; otherwise they become `Uploaded`.
- **`response read`** — reads `<upload>.UPL.RESm` reply files, writes `master_record_response` and
  the `LastResponse*` summary, and advances records to `Reconciled` (`01`/`02`) or `Rejected`.
- **`reconcile`** — CSV report of retry-exhausted and/or CERSAI-failed records.
- **`status`** — counts by status + last batch (read-only).
- **`search-*` / `update-*`** — the `.SRC` bulk-search and `.UPD` bulk-update flows (see A9/A10).
- **`download-response`** — parses `.DWN.RES` ZIP snapshots into immutable lines + artifact inventory.
- **`sftp push|pull`** — upload validated `.UPL.zip` payloads (marks records `Uploaded`) / download
  processed response files. Requires `sftp.enabled=true`.

---

## A7. End-to-end walkthrough with a sample customer id

There are three ways to get a record into the pipeline. Pick one track.

### Setup (once)

```powershell
cd D:\ckyccentral\ckyc
powershell -ExecutionPolicy Bypass -File .\build.ps1
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

# fresh DB
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "IF DB_ID('CkycCentral') IS NOT NULL BEGIN ALTER DATABASE [CkycCentral] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [CkycCentral]; END; CREATE DATABASE [CkycCentral];"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\schema.sql
```

### Track 1 — Insert only (no CRM, quickest smoke test)

Uses `samples\retail-customer.json` (customer id `CUST-RETAIL-SKSS`).

```powershell
& $exe insert --file .\samples\retail-customer.json          # -> PendingSearch (SRP)

# (optional) import the real staging documents for this customer
& $exe documents import --customer-id CUST-RETAIL-SKSS --dir .\staging\CUST-RETAIL-SKSS

& $exe search-customer                                       # SRP -> Searched (SRD); search key in record 20
& $exe build-zip                                             # .UPL + support_docs zip (note the batch key)
& $exe fvu                                                   # validate -> processed zip + hash
& $exe status
```

Fully inline variant:

```powershell
& $exe insert --customer-id CUST-RETAIL-0001 --name "Ashish Kumar" --dob 15-04-1988 --gender M `
              --email ashish.kumar@yopmail.com --mobile 9876543210
```

> **`insert` ends at `PendingSearch`.** `build-zip` batches only `Searched` records, so you must run
> `search-customer` in between (or use `--settings` with `searchApi.enabled=false` to move
> `SRP → SRD` without an API call — but remember the full-replacement caveat in A3).

### Track 2 — Full source fetch → CRM → image → search → batch → FVU (sample `RJKS2026`)

`custid.json` ships `{ "customerId": "RJKS2026", "documentKey": "9f2c7a4e81b3d6f0a5c2" }`.

```powershell
& $exe fetch custid                                          # loads RJKS2026 (+dockey) as Pending

# start the dummy CRM once, keep it running (separate window / background job)
& $exe crm serve --urls http://127.0.0.1:5291

& $exe store                                                 # CRM enrich + save; may stop at IMP (beckyc)
& $exe retry                                                 # recover simulated save failures (if any)
& $exe documents fetch --customer RJKS2026                   # beckyc image -> PendingSearch
& $exe search-customer                                       # -> Searched / SearchFound
& $exe build-zip
& $exe fvu
& $exe status
```

`run.ps1` automates exactly this sequence (starts the CRM as a background job, runs 8 steps, stops it):

```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

> **Important — the image gate.** With the shipped `documentFetch` config, any record whose
> `source` is `beckyc` stops at **`ImagePending` (`IMP`)** after `store`, and `build-zip` refuses to
> batch it until `documents fetch` succeeds. The shipped beckyc host is a **placeholder**
> (`sftp-beckyc.example.in`) with **empty credentials**, so the real fetch fails until [B6](#b6-channel-imagedocument-fetch-beckyc)
> is done. For an offline first run choose one of:
> - set `documentFetch.enabled=false` (restores legacy behaviour — no image gate), or
> - set `documentFetch.channels.beckyc.useRealSftp=false` and place a file at
>   `runtime\document-fetch\inbox\beckyc\9f2c7a4e81b3d6f0a5c2\image.jpg`, or
> - use `insert` (Track 1), which bypasses `store` and the SFTP path.

### Track 3 — Fetch from an explicit source file

```powershell
# JSON with dockey + channel (3 customers; two with source=beckyc)
& $exe fetch cust --file .\samples\document-fetch-source.json

& $exe store
& $exe documents fetch
& $exe search-customer
& $exe build-zip
& $exe fvu
& $exe status
```

Plain-text source files (one customer per line) are also supported:
`customerId[,documentKey[,source]]` separated by comma/tab/pipe/semicolon.

### Response read (after CERSAI replies)

With `sftp.enabled=true`, the flow is `fvu` → `sftp push` → (CERSAI approves/processes) →
`sftp pull` → `response read --dir .\runtime\sftp\downloads`. Without SFTP, put the reply file in
`response.inputRoot` (or pass `--dir`/`--file`) and run `response read`.

```powershell
& $exe response read
& $exe status
```

---

## A8. Legal-entity flow

```powershell
& $exe insert-legal --file .\samples\legal-entity-create.json     # -> Saved (SAV)
& $exe documents import --customer-id ENT-LEGAL-DEMO-001 --dir .\staging\ENT-LEGAL-PROP-001
& $exe build-zip-legal                                            # batches Saved legal records
& $exe fvu
& $exe status
```

Legal entities batch from `Saved` (no search step). If SFTP is enabled, `fvu` stages the validated
ZIP under `sftp\outbound\LEGAL_ENTITY` and `sftp push` marks records `Uploaded`.

---

## A9. Bulk search `.SRC` flow

```powershell
& $exe search-load .\samples\search_customer.json
& $exe search-process --limit 1000        # claim pending rows -> writes .SRC under runtime/search
& $exe search-fvu                         # FVU-validate the latest generated .SRC
& $exe search-response                    # import .SRC.RESm replies from runtime/search/response
```

Writers produce `IRA000337_IN9797_DDMMYYYY_nnnnn.SRC` under `search.outputRoot`. Response imports
are SHA-256 deduplicated.

## A10. Bulk update `.UPD` flow

```powershell
& $exe update-load .\samples\update-individual.json
& $exe update-load .\samples\update-legal.json
& $exe update-process --limit 1000 --client I    # and/or --client L; writes .UPD + support_docs
& $exe update-fvu
& $exe update-response
```

Each `.UPD` amends an existing record (14-digit CKYC number + per-section `*Update Flg` switches).
A section's detail line is written only when it carries amendment payload.

## A11. SFTP push / pull flow

Requires `sftp.enabled=true`.

```powershell
& $exe build-zip
& $exe fvu             # validated .UPL.zip -> sftp\outbound\INDIVIDUAL (records -> FvuPassed)
& $exe sftp push       # upload; records -> Uploaded; uploaded zips archived
# ...CERSAI approves + processes...
& $exe sftp pull       # download response files into sftp.downloadPath
& $exe response read --dir .\runtime\sftp\downloads
```

---

## A12. Verifying results

```powershell
& $exe status
& $exe batch-find --customer RJKS2026
```

Direct DB inspection:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -Q "SELECT Id,CustomerId,ClientType,Status,StatusCode,BatchFile,DocumentKey,LastError FROM master_record ORDER BY Id;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -Q "SELECT TOP (1) BatchKey,ExitCode,Passed,HashValue FROM fvu_run ORDER BY Id DESC;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -Q "SELECT TOP (20) CustomerId,Stage,Success,Error,AttemptedAt,NextRetryAt FROM master_record_attempt ORDER BY Id DESC;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -Q "SELECT CustomerId,SearchOption,IdentityTypeAndNumber,Outcome,SearchKey,CkycReferenceNumber FROM individual_search ORDER BY Id;"
```

Artifacts:

- `runtime\output\<BatchKey>\` — `.UPL`, `support_docs`, zip
- `runtime\runs\<BatchKey>\output\` — FVU-processed zip, Executive Summary PDF, hash
- `runtime\sftp\outbound\{INDIVIDUAL,LEGAL_ENTITY}\` — validated `.UPL.zip` staged for upload
- `runtime\search\` / `runtime\update\` — `.SRC` / `.UPD` outputs
- Logs: `logs\` (NLog)

---

## A13. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Build fails with a `CA…` warning | `TreatWarningsAsErrors` + analyzers are on; fix the analyzer hint. |
| Restore fails on a fresh machine | `nuget.config` local cache; delete it or repoint (A2). |
| Command throws a `5000x` schema error | Run `scripts\sqlserver\schema.sql` + the named migration. |
| `fvu` exits `-1` with empty tmp | PyInstaller sandbox/temp block; run outside the sandbox or set `fvu.useRealFvu=false`. |
| `fvu` exits `3` | A `.UPL` field failed validation; check the per-run `.ERR` file. |
| No `Pending` records in `store` | `fetch` didn't load ids; run `fetch custid` / `fetch cust --file`. |
| Record stuck at `PendingSearch`, not batched | Run `search-customer` (or `searchApi.enabled=false`). |
| Record stuck at `IMP` | `documents fetch` didn't run / beckyc source failed; fix config then `retry --activity ImageFetch`. |
| Record stuck at `Saved` | `build-zip` batches `Searched` individuals (legal entities: `Saved`). |
| `retry` finds nothing | Backoff is 24h × 2; zero it for drills (`samples\failure\scripts\expedite-retry.sql`). |
| `--settings` run behaves oddly | Full replacement, not merge (A3). |
| Batch overwrote an earlier one | `batch.sequenceStart` is fixed and does not auto-increment; raise it per run/day. |

---

# Part B — Production integration (replace the stubs)

This is the core of the pre-UAT work. Each section lists the **current stub**, the **files**, and
**what to add/change**.

## B0. UAT readiness matrix

| # | Area | Current (demo) | Target for real UAT | Type |
|---|---|---|---|---|
| 1 | **CRM** | `DummyCrmDataProvider` + `crm serve` | real CRM endpoint over HTTP | config + code |
| 2 | **Search API** | `DummyIndividualSearchApi` | real search API over HTTP | config + code (response mapping) |
| 3 | **Source (CBS)** | `generate` IDs / `custid.json` | real daily feed (`mode=file` or provider) | config + code (retry stub) |
| 4 | **FVU** | `SimulatedFvuRunner` fallback | real `FVU_RUN_UTILITY.exe` | config |
| 5 | **SFTP** | `SimulatedSftpRunner`, preprod host, empty password | real host/creds/fiCode | config |
| 6 | **Beckyc doc fetch** | placeholder host, empty creds, `x/y/z`, `image.jpg` | real host/creds/path/file | config (+ code if layout differs) |
| 7 | **Database** | LocalDB `CkycCentral` | real SQL Server | config |
| 8 | **FI identifiers** | `IRA000337`/`IN9797`, `IAU010441`/`IN0238` | real User IDs / FI codes / region | config |
| 9 | **Simulation** | save/CBS/search failure injection **on** | all off | config |
| 10 | **Doc generation** | sample templates + hardcoded coordinates | real templates/calibration | config + code |
| 11 | **Sample data** | hardcoded names/PANs/record-70 constants | real data from CRM | code (remove dummy) |
| 12 | **Paths/tooling** | `D:\centralprocessing\...`, machine-local nuget cache | deployment paths | config |

---

## B1. CRM integration (the biggest one)

### B1.1 How the CRM is wired today

- `src\CKYC.Processor\AppContext.cs` (constructor):
  ```csharp
  CrmData        = new DummyCrmDataProvider();          // fake individual data
  CrmLegalEntities = new DummyCrmLegalEntityProvider(); // fake legal-entity data
  Crm            = new HttpCrmApiClient(settings.Crm);  // the client actually used by `store`
  CrmServer      = new CrmServer(CrmData, CustomerIds); // served by `crm serve`
  ```
- `store` calls the client, not the dummy, for enrichment:
  `src\CKYC.Processor\Commands\StoreService.cs` → `_ctx.Crm.GetCustomerAsync(record.CustomerId, ct)`.
- Contract: `src\CKYC.Core\Abstractions\Services.cs`
  ```csharp
  public interface ICrmApiClient
  {
      Task<IReadOnlyList<string>> GetCustomerIdsAsync(CancellationToken ct = default);
      Task<Individual?> GetCustomerAsync(string customerId, CancellationToken ct = default);
  }
  ```
- Implementation: `src\CKYC.Crm\HttpCrmApiClient.cs`
  - `GetCustomerAsync(id)` → `GET {customersEndpoint}` with `{id}` substituted (default `/api/customers/{id}`),
    deserialized with `GetFromJsonAsync<Individual>` (System.Text.Json, **camelCase**).
  - `GetCustomerIdsAsync()` → `GET {listEndpoint}` (`/api/customers`). **Note:** this method is
    defined but **never called by the pipeline** — `fetch` uses the `source`/`custid.json` feed, not
    the CRM list endpoint. You can ignore `listEndpoint` for UAT.
- Settings: `src\CKYC.Core\Configuration\AppSettings.cs` → `CrmSettings`
  (`Mode`, `BaseUrl`, `CustomersEndpoint`, `ListEndpoint`, `TimeoutSeconds`).

### B1.2 Critical facts before you touch it

1. **`crm.mode` is not read anywhere in the code.** The client is *always* `HttpCrmApiClient`;
   `InProcess` vs `Http` only describes whether you also run the dummy `crm serve`. So to use the
   real CRM you **do not need to change `mode`** — you must:
   - set `crm.baseUrl` (and the endpoints) to the real CRM, and
   - **stop running `crm serve`** (remove it from `run.ps1` / your scheduler).
2. **Non-2xx responses throw.** `GetFromJsonAsync` throws on a non-success status; `StoreService`
   catches it and marks the record `Failed` with activity `Crm` (retryable, 24h × 2, max 3). Confirm
   the real CRM's error semantics (404 vs 500) and map them if "no data" should be non-retryable.
3. **`insert` / `insert-legal` still depend on the dummy providers.** `InsertCommand` (around
   `ApplyValidDefaults`) calls `ctx.CrmData.GetCustomer(...)`; `InsertLegalCommand` calls
   `ctx.CrmLegalEntities.GetLegalEntity(...)`. These are **concrete classes with no interface**, so
   `insert` cannot borrow defaults from the real CRM without a code change. For UAT either supply
   full JSON to `insert`/`insert-legal`, or add the abstraction in B1.4.
4. **There is no HTTP client for legal entities and no `store` path for client type `L`.**
   `GetCustomerAsync` returns an `Individual`; `store` only enriches individuals. Legal entities enter
   only via `insert-legal` → `Saved`. To pull legal entities from a real CRM you must add the client
   method + `store` handling (B1.5).

### B1.3 What the real CRM must return (individual)

`GET {crm.customersEndpoint}` must return a JSON `Individual`. The full shape is
`src\CKYC.Core\Domain\Individual.cs`:

- **Record 20 (demographics):** `searchKey`, `kycType`, `name` (`title`/`firstName`/`middleName`/`lastName`),
  `maidenName`, `motherName`, `fatherName`, `spouseName`, `dateOfBirth` (`DD-MM-YYYY`), `gender` (`M`/`F`/`T`),
  `residentialStatus` (`Resident`/`NRI`/`PIO`/`ForeignNational`), `residentialStatusSupportedByDocument`,
  `nationality` (ISO-2, e.g. `IN`), `nationalitySupportedByDocument`, `differentlyAbledStatus`,
  `differentlyAbledType`, `pan`, `panVerified`, `photoOfIndividual`, conditional-mandatory flags
  (`minor`, `dateOfBirthMatchWithOvd`, `nameMatchWithOvd`, `photoProvidedMatchWithOvd`,
  `genderProvidedInOvd`, `genderMatchWithOvd`, `form97Provided`, `form61Provided`, `panDocument`, PwD fields).
- **Record 30:** `proofs[]` → `ovdType`, `modeOfAadhaarVerification`, `passportExpiryDate`,
  `drivingLicenseExpiryDate`, `lengthOfAadhaar`, `idNumber`, `certifiedCopyWithOriginal`,
  `equivalentEDoc`, `verifiedFromDigiLocker`, `presenceIn*Repository`, `dataFromOfflineVerification`,
  `modeOfAuthentication`, `ekycDataFromUidai`, `copyOfOvd`.
- **Record 40:** `permanentAddress` / `currentAddress` (`line1..3`, `country`, `state`, `district`,
  `city`, `pinCode`, `addressSupportedWithDocument`, `addressMatchWithOvd`, plus the current-address
  POA block), `currentAddressSameAsPermanent`.
- **Record 50:** `contact` → `email`, `countryCode`, `mobileNumber`, validation flags.
- **Record 60:** `relatedParties[]` → `relatedPersonType`, `ckycNumberOfRelatedPerson`.
- **Record 70:** `other` → `remarks`, KYC-method flags, `attestationDate`, `employeeName`,
  `employeeCode`, `employeeDesignation`, `employeeBranch`, `employeeCkycId`, `institutionName`,
  `institutionCode`, `declarationDocument`, `declarationFlag`, `clientConsent`, `place`,
  `declarationDate`.

The JSON property names are matched **case-insensitively** (nested objects included). "Must be
FVU-valid" rules apply — see `src\CKYC.Core\Spec\CkycRecordValidator.cs` (`ERR_*` rules) and
`samples\*.json` for working examples.

### B1.4 What to add / change for the real CRM (checklist)

1. **Config** — `appsettings.json` → `crm`:
   ```jsonc
   "crm": {
     "mode": "Http",                              // documentation only, but set for clarity
     "baseUrl": "https://<real-crm-host>",
     "customersEndpoint": "/api/customers/{id}",  // match the real route
     "listEndpoint": "/api/customers",            // unused by the pipeline
     "timeoutSeconds": 30
   }
   ```
   Set `crm.baseUrl` **without** a trailing slash if `customersEndpoint` starts with `/`.
2. **Remove `crm serve`** from `run.ps1` and any scheduler/orchestration.
3. **Auth / TLS (if the real CRM needs it)** — none is implemented. The cleanest place is
   `src\CKYC.Crm\HttpCrmApiClient.cs`: switch to an `IHttpClientFactory` (or accept an injected
   `HttpClient` — the constructor already allows `HttpClient? http = null`) and add
   `DefaultRequestHeaders.Authorization`, an API key header, client certificates, or a proxy as
   required. Register the handler in `AppContext.cs`.
4. **Make `insert` defaults pluggable (optional but recommended).** Introduce
   `ICrmDataProvider` with `GetCustomer` / `GetLegalEntity`, implement it over `ICrmApiClient`, and
   change:
   - `AppContext.cs` → type the properties as the interface;
   - `InsertCommand.cs` → replace `ctx.CrmData.GetCustomer(...)`;
   - `InsertLegalCommand.cs` → replace `ctx.CrmLegalEntities.GetLegalEntity(...)`.
   Otherwise keep supplying full JSON to `insert`.
5. **Customer-id feed** — if the real flow expects `store` to discover ids from the CRM list
   endpoint, wire `ICrmApiClient.GetCustomerIdsAsync` into `fetch` (it is currently unused).
6. **Error mapping** — decide which CRM responses are retryable. `StoreService.FailAsync` already
   uses activity `Crm` (retryable) for "no data" and `Store` for save failures. Adjust if the real
   CRM distinguishes transient vs permanent errors.
7. **Record-70 attestation values** — the dummy hardcodes `institutionName`/`institutionCode`/
   `employeeName`/`employeeCode`/`employeeBranch` (see B11). In production these must come from the
   CRM (or your institution config), not the dummy.

### B1.5 Adding legal-entity CRM support (client type `L`)

There is currently **no** legal CRM path. To add one:

1. `src\CKYC.Core\Abstractions\Services.cs` → extend `ICrmApiClient`:
   ```csharp
   Task<LegalEntity?> GetLegalEntityAsync(string customerId, CancellationToken ct = default);
   ```
2. `src\CKYC.Crm\HttpCrmApiClient.cs` → implement it against a legal endpoint (add
   `crm.legalEntityEndpoint` to `CrmSettings`).
3. `src\CKYC.Processor\Commands\StoreService.cs` (or a sibling `StoreLegalService`) → for records
   with `ClientType == "L"`, call the legal fetch, validate with
   `LegalEntityRecordValidator.Validate`, save via `ctx.LegalEntities.SaveAsync`, and move to
   `Saved` (`build-zip-legal` batches from `Saved`).
4. Reuse `CkycLegalEntityBatchGenerator` unchanged.

---

## B2. Customer search API

**Current stub:** `src\CKYC.Crm\DummyIndividualSearchApi.cs` (deterministic; every
`searchApi.simulateFoundEvery`-th customer is "found", others get a synthetic search key). Selected
by `AppContext.cs`:
```csharp
SearchApi = string.Equals(settings.SearchApi.Mode, "Http", ...)
    ? new HttpIndividualSearchApiClient(settings.SearchApi)
    : new DummyIndividualSearchApi(settings.SearchApi);
```

**Real client:** `src\CKYC.Crm\HttpIndividualSearchApiClient.cs` — `POST {searchEndpoint}` with an
`IndividualSearchApiRequest` body, reads a `SearchApiResponse`:
```csharp
private sealed record SearchApiResponse(
    string? Outcome,            // "Found" -> Found, anything else -> NotFound
    string? SearchKey,          // 20 chars, written to record 20 when not found
    string? CkycReferenceNumber,// present when found
    string? Remark,
    string? RawResponseJson);
```
The request shape is `IndividualSearchApiRequest` in `src\CKYC.Core\Domain\IndividualSearch.cs`
(the search-format record-20 layout: `customerId`, `clientType`, `searchOption`,
`identityTypeAndNumber`, name fields, `dateOfBirth`, `gender`, `photoReferenceNumber`, `relation*`,
`mobileNumber`, `verifiableCredential`, and optional legal fields).

**Config** — `appsettings.json` → `searchApi`:
```jsonc
"searchApi": {
  "enabled": true,
  "mode": "Http",
  "baseUrl": "https://<real-search-host>",
  "searchEndpoint": "/api/search",
  "timeoutSeconds": 30,
  "claimTimeoutMinutes": 30,
  "simulateFoundEvery": 0            // ignored in Http mode, but leave 0
}
```

**What to add / change:**

- Point `searchApi.baseUrl`/`searchEndpoint` at the real endpoint and set `mode=Http`.
- If the real API's **request or response JSON differs** from the records above, edit
  `HttpIndividualSearchApiClient.cs` (the `SearchApiResponse` record and/or the request
  serialization). This is the single file that owns the wire contract.
- Auth/TLS: same pattern as the CRM client (`HttpClient` injection is already supported).
- Enforce the **20-character search key** rule (`ERR_061`): the real API's "not found" response must
  return a 20-char key; `IndividualSearchService` writes it to `IndividualRecord20.SearchKey`.
- Outcome semantics: only the exact string `Found` (case-insensitive) is treated as a match;
  everything else is `NotFound`.

---

## B3. Daily customer-id source (CBS fetch)

**Current stub:** `src\CKYC.Core\Abstractions\DailyCustomerIdProvider.cs`.
- `source.mode=generate` synthesizes `CUST<yyyyMMdd><0001..>` ids (ignores `generateSeed`).
- `source.mode=file` reads `filePath` (JSON or one-id-per-line).
- Note the retry stub: `src\CKYC.Processor\Commands\RetryService.cs` →
  `RunCbsFetchRetryAsync` **always marks the record `Pending` and "succeeds" without any real fetch**.

**Config** — `appsettings.json` → `source`:
```jsonc
"source": {
  "mode": "file",
  "filePath": "D:\\...\\daily-custids.json",
  "documentKeyProperty": null        // set only if the source names the dockey differently
}
```

**What to add / change:**

1. Point `source.mode=file` + `filePath` at the real daily feed (dropped by your scheduler/EAI), or
   leave `generate` off entirely.
2. **Implement a real CBS provider** if the feed is a service rather than a file: add a class
   implementing `IDailyCustomerIdProvider` (returning `SourceCustomer(id, documentKey, source)`) and
   wire it in `AppContext.cs` (`CustomerIds = new YourProvider(...)`).
3. **Replace the always-succeeds retry stub** in `RetryService.RunCbsFetchRetryAsync` with a real
   re-fetch (call the same provider/CBS and set `Failed`/`Pending` based on the real result).
4. Ensure the feed supplies the **`documentKey`** (dockey) per customer for the beckyc image step,
   and the **`source`** channel (`beckyc`/`app`) — see [B6](#b6-channel-imagedocument-fetch-beckyc).
5. `source.generateSeed` is declared but unused; do not rely on it.

---

## B4. FVU integration

**Current:** `src\CKYC.Fvu\FvuRunner.cs` selects
`UseRealFvu ? CommandLineFvuRunner : SimulatedFvuRunner`. `SimulatedFvuRunner` writes a fake
`.validated` file + zip and always succeeds.

**Config** — `appsettings.json` → `fvu`:
```jsonc
"fvu": {
  "exePath": "D:\\ckyccentral\\vendor\\FVU_RUN_UTILITY.exe",
  "workspaceRoot": "D:\\ckyccentral\\ckyc\\runtime",
  "apiBaseUrl": "http://localhost:9091",
  "apiEndpoint": "/api/search-validate/file-with-support",
  "triggerSource": "BATCH",
  "requestTimeoutSeconds": 600,
  "maxRetries": 3,
  "useRealFvu": true
}
```

**What to add / change:**

- Install/point the real `FVU_RUN_UTILITY.exe` (PyInstaller bundle that auto-starts its own Spring
  Boot backend + embedded JDK 21). Keep `useRealFvu=true`.
- Set `exePath` and `workspaceRoot` to deployment paths (relative `workspaceRoot` resolves against
  the working directory).
- Confirm the FVU backend URL/port (`apiBaseUrl`/`apiEndpoint`) matches the vendor build.
- Exit codes: `0` success, `2` config error, `3` validation failed, `1`/other fatal. The `fvu`
  command stores processed zip + hash and marks records `FvuPassed`/`Uploaded`/`FvuFailed`.
- Only fall back to `useRealFvu=false` for offline CI — **not** for real UAT.

---

## B5. CERSAI SFTP transport

**Current:** `src\CKYC.Sftp\SftpRunner.cs` selects
`UseRealSftp ? CommandLineSftpRunner : SimulatedSftpRunner`. The shipped host is **preprod** with an
**empty password**.

**Config** — `appsettings.json` → `sftp`:
```jsonc
"sftp": {
  "enabled": true,
  "exePath": "D:\\ckyccentral\\vendor\\SFTP_Utility_windows\\SFTPRunner.exe",
  "host": "https://<cersai-sftp-host>",
  "username": "<cersai-issued-user>",
  "password": "<cersai-issued-password>",
  "fiCode": "<your-real-fi-code>",
  "useRealSftp": true,
  "timeoutSeconds": 600,
  "archiveUploadedFiles": true
}
```

**What to add / change:**

- Install the vendor `SFTPRunner.exe` and set the real host/username/password (or key) and the real
  `fiCode` (sent as `upload.ficode`).
- `workspaceRoot`, `outboundRoot`, `downloadPath`, `reportFolder` default to relative `runtime/sftp/*`
  (resolve against the working directory) — point them at deployment paths if needed.
- The utility scans fixed folders; FVU routes validated `.UPL` zips into
  `outbound\{INDIVIDUAL,LEGAL_ENTITY}`. After a successful push, uploaded zips are archived unless
  `archiveUploadedFiles=false`.
- Set the proxy block if your network requires it.
- Exit codes: `0` success, `1` config error, `3` runtime error.
- Do not use `useRealSftp=false` for real UAT.

---

## B6. Channel image/document fetch (beckyc)

**Current:** `src\CKYC.Sftp\DocumentSources\SftpDocumentSource.cs`
(`FetchRealAsync` / `FetchSimulatedAsync`). The shipped beckyc config is a **placeholder**:
`sftp-beckyc.example.in`, empty username/password, `basePath: x/y/z`, remote `image.jpg`. See
`docs\image.md` for the full field-by-field guide.

**Config** — `appsettings.json` → `documentFetch.channels.beckyc`:
```jsonc
"documentFetch": {
  "enabled": true,
  "downloadRoot": "D:\\ckyccentral\\ckyc\\runtime\\document-fetch",
  "channels": {
    "beckyc": {
      "enabled": true,
      "kind": "Sftp",
      "useRealSftp": true,
      "host": "<real-beckyc-sftp-host>",
      "port": 22,
      "username": "<real-user>",
      "password": "<real-password>",
      // "privateKeyPath": "C:\\keys\\beckyc.pem", "privateKeyPassphrase": null,
      "basePath": "<real-base-folder>",
      "folderPattern": "{dockey}",
      "timeoutSeconds": 60,
      "documents": [
        { "remote": "image.jpg", "target": "Photo.jpg", "slot": "photoOfIndividual" }
      ]
    }
  }
}
```

**What to add / change:**

1. Set the real host/port/credentials (or key), `basePath`, and confirm the **exact remote file
   name/extension** actually present in the dockey folder. The path built is
   `<basePath>/<folderPattern with {dockey}>/<remote>`.
2. Supported extensions: `.pdf`, `.jpg`, `.jpeg`, `.png` (matching is case-insensitive; listing is
   **non-recursive**). If the listing differs, use `pattern` instead of `remote`, or move a
   subfolder into `folderPattern`; if the layout is fundamentally different, extend
   `SftpDocumentSource.FetchRealAsync`/`SelectFiles`.
3. Valid `slot` values: `photoOfIndividual`, `proofOvd`, `permanentAddressOvd`, `currentAddressOvd`,
   `panDocument`, `clientConsent`, `declarationDocument`.
4. The lookup key is `master_record.DocumentKey` (the step-1 **dockey**, an opaque string — *not*
   the customer id; falls back to the customer id when blank). If the source names the dockey field
   differently, set `source.documentKeyProperty`.
5. `target`'s extension must match the actual content type (a PNG stored as `.jpg` is rejected).
6. Offline dry-run before you point at the real server: set `useRealSftp=false` and place a file at
   `runtime\document-fetch\inbox\beckyc\<dockey>\image.jpg`.

---

## B7. Database

**Current:** LocalDB `(localdb)\MSSQLLocalDB / CkycCentral`.

**What to add / change:**

- `appsettings.json` → `database.connectionString` = the real SQL Server / DB with a
  **least-privilege application identity**.
- Provision with `scripts\sqlserver\schema.sql` using a DBA/deployment identity; apply migrations
  for any pre-existing DB.
- `createSchemaOnStartup` stays `false`; the app only verifies.
- Review the seeded retry policy (`activity_type`) and `status_master` against the agreed policy.

---

## B8. FI identifiers & file naming

Replace the demo institution identifiers with the real ones. Mismatches fail FVU validation.

| Setting | Shipped | Purpose |
|---|---|---|
| `batch.userId` | `IRA000337` | `.UPL` file name |
| `batch.fiCode` | `IN9797` | `.UPL` file name + defaults |
| `batch.regionCode` | `12345` | `.UPL` file name |
| `batch.clientType` | `I` | individual `.UPL` |
| `batch.versionNumber` | `V1.1` | **FVU requires `V1.1` (ERR_172)** |
| `batch.sequenceStart` | `64` | fixed start; **does not auto-increment** |
| `search.userId` / `search.fiCode` / `search.regionCode` | `IRA000337` / `IN9797` / `12345` | `.SRC` naming |
| `update.userId` / `update.fiCode` / `update.regionCode` | `IAU010441` / `IN0238` / `01` (code defaults) | `.UPD` naming |
| `sftp.fiCode` | `IN0031` | SFTP upload FI code |
| record-70 `institutionCode` | `IN0238`/`IN9797` | per-record institution |

File name patterns:

- `.UPL`: `<ClientType>_<UserID>_<FICODE>_<DDMMYYYY>_<nnnnn>.UPL`
- `.SRC`: e.g. `IRA000337_IN9797_DDMMYYYY_nnnnn.SRC`
- `.UPD`: e.g. `I_IAU010441_IN0238_DDMMYYYY_nnnnn.UPD`

> Keep `versionNumber = V1.1`. Raise `sequenceStart` per run/day to avoid overwriting a batch
> generated the same date.

---

## B9. Simulation knobs to switch off

For real UAT all deterministic failure injection must be **off**:

```jsonc
"simulation": {
  "saveErrorsEnabled": false,
  "saveErrorEvery": 0,
  "saveErrorForCustomerId": null,
  "fvuFailEvery": 0,
  "cbsFetchErrorsEnabled": false,
  "cbsFetchFailEvery": 0,
  "cbsFetchFailForCustomerId": null
}
```

Also disable the search simulator by using `searchApi.mode=Http` (and leave `simulateFoundEvery: 0`,
`simulateErrorsEnabled: false`). Consumers to be aware of: `StoreService` (save errors),
`FetchCommand` (CBS failures), `DummyIndividualSearchApi` (search failures/found), `FvuRunner`
(`fvuFailEvery`), `Program.cs` (verbose exception logging gated by `saveErrorsEnabled`).

---

## B10. Supporting-document generation

**Current:** `documentGeneration` renders `D1.pdf` (static undertaking from `Template_1.pdf`),
`AdhaarAP.pdf` (Aadhaar EKYC from `Aadhaar_template_blank.jpg`) and `C3.pdf` (client consent from
`Consent_template_blank.png`) at `build-zip`, pixel-targeted at the sample templates. Renderers:
`src\CKYC.Files\Documents\{DocumentGenerationService,AadhaarDocumentRenderer,ConsentDocumentRenderer,DocumentTemplateRenderer,RecordDocumentSlots}.cs`. `CkycUploadWriter` falls back to
`AdhaarAP.jpg` when an OVD copy name is missing.

**What to add / change:**

- Replace the templates in `doc_format\` with the real regulator/institution templates and
  recalibrate `coordinateWidth`/`coordinateHeight`, font/family/size, and (Aadhaar) the photo frame
  coordinates in `AadhaarDocumentRenderer.cs`.
- Confirm the published `fileName`s (`D1.pdf`, `C3.pdf`, `AdhaarAP.pdf`) match what the CRM record
  references and what CERSAI expects.
- If documents should come from the CRM/document store instead of being rendered, set
  `documentGeneration.enabled=false` and import the files (`documents import`) / fetch them from the
  channel source.
- Note the 500 KB per-customer supporting-document limit.

---

## B11. Hardcoded sample data to scrub

These live in code (not config) and must not reach real UAT output:

| File | Hardcoded content | Action |
|---|---|---|
| `src\CKYC.Crm\DummyCrmDataProvider.cs` | 12 fixed names, `yopmail.com` emails, generated PAN/Aadhaar, fixed addresses, record-70: `Anusaya`/`A236`/`Kamlamills`/`PhonePe_Limited`/`IN0238`/`Mumbai`, `D1.pdf`/`C3.pdf` | Retire by pointing at the real CRM (`crm.baseUrl`). Keep only for offline tests. |
| `src\CKYC.Crm\DummyCrmLegalEntityProvider.cs` | `Meridian Legal Entity`, `meridiansoft.com` emails, `Mumbai`/`Pune` addresses, fixed proof filenames, record-70 constants | Same as above (needs the B1.5 legal path for real use). |
| `src\CKYC.Crm\DummyIndividualSearchApi.cs` | synthetic reference `R…`, search key `ISR…` | Retire via `searchApi.mode=Http`. |
| `src\CKYC.Processor\Commands\RetryService.cs` | `RunCbsFetchRetryAsync` always succeeds | Replace with a real CBS re-fetch (B3). |
| `src\CKYC.Files\CkycUploadWriter.cs` | `AdhaarAP.jpg` fallback | Ensure records always reference a real imported/generated file. |
| `tests\CKYC.SpecChecks\Program.cs` | test fixtures with sample names/ids | Test-only; leave, but do not reuse as UAT data. |
| `custid.json`, `samples\*`, `staging\*`, `samples\failure\*` | `RJKS2026`, `CUST-…`, `ashish.kumar@yopmail.com`, `9876543210`, PANs, `33720047736138` | Use only for the dummy run; supply real UAT data for the real run. |

---

## B12. Environment paths & tooling

The shipped config still references the original `D:\centralprocessing\...` tree. Fix before a local
run:

| File | Field | Shipped | Set to |
|---|---|---|---|
| `appsettings.json` | `batch.outputRoot` | `D:\centralprocessing\ckyc\runtime\output` | `D:\ckyccentral\ckyc\runtime\output` |
| `appsettings.json` | `fvu.exePath` | `D:\centralprocessing\vendor\FVU_RUN_UTILITY.exe` | `D:\ckyccentral\vendor\FVU_RUN_UTILITY.exe` |
| `appsettings.json` | `fvu.workspaceRoot` | `D:\centralprocessing\ckyc\runtime` | `D:\ckyccentral\ckyc\runtime` |
| `appsettings.json` | `response.inputRoot` | `D:\centralprocessing\cersai\response` | `D:\ckyccentral\cersai\response` |
| `appsettings.json` | `search.outputRoot` | `D:\centralprocessing\ckyc\runtime\search` | `D:\ckyccentral\ckyc\runtime\search` |
| `appsettings.json` | `documentFetch.downloadRoot` | `D:\centralprocessing\ckyc\runtime\document-fetch` | `D:\ckyccentral\ckyc\runtime\document-fetch` |
| `appsettings.json` | `sftp.exePath` | (already `D:\ckyccentral\vendor\...`) | verify it exists |
| `nuget.config` | `local-cache` / `globalPackagesFolder` | `C:\Users\offic\.nuget\packages` | your machine's cache or nuget.org |
| `samples\failure\settings-*.json` | same roots | `D:\centralprocessing\...` | fix if you use these overrides |

Also note: `runtime\ckyc.db*` are **stale SQLite leftovers** (SQL Server is the only provider) and
can be deleted; `run.md`'s `Remove-Item .\runtime\ckyc.db` is stale.

---

## B13. UAT execution checklist and test cases

### Pre-UAT checklist (go/no-go)

- [ ] Real CRM base URL + endpoints configured and reachable (`crm.baseUrl`, `customersEndpoint`).
- [ ] `crm serve` removed from run scripts / scheduler.
- [ ] Search API configured (`searchApi.mode=Http`, real `baseUrl`/`searchEndpoint`) and response
      mapping verified against a known customer.
- [ ] Daily source wired (`source.mode=file` + real feed, or real `IDailyCustomerIdProvider`).
- [ ] `RetryService.RunCbsFetchRetryAsync` replaced with a real re-fetch.
- [ ] Real FVU exe installed (`fvu.useRealFvu=true`, correct `exePath`/`workspaceRoot`/backend).
- [ ] Real SFTP host/credentials/`fiCode` set (`sftp.useRealSftp=true`).
- [ ] Beckyc host/credentials/basePath/remote file verified (`documentFetch.channels.beckyc`).
- [ ] Real SQL Server provisioned (`schema.sql` applied) and connection string set.
- [ ] Real FI identifiers in `batch` / `search` / `update` / `sftp`; `versionNumber=V1.1`.
- [ ] All simulation knobs off ([B9](#b9-simulation-knobs-to-switch-off)).
- [ ] Doc templates/files confirmed (or `documentGeneration.enabled=false` with imported docs).
- [ ] Paths fixed ([B12](#b12-environment-paths--tooling)); real staging documents available.
- [ ] Warning-free build succeeds (`build.ps1`) and `CKYCProcessor.exe help` runs.

### End-to-end UAT test cases

| # | Scenario | Steps | Expected result |
|---|---|---|---|
| 1 | Daily source fetch | `fetch` with the real feed | master rows `Pending` with correct `documentKey`/`source` |
| 2 | CRM enrich (individual) | `store` | record tables populated; `ImagePending` (beckyc) or `PendingSearch` |
| 3 | Channel image fetch | `documents fetch --customer <id>` | image imported, slot repointed, `PendingSearch` |
| 4 | Customer search — not found | `search-customer` | `Searched`; 20-char search key in record 20 |
| 5 | Customer search — found | `search-customer` | `SearchFound` + CKYC reference; not batched |
| 6 | Batch generation | `build-zip` | `.UPL` + `support_docs` zip; records `Batched` |
| 7 | FVU validation — pass | `fvu` | processed zip + hash; `FvuPassed`/`Uploaded` |
| 8 | FVU validation — fail | inject a bad field | `FvuFailed`; `.ERR` file present |
| 9 | SFTP upload | `sftp push` | files uploaded; records `Uploaded`; zips archived |
| 10 | Response read — accept | `sftp pull` + `response read` | `Reconciled`; ack/CKYC number on master |
| 11 | Response read — reject | reply with a rejection remark | `Rejected` + remark; shows in `reconcile --kind cersai` |
| 12 | Retry path | force a CRM failure, then `retry` | backoff honoured; success clears retry state |
| 13 | Re-push rejected | `reattempt --customer <id>` then re-batch | snapshot written; re-submitted |
| 14 | Reconciliation report | `reconcile --out recovery.csv` | CSV with exhausted/failed records |
| 15 | Legal entity | `insert-legal` → `build-zip-legal` → `fvu` | `Saved` → `Batched` → validated |
| 16 | Bulk search `.SRC` | `search-load` → `search-process` → `search-fvu` → `search-response` | `.SRC` + validated `.SRC.zip` + response tables |
| 17 | Bulk update `.UPD` | `update-load` → `update-process` → `update-fvu` → `update-response` | `.UPD` + ack/status on submissions |
| 18 | Download response | `download-response --path <DWN.RES.zip>` | immutable lines + artifact inventory |

### Evidence to capture per case

- Console output + the `logs\` file for the run.
- DB rows: `master_record` (status/`LastError`/`BatchFile`), `master_record_attempt`,
  `master_record_response`, `individual_search`.
- Generated artifacts: `.UPL`/`.SRC`/`.UPD`, `support_docs`, FVU processed zip + hash, SFTP reports.

---

*End of document. Keep Part B in sync with the code: the "what to add" items are the exact seams
(`AppContext.cs`, `HttpCrmApiClient.cs`, `HttpIndividualSearchApiClient.cs`, `RetryService.cs`,
`SftpDocumentSource.cs`, `appsettings.json`) that turn the demo into the real integration.*
