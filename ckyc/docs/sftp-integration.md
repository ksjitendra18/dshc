# SFTP transport integration (`sftp push` / `sftp pull`)

The processor drives the CERSAI-provided **`SFTPRunner.exe`** (Windows headless utility) to
exchange files with the CKYCRR SFTPGo server. Transport is split into **two independent
processes**:

| Command | Direction | What it does |
|---------|-----------|--------------|
| `CKYCProcessor.exe sftp push` | local → CERSAI | Uploads every FVU-validated `.UPL.zip` staged in the individual / legal-entity outbound folders, then marks their master records **Uploaded**. |
| `CKYCProcessor.exe sftp pull` | CERSAI → local | Downloads processed response files into `sftp.downloadPath`. **Download only** — ingest them separately with `response read` / `download-response`. |

Both are normal CLI invocations (like every other step), so they are independently runnable
and schedulable.

---

## Why the FVU output location changed (deterministic routing)

The SFTP utility scans fixed, configured folders. To avoid a copy-and-guess step, the FVU now
writes each validated individual/legal `.UPL` ZIP **straight into the matching outbound folder**
when SFTP is enabled:

```
runtime/sftp/outbound/
├── INDIVIDUAL/        <- I_<user>_<fi>_<ddmmyyyy>_<seq>.UPL.zip
└── LEGAL_ENTITY/      <- L_<user>_<fi>_<ddmmyyyy>_<seq>.UPL.zip
```

These are exactly the folders passed to the utility as `upload.individual-folder` and
`upload.legal-entity-folder`. Search (`.SRC`) and bulk-update (`.UPD`) batches keep their
existing per-run output folders — only `.UPL` creation batches are routed here.

After a successful `sftp push`, the uploaded ZIPs are **archived** out of the scan folders
(`runtime/sftp/archive/<ENTITY>/<timestamp>/`) so they are not re-sent on the next run.
Set `sftp.archiveUploadedFiles=false` to keep them in place.

> The FVU also writes its `Executive_Summary_*.pdf` into its output folder. The vendor utility
> selects files by naming/date criteria, so that report is not uploaded.

---

## Configuration (`appsettings.json` → `sftp`)

```jsonc
"sftp": {
  "enabled": true,                         // master switch; false = commands disabled + legacy FVU output
  "exePath": "D:\\centralprocessing\\vendor\\SFTP_Utility_windows\\SFTPRunner.exe",
  "workspaceRoot": "runtime/sftp",         // generated config.yaml + archive
  "outboundRoot": "runtime/sftp/outbound", // INDIVIDUAL / LEGAL_ENTITY under this
  "downloadPath": "runtime/sftp/downloads",
  "reportFolder": "runtime/sftp/reports",
  "host": "https://sftp-preprod.ckycindia.dev",
  "username": "IAU010054",
  "password": "<CERSAI-issued password>",
  "fiCode": "IN0031",
  "proxy": { "enabled": false, "host": "127.0.0.1", "port": 18888 },
  "timeoutSeconds": 600,
  "useRealSftp": true,                     // false = deterministic local simulation (no network)
  "archiveUploadedFiles": true
}
```

Relative paths resolve against the process working directory. Optional overrides:
`sftp.individualFolder`, `sftp.legalEntityFolder`, `sftp.configPath`, `sftp.archiveRoot`.

A deterministic `config.yaml` is regenerated from these settings on every run at
`sftp.workspaceRoot/config.yaml` and passed to the utility with `--config`. **Do not edit it by
hand** — edit `appsettings.json`.

### `enabled` behaviour

- **`sftp.enabled = true`** — the FVU routes `.UPL` output to the outbound folders, `fvu` leaves
  records at **FVU passed**, and `sftp push` advances them to **Uploaded**.
- **`sftp.enabled = false`** — legacy behaviour: the FVU writes to its per-batch run folder and
  `fvu` marks records **Uploaded** directly; the `sftp` commands refuse to run.

---

## End-to-end flow

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

& $exe build-zip            # .UPL + support_docs zip (per batch)
& $exe fvu                  # validate -> validated .UPL.zip lands in sftp/outbound/INDIVIDUAL
& $exe sftp push            # upload to CERSAI; records -> Uploaded (pending approval/processing)

# ... RE Checker approves + CERSAI processes the file ...

& $exe sftp pull            # download processed response ZIPs to sftp/downloads
& $exe response read --dir .\runtime\sftp\downloads   # ingest the .RES replies
```

For legal-entity creation the flow is identical (`build-zip-legal` → `fvu` → `sftp push`),
with the validated ZIP staged under `LEGAL_ENTITY`.

---

## Exit codes and results

`SFTPRunner.exe` exit codes are mapped as follows (per the vendor integration document V1.2):

| Exit | Meaning | `sftp` result |
|------|---------|---------------|
| `0` | Transfer completed; all files processed | success (`Passed=true`) |
| `1` | Configuration error (missing/invalid config) | failure |
| `3` | Runtime error (connection failed / JAR exception) | failure |

On a successful push the command logs the staged files, the generated config path and the
Executive Summary report path, then marks records. On failure the files are left in place so a
retry is a simple re-run.

---

## Audit trail

`sftp push` writes one `master_record_attempt` row per uploaded record
(`Stage = SftpUpload`, activity `SftpUpload`). Apply
`scripts/sqlserver/migrations/20260924_add_sftp_activity_types.sql` once to seed the two new
activity types (`SftpUpload`, `SftpDownload`); the code tolerates their absence, but the audit
row is only anchored to an activity when the seed row exists.

---

## Offline testing

Set `sftp.useRealSftp=false` to exercise the whole flow without network access: the runner still
generates the `config.yaml` and reports the staged files, but performs no transfer. This is what
the `CKYC.SpecChecks` suite uses to verify the deterministic routing and config generation.
