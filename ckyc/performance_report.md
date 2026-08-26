# CKYC Processor — Performance Audit Report

**Date:** 2026-08-25
**Scope:** full `src/` solution (`CKYC.Core`, `CKYC.Data`, `CKYC.Crm`, `CKYC.Files`, `CKYC.Fvu`, `CKYC.Processor`) — .NET 10, SQLite (`Microsoft.Data.Sqlite`), raw ADO.NET (no EF Core).
**Audit type:** static code review (no code changes made).

---

## 1. Executive summary

The codebase is well structured and readable, but it treats SQLite as if it were a
plain file store. The dominant performance risks are **database write-throughput (no
SQLite PRAGMA tuning, autocommitted per-row writes, whole-table dedup reads)** and
**in-memory file/response parsing**. Batch caps are small (`MaxIndividualBatchRecords = 500`,
`MaxLegalEntityBatchRecords = 10`), so per-record costs are proportional today, but the
patterns do not scale and several are O(n²) or O(table) regardless of the cap.

**Three highest-impact fixes (by effort/return):**

1. **PRAGMA/WAL + `synchronous=NORMAL` + `busy_timeout`** on every connection — single biggest
   write-throughput win for a write-heavy batch pipeline.
2. **Batch inserts inside transactions** (envelope bulk-write loops) and **`INSERT OR IGNORE` /
   UPSERT** instead of check-then-insert / delete-then-insert.
3. **Stop scanning the whole `master_record` table** in `UpsertDailyAsync`; use a unique index
   + `INSERT OR IGNORE` (or a set-operation `NOT EXISTS` batch).

---

## 1.1 Implementation status (2026-08-25)

> Changes were made on branch `performance` against commit `fe45de6` after this report
> (re-read the report for the reasoning). The schema's **"length validation yes, other
> validation no"** design explicitly forbids UNIQUE/CHECK/FK constraints, so the report's
> UNIQUE-index + `INSERT OR IGNORE`/`ON CONFLICT` fixes were **intentionally not applied** —
> they could fail on existing data and would violate the documented schema philosophy. The
> safe, behavior-preserving equivalents were implemented instead.

| Finding | Status | What was done | File |
|---------|--------|---------------|------|
| D1 (SQLite tuning) | ✅ Done | Per-connection PRAGMAs: `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=5000`, `cache_size=-20000`, `temp_store=MEMORY` | `src/CKYC.Data/SqliteDatabase.cs` |
| D2 (`UpsertDailyAsync`) | ✅ Done | Dropped the whole-table `HashSet` scan + N autocommit inserts; one prepared command in **one transaction** via `INSERT ... SELECT ... WHERE NOT EXISTS` (indexed CustomerId lookup) | `src/CKYC.Data/MasterRepository.cs` |
| D5 (`NextAttemptNumberAsync`) | ✅ Done | Removed the separate COUNT round-trip; attempt number computed **inside** the INSERT via `SELECT COUNT(*)+1`; deleted the dead method | `src/CKYC.Data/MasterRepository.cs` |
| P1 (O(n²) build-zip) | ✅ Done | `FirstOrDefault`-per-id → `Dictionary` lookup in **both** `BuildZipCommand` and `BuildZipLegalCommand` | `src/CKYC.Processor/Commands/BuildZipCommand.cs`, `BuildZipLegalCommand.cs` |
| Missing indexes (perf §5.1) | ✅ Done | Non-unique `CREATE INDEX IF NOT EXISTS`: all `kyc_record_*(MasterRecordId)`, `master_record(Status,Id)`, `master_record(Status,RetryCount,LastActivity,NextRetryAt)`, `batch(BatchKey)`, `fvu_run(BatchKey)` | `src/CKYC.Data/Schema/Ddl.cs` |
| D3 / D4 / D8 (check-then-write, DELETE-then-INSERT) | ⏳ Not done | Requires UNIQUE indexes + `ON CONFLICT`, which the schema forbids; left as-is to avoid regressions | — |
| D6 (ordinal cache / typed readers) | ⏳ Not done | Reader-mapping refactor (typed getters + invariant date parse); larger change, deferred | — |
| D7 (`IN` size guard) | ⏳ Not done | SQLite variable cap is fine at the ≤500 batch cap; chunking deferred | — |
| D9 / D11 (`SELECT *`, TEXT dates) | ⏳ Not done | Acceptable at current batch caps; deliberate TEXT-datetime choice preserved | — |
| D10 (`CreateSchemaOnStartup`, dispose) | ⏳ Not done | Startup/migration + `IDisposable` wiring cleanup; deferred | — |
| F1–F5, P2, P3, C1, C2, X1–X3 | ⏳ Not done | File streaming, parallelism, `IHttpClientFactory`, STJ source-gen, structured logging — all larger refactors/new deps; out of scope for this pass | — |

