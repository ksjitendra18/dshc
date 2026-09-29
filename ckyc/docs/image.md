# Beckyc image/document fetch — where to change things

This is the working guide for the **pre-search image step** (`documents fetch`). Use it when you
find out how the real beckyc SFTP folder is laid out: find your case below and edit
**`appsettings.json` → `documentFetch` → `channels.beckyc`**.

> Status today: the config ships with a **placeholder** host and a guessed file name
> (`image.jpg`). Nothing is wired to the real server yet — that's expected. Change the values
> below once you inspect the folder.

- Config file: `ckyc/appsettings.json`
- Code (transport): `src/CKYC.Sftp/DocumentSources/SftpDocumentSource.cs`
- Code (mapping/import): `src/CKYC.Processor/Commands/DocumentFetchService.cs`
- Field definitions: `src/CKYC.Core/Configuration/DocumentFetchSettings.cs`
- Full reference: `docs/document-fetch.md`
- DB migration: `scripts/sqlserver/migrations/20260928_add_document_fetch.sql`

---

## 1. How the remote path is built

For a record, the fetch uses its `master_record.DocumentKey` (the step-1 **dockey**; falls back
to the customer id when blank):

```
<basePath> / <folderPattern with {dockey} replaced> / <remote>
   x/y/z    /         9f2c7a4e81b3d6f0a5c2           / image.jpg
```

> **The dockey is not the customer id.** `RJKS2026` is the customer id; the document key is a
> separate, opaque string the source supplies (no assumed format), and it names the SFTP folder.

Rules the code enforces:

| Rule | Detail |
|------|--------|
| Folder listing is **top-level only** (non-recursive) | Files inside subfolders are ignored |
| `remote` matches a **file name**, not a path | Cannot contain `/` |
| Matching is **case-insensitive** | `image.JPG` matches `image.jpg` |
| Supported extensions | `.pdf`, `.jpg`, `.jpeg`, `.png` only |
| `basePath` is relative to the SFTP login home | Leading/trailing `/` are trimmed |
| Missing folder or no matching file | Record is blocked at **`ImageFailed` (`IMF`)** and retryable |

---

## 2. The fields you will change

### Connection (in `documentFetch.channels.beckyc`)

| Field | Meaning | Change when |
|-------|---------|-------------|
| `enabled` | Turns the channel source on/off | Leave `true` for beckyc |
| `kind` | Transport; only `Sftp` is implemented | Leave `Sftp` |
| `useRealSftp` | `true` = call the server; `false` = read a local inbox | Set `true` once credentials are known |
| `host` / `port` | SFTP server | **Placeholder — set the real server** |
| `username` / `password` | SFTP login | **Empty — set the real credentials** |
| `privateKeyPath` / `privateKeyPassphrase` | Optional key auth (used instead of password) | If the server wants key auth |
| `basePath` | Remote root, e.g. `x/y/z` | **Set to the real base folder** |
| `folderPattern` | Folder under `basePath`; `{dockey}` is replaced | Usually `{dockey}`; change if the folder is named differently |
| `timeoutSeconds` | Connect/operation timeout | Increase if the server is slow |
| `localInboxPath` | Root used only when `useRealSftp=false` | For offline tests |

### The `documents[]` entries — **what to download and where to put it**

| Field | Meaning | Notes |
|-------|---------|-------|
| `remote` | Exact remote file name inside the dockey folder | **Blocks the record if it isn't found** |
| `pattern` | Glob (`*`, `?`) matched against file names; used when `remote` is empty | Case-insensitive |
| `target` | Name the bytes are stored as in the DB | Defaults to the slot's current record name, then the remote name. **Extension must match content type** |
| `slot` | Record field to point at `target` | Without it, the file is stored but not referenced by the record |
| `enabled` | Skip this entry without deleting it | Default `true` |

Valid `slot` values (from `RecordDocumentSlots`):

`photoOfIndividual`, `proofOvd`, `permanentAddressOvd`, `currentAddressOvd`,
`panDocument`, `clientConsent`, `declarationDocument`.

---

## 3. Examples — pick the one matching your folder

### A. Folder contains exactly `image.jpg` (current assumption)

```jsonc
"documents": [
  { "remote": "image.jpg", "target": "Photo.jpg", "slot": "photoOfIndividual" }
]
```
Downloads `x/y/z/<dockey>/image.jpg`, stores it as `Photo.jpg`, sets the record's photo.

### B. File name varies / different case (`photo.jpg`, `Photo.JPG`, `img_001.jpg`)

Drop `remote`, use a pattern. A single entry still gets its `target`/`slot` applied:

```jsonc
"documents": [
  { "pattern": "*.jpg", "target": "Photo.jpg", "slot": "photoOfIndividual" }
]
```
`*.jpg` matches any `.jpg` (case-insensitive); `.jpeg` and `.png` would need their own pattern.

### C. The real server serves PNG

Extension must match the content, so store it as `.png`:

