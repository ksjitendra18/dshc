# Channel image/document fetch (pre-search)

After the CRM data is fetched (`store`), an individual record must have its supporting
image/document pulled from the intake channel's source before it can be searched and batched.
Each channel has its own source because the image lives in a different place per channel; only
**`beckyc`** is implemented (SFTP), while a channel with no configuration is a pass-through.

## Flow

```
fetch cust  ->  store  ->  documents fetch  ->  search-customer  ->  build-zip
                  |              |
                  |       beckyc SFTP: <basePath>/<dockey>/<file>.txt (base64 data-URI image/PDF)
                  |
        channel source configured ? ImagePending (IMP) : PendingSearch
```

* `store` leaves a record in **ImagePending (`IMP`)** when its channel has an active source.
* `documents fetch` pulls the file(s) for the record's `dockey`, imports them into the document
  store, points the configured record slot at the fetched file, then advances the record to
  `PendingSearch`.
* A missing/failed file leaves the record in **ImageFailed (`IMF`)**, blocked from batching and
  retryable via `retry --activity ImageFetch`.
* `build-zip` only batches `Searched` records, so an image-pending/failed record can never reach
  a batch — the image step is a hard gate.

## The document key (`dockey`)

The **customer id** (e.g. `RJKS2026`) and the **document key** are **separate values**. The
document key is an opaque string supplied by the source (do not assume a format/prefix); it
arrives with the step-1 source fetch and is stored on `master_record.DocumentKey`. The channel
source folder is named by it (`<basePath>/<dockey>/...`), **not** by the customer id. When the
source omits the key, the customer id is used as a fallback.

`custid.json` / source file:

```jsonc
// single record
{ "customerId": "RJKS2026", "documentKey": "9f2c7a4e81b3d6f0a5c2" }

// array (strings or objects)
[
  { "customerId": "CUST-A", "documentKey": "4b8e1d6c0a9f3e7d2b5c", "source": "beckyc" },
  { "customerId": "CUST-B" },
  "CUST-C"
]

// plain text, one per line: customerId[,documentKey[,source]]
RJKS2026,9f2c7a4e81b3d6f0a5c2,beckyc
CUST-B
```

Accepted key property names (case-insensitive) are `documentKey`, `dockey`, `docKey`,
`document_key`, `documentId`, `docId`, `imageKey`. If the upstream payload names it something
else, set the exact name once in config and it is used first:

```jsonc
"source": { "mode": "file", "filePath": "...", "documentKeyProperty": "documentNumber" }
```

## Configuration (`appsettings.json` → `documentFetch`)

> Once you know the real beckyc folder/file layout, use **`docs/image.md`** — a practical
> "where to change it" guide with examples (exact name, wildcard, PNG, multiple files, nested
> folders) and a troubleshooting table.

```jsonc
"documentFetch": {
  "enabled": true,
  "downloadRoot": "D:\\centralprocessing\\ckyc\\runtime\\document-fetch",
  "channels": {
    "beckyc": {
      "enabled": true,
      "kind": "Sftp",
      "useRealSftp": true,               // false = read the local inbox (offline demo)
      "host": "sftp-beckyc.example.in",
      "port": 22,
      "username": "",
      "password": "",
      "privateKeyPath": null,            // optional OpenSSH/PEM key (used instead of the password)
      "basePath": "x/y/z",               // remote root, e.g. x/y/z
      "folderPattern": "{dockey}",       // folder under basePath; {dockey} is substituted
      "timeoutSeconds": 60,
      "documents": [
        // download the base64 .txt in <basePath>/<dockey>/, decode it, store it as Photo.jpg
        // and point the record's photo slot at it. Omit "target" to keep the record's current name.
        { "pattern": "*.txt", "target": "Photo.jpg", "slot": "photoOfIndividual" }
      ]
    }
  }
}
```

* **`remote`** — exact file name inside the dockey folder. Omit it to use **`pattern`**
  (glob with `*`/`?`). When `documents` is empty, every supported file in the folder is fetched
  under its own name.
* **`target`** — the file name the bytes are stored/imported as. Defaults to the slot's current
  record file name, then the remote name. The extension must match the content type
  (PDF / JPG / JPEG / PNG); if it does not, it is corrected to the fetched content's real
  extension (see below).
* **`slot`** — record document slot to point at the fetched file (`photoOfIndividual`,
  `proofOvd`, `panDocument`, `currentAddressOvd`, `permanentAddressOvd`, `clientConsent`,
  `declarationDocument`).

### Base64 `.txt` payloads (beckyc)

`beckyc` delivers the image inside a `.txt` file — a single `data:<mime>;base64,<payload>` line,
e.g.:

```
data:image/png;base64,/9j/4AAQSkZJRgABAQAAAQABAAD...
```

`documents fetch` decodes it automatically and stores the raw image/PDF bytes. Notes:

* **The declared mime type is not trusted** — the real content type is sniffed from the decoded
  bytes (JPEG `FF D8 FF`, PNG `89 50 4E 47…`, PDF `%PDF-`). A payload labelled `image/png` whose
  bytes are JPEG is stored as a **`.jpg`**.
* The stored name keeps the configured `target` base name but takes the real extension: with
  `"target": "Photo.jpg"` a PNG payload is stored as `Photo.png`.
* A `.txt` that is neither a data URI nor (for a `.txt` file) valid base64 is passed through
  unchanged and then fails the store's extension check, blocking the record at `IMF`.
* `remote`/`pattern` select the `.txt`; the decoded file is published under the sniffed
  extension, not the `.txt` name.

### Offline / simulated transport

Set `useRealSftp=false` to serve the same layout from a local inbox — useful for CI and demos:

```
<downloadRoot>/inbox/<channel>/<dockey>/<file>.txt
```

## Commands

```powershell
CKYCProcessor.exe documents fetch                       # all ImagePending records
CKYCProcessor.exe documents fetch --customer CUST...    # one record (also forces ImageFailed)
CKYCProcessor.exe documents fetch --channel beckyc       # only one channel
CKYCProcessor.exe retry --activity ImageFetch           # re-attempt blocked records
```

## Database

The schema needs the `20260928_add_document_fetch.sql` migration (new `master_record` columns,
statuses 15/16 and the `ImageFetch` activity):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CkycCentral -b -i .\scripts\sqlserver\migrations\20260928_add_document_fetch.sql
```