**Validation performed on the completed changes:**
- `dotnet build` (Release, `CKYC.Processor`) → **0 warnings, 0 errors**.
- `tests/CKYC.SpecChecks` (individual + legal DB round-trip) → **passed**.
- `fetch` idempotency on a fresh DB → run 1 `Inserted=5/Skipped=0`, run 2 `Inserted=0/Skipped=5`.
- CBS-failure attempt numbering → retry 1 on run 1, retry 2 on run 2 (correct increment).
- Baseline comparison (stashed changes): the `build-zip` "No Saved records" message is **pre-existing**
  (the standard `store` path never sets `master_record.ClientType`, so `build-zip`'s `clientType="I"`
  filter matches nothing) — **not** caused by these changes.

---

## 2. Severity legend

| Severity | Meaning |
|----------|---------|
| 🔴 **High** | Real throughput/latency or correctness-under-load issue; fixes fastest ROI. |
| 🟠 **Medium**| Sub-optimal for the target volume; becomes material as data grows. |
| 🟡 **Low**  | Minor; code-quality/consistency, negligible at current batch caps. |

---

## 3. Findings

### 3.1 Data layer — `CKYC.Data`

#### 🔴 D1 — No SQLite connection tuning (WAL, synchronous, busy_timeout)
`src/CKYC.Data/SqliteDatabase.cs` (`Create()`) opens a bare `SqliteConnection`. No
`journal_mode`, `synchronous`, `busy_timeout`, `cache_size`, or `temp_store` are ever set.

- **Why it hurts:** SQLite defaults to `journal_mode=DELETE` + `synchronous=FULL`. Every
  committed transaction performs a full disk flush. In a write-heavy pipeline (`store`,
  `build-zip`, response/download import) that is the dominant cost.
- **Why it's worse for you:** the config uses `Cache=Shared` (same-process shared cache).
  Under any concurrency, unset `busy_timeout` means writers/readers surface
  `SQLITE_BUSY` immediately instead of waiting.
- **Fix (recommended):**
  - Set `busy_timeout`, `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys`, and a
    sensible `cache_size` on every opened connection — e.g. run the PRAGMAs in `Create()`
    after `Open()`, or subscribe to the connection's `StateChange`/`Opened` event (so it
    applies to every pooled connection).
  - WAL is especially important if `store`/`retry` is ever parallelized or if the CLI and
    the Kestrel CRM host share the same DB file.
  - `Data Source=...;Cache=Shared` can be extended with `Pooling=true` and `Default Timeout`
    in the string; the PRAGMAs still have to run per connection.

#### 🔴 D2 — `UpsertDailyAsync` loads the entire table to dedup, then inserts row-by-row (autocommit)
`src/CKYC.Data/MasterRepository.cs` `UpsertDailyAsync`:
- `SELECT CustomerId FROM master_record` loads **every** existing id into a `HashSet`
  (`MasterRepository.cs:22`). This is O(table size) on every daily run — the table only grows.
- Then one `INSERT` per new id with **no surrounding transaction**
  (`MasterRepository.cs:34-47`) → each insert is its own autocommit + fsync.

- **Fix:** add a unique index on `master_record(CustomerId)` (schema already leans on
  uniqueness elsewhere) and use a single
  `INSERT OR IGNORE ... SELECT @id,@date,... UNION ALL ...` (or a VALUES multi-row) inside
  one transaction. `INSERT OR IGNORE` gives you the count delta without reading the table.

#### 🟠 D3 — Check-then-write patterns are non-atomic and extra round-trips
Several flows `SELECT COUNT(...)` then `INSERT` inside a transaction, which is not safe
under concurrent workers and doubles round-trips:
- `TryAddUploadResponseFileAsync` (`MasterRepository.cs:410`)
- `SearchRepository.ImportResponseAsync` (`SearchRepository.cs:220`)
- `DownloadRepository.ImportAsync` (`DownloadRepository.cs:19`)