```jsonc
"documents": [
  { "remote": "image.png", "target": "Photo.png", "slot": "photoOfIndividual" }
]
```
(`.png` ingestion is supported by the document store.)

### D. Multiple files in one dockey folder

One entry per file; each gets its own target/slot:

```jsonc
"documents": [
  { "pattern": "photo*.jpg", "target": "Photo.jpg",   "slot": "photoOfIndividual" },
  { "pattern": "aadh*.jpg",  "target": "AdhaarAP.jpg", "slot": "proofOvd" }
]
```
Do **not** map two patterns to the same `target` — the last import wins by canonical name.

### E. Files are nested one level deeper (`.../<dockey>/front/image.jpg`)

Listing is non-recursive, so fold the subfolder into `folderPattern` (not `remote`):

```jsonc
"folderPattern": "{dockey}/front",
"documents": [ { "remote": "image.jpg", "target": "Photo.jpg", "slot": "photoOfIndividual" } ]
```

### F. The file is already named exactly like the record expects (e.g. `Photo.jpg`)

Fetch everything as-is; no repointing needed:

```jsonc
"documents": []
```
Downloads all supported top-level files under their own names. Only works if the name matches a
name the record already references, otherwise `build-zip` won't find it.

### G. The dockey field is named something else in the source

The fetch always uses `master_record.DocumentKey` (the opaque key from the source — not the
customer id). Only the **name of the source field** is configurable — set it under `source` when
the upstream payload doesn't call it `documentKey`:

```jsonc
"source": { "mode": "file", "filePath": "custid.json", "documentKeyProperty": "documentNumber" }
```

Accepted names out of the box: `documentKey`, `dockey`, `docKey`, `document_key`, `documentId`,
`docId`, `imageKey`. If the source genuinely has no dockey at all, the customer id is used as the
fallback and `folderPattern: "{dockey}"` then points at the customer-id folder.

---

## 4. Before you point it at the real server (offline dry run)

Set the simulated transport and copy a dummy file into the local inbox — this proves the path
logic and slot mapping without touching the network:

```jsonc
"useRealSftp": false
```

Then place the file at:

```
runtime/document-fetch/inbox/beckyc/<dockey>/image.jpg
```

and run:

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
& $exe fetch cust
& $exe store
& $exe documents fetch          # or: documents fetch --customer RJKS2026
& $exe status                   # IMP should clear; IMF means it couldn't find the file
```

---

## 5. Troubleshooting — symptom → cause → where to change

| Symptom / log | Likely cause | Fix |
|---------------|--------------|-----|
| `The SFTP folder for document key '<k>' does not exist: '<path>'` | Wrong `basePath`, `folderPattern`, or dockey | Check `x/y/z/<dockey>`; adjust `basePath` / `folderPattern` |
| `Expected file '<remote>' ... was not found in '<folder>'` | File isn't named exactly `remote`, or wrong extension | Use a `pattern`, or correct `remote` (case matters only for clarity — matching is case-insensitive) |
| `No matching supported document was found ...` | Folder has only unsupported types, or files are in a subfolder | Use a supported extension; move the subfolder into `folderPattern` |
| `The content signature does not match image/jpeg` | `target` extension ≠ actual content (e.g. PNG stored as `.jpg`) | Make `target`'s extension match the bytes |
| Record stuck at `IMF` (ImageFailed) | Any of the above; fetch threw | Fix config, then `retry --activity ImageFetch` |
| `documents fetch` says "No records awaiting an image/document" | No records at `IMP` — `store` wasn't run, or the channel has no source | Run `store`; ensure `documentFetch.enabled=true` and the beckyc channel `enabled=true` |
| `build-zip` skips the record / missing doc in `support_docs` | Fetched file imported, but no `slot` repointed the record | Add `slot` (or set `target` to the name the record references) |
| Connection/auth failure | Wrong `host`/`port`/`username`/`password`/key | Correct the connection fields; set `useRealSftp=true` |

**Quick checks**

- Actual names on the server: list `x/y/z/<dockey>/` with your SFTP client (WinSCP, `sftp`) and
  note the exact names + extensions.
- Processor logs: NLog writes to `logs/`; look for `[documents]` lines (they log the folder/file).
- DB state:
  ```sql
  SELECT CustomerId, DocumentKey, Status, StatusCode, LastError, LastActivity
  FROM master_record WHERE Status IN (15,16);           -- IMP / IMF
  ```

---

## 6. Changing the folder *layout* itself

If the layout is fundamentally different (e.g. one flat folder of all files, or the dockey is
embedded in the file name), the config can't express it — extend the transport:

- `SftpDocumentSource.FetchRealAsync` / `SelectFiles` — how the folder and files are chosen.
- `ChannelDocumentFetchSettings.ResolveRemoteFolder` — how `basePath` + `folderPattern` combine.

Keep the `IDocumentSource` contract (`FetchAsync` returns whatever files it selects) and the
rest of the pipeline (import, slot mapping, status gating) stays unchanged.