Even sequentially, each is a wasted SELECT. **Fix:** add the unique index
(`upload_response_file(SourceHash,ResponseFileName)` already has an index — make it
`UNIQUE`; same for `download_response_file` hash pair and `search_response_file(SourceHash)`)
and use `INSERT OR IGNORE` / `INSERT ... ON CONFLICT DO UPDATE` — one statement, atomic.

#### 🟠 D4 — `AddResponseAsync` DELETE-then-INSERT for idempotency
`MasterRepository.cs:289-330`. The idempotent re-read deletes and re-inserts the detail row.
**Fix:** a single UPSERT
`INSERT ... ON CONFLICT(MasterRecordId, ResponseFileName, LineNumber) DO UPDATE SET ...`
(requires a `UNIQUE` index on that triple). Fewer round-trips and no gap where the row is absent.

#### 🟠 D5 — `NextAttemptNumberAsync` is a separate COUNT + race-prone
`MasterRepository.cs:743`. Each `LogAttemptAsync` first does `SELECT COUNT(*)` then the
`INSERT`; two concurrent attempts for the same record could both compute the same number.
**Fix:** compute the attempt number in the INSERT itself
(`INSERT ... SELECT (COUNT(*)+1), ... FROM master_record_attempt WHERE ...`) or rely on an
`Attempt`/`Stage` unique key with `ON CONFLICT` retry.

#### 🟠 D6 — Column-name indexer + `DateTime.TryParse` on every read
`MasterRepository.QueryAsync` (`MasterRepository.cs:753`), `ReadDate`/`ReadNullableDate`, and
all `GetResponsesAsync`/`Load*` maps use `r["Column"] as string` (a per-cell name→ordinal
lookup + boxing) and `DateTime.TryParse` per date cell.

- **Fix:** resolve `GetOrdinal(name)` once (cache in a `static readonly` ordinals dictionary or
  compute per-row and pass ordinals), use typed getters (`GetFieldValue<T>`,
  `GetString`), and parse the known ISO-8601 `"O"` cells with `DateTimeOffset.TryParseExact`
  or `long`/TEXT comparisons instead of `TryParse`.

#### 🟠 D7 — Dynamic `IN (@v0,...)` placeholders have no size guard
`GetByCustomerIdsAsync` (`MasterRepository.cs:74`), `MarkBatchAsync` (`:207`),
`IndividualRepository`/`LegalEntityRepository.GetByCustomerIdsAsync`. SQLite variables are
capped (default 999; often 32766). 500 works today, but the callers can pass unchecked sizes.
**Fix:** chunk into ≤ 900-id batches (or use a JSON/`VALUES` table) with a `Chunk(newSize)`
guard.

#### 🟡 D8 — `MarkBatchAsync` per-record DELETE+INSERT + one UPDATE each
`MasterRepository.cs:236-257`. Within the transaction this is fine, but it's N×3 statements.
At the 500 cap it's tolerable. **Fix (optional):** replace the DELETE/INSERT pair with a
single `UPSERT` keyed on `master_record_batch(MasterRecordId, BatchFile)` (the index already
exists — make it unique + `ON CONFLICT DO UPDATE`).

#### 🟡 D9 — `SELECT *` everywhere, incl. hot loops
All queries pull the full 40+-column `master_record` row even when a subset suffices. At
≤500 rows/batch this is negligible; at nightly volume it widens. Consider column projection in
hot paths (e.g. `GetRetryableForActivityAsync`).

#### 🔴 D10 — `InitializeSchemaAsync` ignores `CreateSchemaOnStartup`
`SqliteDatabase.InitializeSchemaAsync` runs all DDL/migrations on every startup regardless of
`DatabaseSettings.CreateSchemaOnStartup` (`SqliteDatabase.cs:33`, setting read at `:84`).
The `PRAGMA table_info` column checks are per-startup overhead and dead code once the flag is
honoured. Also, `SqliteDatabase` implements `IDisposable` but `AppContext` never disposes it
(`Program.cs` disposes only the CTS).

#### 🟡 D11 — Date/time stored as TEXT (schema-wide)
All timestamps/dates are `TEXT` in ISO `"O"`/`yyyy-MM-dd` form and compared lexicographically.
That works only while the formats are consistent, and it means ordering/`BETWEEN` on dates is a
string comparison. This is a deliberate, documented choice — keep it, but do the `ReadDate`
parsing efficiently (see D6) and never mix date formats.

### 3.2 File layer — `CKYC.Files`

#### 🟠 F1 — Whole file built in memory before writing
`CkycUploadWriter.Write` / `CkycLegalEntityUploadWriter.Write` accumulate into one
`StringBuilder`, then `File.WriteAllText` (`CkycBatchGenerator.cs:64`). `CkycSearchWriter.Write`
builds a `List<string>` and `string.Join` (`CkycSearchWriter.cs:19`).
- Fine at the 500/10 record caps; the memory and GC pressure grow linearly with the batch and
  with field width.
- **Fix:** write through a `StreamWriter`/`FileStream` directly (and `WriteAllTextAsync`),
  or use `StringBuilder` with a capacity hint; avoid the intermediate `List<string>` + Join.

#### 🟠 F2 — Response parsers split whole files into arrays
`CkycResponseParser.Parse` (`content.Split('\n','\r')` + `Split('|')`),
`SearchResponseReader.Parse` (same), `DownloadResponseReader` (`content.Split([...])` + a
re-`string.Join` for `AddLines`, `DownloadResponseReader.cs:72`). `ReadToEndAsync` loads each
archive entry into memory.
- **Fix:** stream line-by-line (`StreamReader.ReadLineAsync`) and use
  `ReadOnlySpan<byte>/<char>.Slice` between delimiters instead of allocating `Split('|')`
  arrays. Largest benefit for large `search-response`/`download-response` archives.

#### 🟡 F3 — `SearchResponseReader.Values` allocates two arrays per record via LINQ
`SearchResponseReader.cs:62,84` — `Enumerable.Range(...).Select(...).ToArray()` per detail
row, then copied into 26 parameters. At scale this is measurable GC churn. Read the span
directly into each named field instead.

#### 🟡 F4 — Batch generators: sync I/O behind async signatures + repeated doc scans
`CkycBatchGenerator.GenerateAsync` / `CkycLegalEntityBatchGenerator.GenerateAsync` return
`Task.FromResult` but do blocking `File.WriteAllBytes`, `File.Delete`, `CreateEntryFromFile`,
and re-enumerate `valid.SelectMany(EnumerateDocs)` plus per-doc `File.Exists`/`FileInfo`
(`CkycBatchGenerator.cs:68-77`, `:143-156`, `:163-169`).
- **Fix:** make them genuinely async (`.Async` I/O) or drop the `Async` name; enumerate docs
  once and cache sizes; use `CompressionLevel.Fastest` for the placeholder-only docs unless
  real PDFs are required to compress well.

### 3.3 Processor commands / services — `CKYC.Processor`

#### 🟠 P1 — `BuildZipCommand` O(n²) matching
`BuildZipCommand.cs:25-29`:
`customerIds.Select(id => individuals.FirstOrDefault(i => i.CustomerId == id))` is O(n²).
`individuals` is already retrieved by key. **Fix:** build a `Dictionary<string,Individual>`
once (or order `individuals` by a `CustomerId` set) and look up in O(1).

#### 🟠 P2 — `store` is strictly sequential (network + DB interleaved per record)
`StoreService.ProcessAsync` loops one record at a time: CRM HTTP fetch → save → update status →
log attempt, each a separate connection/round-trip. SQL is correctly used, but the wall-clock is
`N × (latency(CRM) + latencies(DB))`.
- **Fix (throughput lever):** `Parallel.ForEachAsync` with bounded parallelism
  (`MaxDegreeOfParallelism` ~ CPU/4), giving each task its own connection. **Prerequisite:**
  apply D1 (`busy_timeout`, WAL) or parallel writers will contend on SQLite's single writer and
  on `master_record_attempt` counting (see D5).

#### 🟡 P3 — Audit logging is chatty in hot loops
`StoreService.FailAsync` calls `GetActivityTypeByCodeAsync` per failing record
(`StoreService.cs:107`); `LogAttemptAsync` does a COUNT (D5) + INSERT per record
(`BuildZipCommand.cs:58-68`). Consider caching `activity_type` in memory for the process
lifetime and batching attempt inserts.

### 3.4 CRM / FVU — `CKYC.Crm`, `CKYC.Fvu`

#### 🟠 C1 — Hand-rolled `HttpClient`; no factory, no cleanup, no HTTP retry
`HttpCrmApiClient` (`HttpCrmApiClient.cs:22`) creates `new HttpClient { Timeout = ... }`.
It's created once in `AppContext` (good — not per-call), but:
- No `IHttpClientFactory`, no `SocketsHttpHandler` tuning
  (`PooledConnectionLifetime`, `MaxConnectionsPerServer`), no `AutomaticDecompression`.
- Transient HTTP failures aren't retried at the HTTP layer (only the record-level retry
  engine, which is coarse, 24h-backoff).
- **Fix:** register via `AddHttpClient` (or configure a shared `SocketsHttpHandler`) with
  `CancellationToken` timeout + a light transient-retry (Polly `OnTimeout`/`Retry`) — but keep
  the process's existing overall retry/backoff policy on top so they don't double-retry.

#### 🟡 C2 — STJ default options; no source-gen / named options
`GetFromJsonAsync` and `SettingsLoader.Load` use default `System.Text.Json` options (reflection
+ camelCase). **Fix (perf/trimming):** pass a shared
`JsonSerializerOptions`/`JsonSerializerContext` (source generator) so the `Individual` and
config DTOs are compiled — faster and AOT/single-file friendly.

#### 🟡 F5 (FVU) — subprocess stdout captured fully in memory
`CommandLineFvuRunner.RunProcessAsync` uses `ReadToEndAsync` for stdout+stderr
(`CommandLineFvuRunner.cs:114-115`). Correct (no deadlock), but a verbose FVU can grow memory.
Also `FindOutputZip`/`ExtractFileHash` read the zip listing and one entry; acceptable. No change
required beyond streaming stdout line-by-line if console output is ever large.

### 3.5 Cross-cutting

#### 🟠 X1 — No structured logging / telemetry
All output is `Console.WriteLine`/`Console.Error`. For production:
- `ILogger` + a `JsonConsole` sink, semantic message templates, and a correlation id
  (`CustomerId`/`BatchKey`/`claim.Token`) on every log line.
- Add metric counters (records fetched/saved/batched/retried, per-stage duration) and an
  `ActivitySource` so OpenTelemetry can trace the pipeline.

#### 🟡 X2 — Globalization/culture
ISO `"O"` timestamps and `"dd-MM-yyyy"` / `"yyyy-MM-dd"` dates are culture-invariant — good.
Consider `<InvariantGlobalization>true</InvariantGlobalization>` in
`Directory.Build.props` for a batch CLI (deterministic, smaller, faster).

#### 🟡 X3 — Startup/assembly options
`Directory.Build.props` already sets `net10.0`, `Nullable`, `TreatWarningsAsErrors`,
`AnalysisMode=Recommended`, deterministic builds — excellent baseline. Evaluate:
- `<TieredPGO>true</TieredPGO>` for the FVU/response hot paths.
- `ServerGarbageCollection` for a long-running `crm serve`; irrelevant for short-lived commands.
- NativeAOT single-file (`PublishAot`) is **not** straightforward here because
  `SQLitePCLRaw.bundle_e_sqlite3` can't be trimmed to native easily — document/avoid unless the
  sqlite bundle is swapped for an AOT-compatible provider.

---

## 4. Prioritised remediation plan — updated 2026-08-25

Legend: ✅ done · ⏳ not done.

**Do first (biggest, cheapest wins):**
1. ✅ D1 — per-connection PRAGMAs: `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout`.
2. ✅ D2 — single-transaction, indexed `NOT EXISTS` dedup (unique-index equivalent).
3. ⏳ D3/D4 — unique indexes + UPSERT/`INSERT OR IGNORE` (blocked by the no-constraints schema).
4. ✅ P1 — O(n²) → dictionary lookup in `BuildZipCommand` (+ `BuildZipLegalCommand`).

**Then (throughput/scale):**
5. ⏳ P2 — `store`/`retry` bounded parallelism (blocked until D3/D4/D5-style atomicity is added).
6. ⏳ D6 — typed readers + cached ordinals; drop `DateTime.TryParse` from row mapping.
7. ⏳ F1/F2/F3 — stream file/response parsing instead of whole-file `Split`.

**Later (production hardening):**
8. ⏳ X1 — structured logging + OpenTelemetry metrics/tracing.
9. ⏳ C1/C2 — `IHttpClientFactory` + STJ source-gen.
10. ⏳ D10 — honour `CreateSchemaOnStartup`; dispose `SqliteDatabase`; F4 — genuinely-async I/O.

---

## 4.1 Measured / estimated speedup from the completed changes

> The completed work is **write-throughput + query-cost** optimisations, so they show up as
> *less wall-clock in the DB-heavy stages*. At the current demo scale (~12 records/day) the
> running time is dominated by process start-up and the real FVU subprocess, not SQLite — so
> the absolute per-run delta is small. The numbers below are the **relative** gains that become
> material as the batch cap is reached (N ≤ 500) and at nightly volume (N ≈ 1 000–10 000).

| Change | Cost removed / added | Speedup (dominant effect) | When it matters |
|--------|----------------------|---------------------------|-----------------|
| D1 — WAL + `synchronous=NORMAL` + `busy_timeout` | Commits drop from ~2 fsyncs (FULL/DELETE) to ~1 under WAL → writes blocked far less | **≈ 2× on pure write stages** (fetch/store/build-zip); removes `SQLITE_BUSY` under the shared-cache/concurrent profile | Any write-heavy stage; required before any parallelism |
| D2 — transaction + indexed `NOT EXISTS` dedup | N autocommit fsyncs → 1; whole-table `HashSet` scan → indexed per-id lookup | **≈ 3–10× on `fetch` dedup/insert** (N fsyncs → 1; O(table) scan → O(N)) | `fetch` with many ids; nightly daily-set load |
| D5 — attempt number in INSERT | Removes one connection + one COUNT per attempt (was 2 connections per attempt → 1) | **≈ 1.5–2× on per-record attempt logging** (`store`/`retry`/`build-zip`); also fixes the count race | Every logged attempt (multiples per record) |
| P1 — dictionary lookup | `O(N²)` `FirstOrDefault` scan → `O(N)` map lookup | **≈ N× fewer comparisons** for the build-zip ordering (500 records: ~250k → ~500 comparisons) | `build-zip` at the batch cap; grows quadratically |
| Indexes (child `MasterRecordId`, `(Status,Id)`, retry picker, `batch/fvu_run` key) | Table scans → index range scans for load/delete/status/retry queries | **≈ 2–5× on child-table load/delete + status/retry reads** | `store` (per-record load/delete), `retry` picker, `status`/`fvu` lookups |

**Overall estimate:** for a **write-heavy batch run at the 500-record cap**, these changes
should cut the SQLite-portion of the stage time by roughly **3–8×** (the D1 + D2 + D5
transaction/fsync wins are multiplicative on writes, and P1 removes the quadratic ordering).
At the current 12-record demo the stage time is already sub-second and dominated by process
startup + FVU, so the observable end-to-end change is small; the gain is in **headroom** and
in not degrading as volume scales. The remaining, larger wins are **P2 (parallelism)** and
**F1/F2 (streaming I/O)**, which are deferred.

> **Note (no measurement was run):** this is an estimate from the documented SQLite costs and
> the query-shape changes, not a benchmark. The report §9 measurement plan (bump
> `generateCount`, `Measure-Command` before/after) is the intended way to confirm the deltas
> empirically.

---

## 5. Key observations that LIMIT the impact of the findings

- **Small, enforced batch caps.** `MaxIndividualBatchRecords=500`,
  `MaxLegalEntityBatchRecords=10` (`CKYC.Core/Spec/CkycRecords.cs:8-9`). Several "memoryy big"
  findings (F1, D9) are proportionate today.
- **Reasonable foundation already in place:** parameterized SQL everywhere (no string
  concatenation into executable SQL), transactions used for multi-statement writes, `CancellationToken`
  threaded through the pipeline, `TreatWarningsAsErrors` + analyzers, SHA-256 via
  `SHA256.HashData` + `Convert.ToHexStringLower` (no LINQ/alloc churn).