# CKYC Processor — Performance Analysis & .NET 10 Best-Practices Review

**Report date:** 2026-08-24 (repo snapshot `7f9b60d`)
**Scope:** `D:\centralprocessing\ckyc` — `CentralCkyc.slnx` (CKYC.Core, CKYC.Data, CKYC.Crm, CKYC.Files, CKYC.Fvu, CKYC.Processor), targeted at `net10.0`.
**Method:** Static code review of every project + runtime/build configuration; no prior performance-analysis document exists in the repo (searched for `perf|bench|analys|report` — none found), so this report is the initial baseline. The only existing perf-relevant evidence is the README's end-to-end verification of a 12-record batch against the real `FVU_RUN_UTILITY.exe`.
**Environment:** .NET SDK `10.0.302` (also `9.0.301` present), Windows, SQLite via `Microsoft.Data.Sqlite.Core 10.0.11` + `SQLitePCLRaw.bundle_e_sqlite3 2.1.12`, central package management.

> **No code changes were made.** This report is analysis + prioritized recommendations only.

---

## 1. Executive summary

The codebase is clean, modern .NET 10 (nullable, analyzers, `TreatWarningsAsErrors`, CPM, fully async I/O — no `.Result`/`.Wait()`/`async void` anywhere). At the current demo scale (~12 records/day) everything is fast and the existing single-process design is sound. The performance issues are **scalability patterns**, not micro-optimizations: they become dominant when `generateCount` / `--limit` grows to thousands of records, which is exactly what a nightly CKYC batch would see.

Top findings, in priority order:

| # | Finding | Where | Impact at scale |
|---|---------|-------|-----------------|
| 1 | **~5–12 DB round-trips per record** in `store`/`response read`/`fvu`, each opening a fresh connection | `StoreService`, `ResponseCommand`, `FvuCommand`, `MasterRepository` | 10k records → 50k–120k round-trips |
| 2 | **N+1 query pattern** loading record tables; record-40 read **twice** for the same row | `IndividualRepository.GetByCustomerIdsAsync` | 6+ query/record → 6N+ queries |
| 3 | **Full-table materialization** to dedupe + per-row autocommit inserts | `MasterRepository.UpsertDailyAsync` | O(table) memory + N commits |
| 4 | **No SQLite tuning**: default DELETE journal, `synchronous=FULL`, no `busy_timeout`, `Cache=Shared` instead of WAL | `SqliteDatabase`, `appsettings.json` | Insert/commit throughput 10–100× below what SQLite can do |
| 5 | **Missing indexes on every `kyc_record_*` table** (`MasterRecordId`) and on `kyc_record_20(CustomerId)` | `Ddl.cs` | Every save/load is a table scan as tables grow |
| 6 | **O(N²) `FirstOrDefault` inside a `Select`** | `BuildZipCommand.cs:24-28` | 50k records → 2.5G string comparisons |
| 7 | Per-row `COUNT(*)`+INSERT attempt numbering on a **separate connection**, race-prone | `MasterRepository.NextAttemptNumberAsync` / `LogAttemptAsync` | 2 extra round-trips/attempt + integrity risk |
| 8 | Per-record **`Console.WriteLine`** in loops | `StoreService`, `ResponseCommand` | Console I/O becomes a wall-clock bottleneck at 10k+ |
| 9 | Full-file `Split` + whole-file reads for response parsing | `CkycResponseParser` | Memory spikes on large `.RES` files |
| 10 | Whole `Batched` status table loaded, then filtered in memory | `FvuCommand.MarkRecordsAsync` | Unbounded read per FVU run |

> **Update (2026-08-25):** items **4 (SQLite tuning)**, **3 (full-table dedup)**, **6 (O(N²))**,
> the **missing indexes (5)**, and **7 (attempt COUNT round-trip)** have been implemented on
> branch `performance`. See §1.1 below for status, and note that the report's UNIQUE-index
> recommendations were *deliberately* skipped to respect the schema's "no constraints" spec.

---

## 1.1 Implementation status (2026-08-25)

| Finding | Status | What was done |
|---------|--------|---------------|
| **4 — SQLite tuning** | ✅ Done | `SqliteDatabase.Create()` applies `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=5000`, `cache_size=-20000`, `temp_store=MEMORY` on every opened connection. `src/CKYC.Data/SqliteDatabase.cs`. |
| **3 — `UpsertDailyAsync`** | ✅ Done | Replaced whole-table `HashSet` + N autocommit inserts with one prepared command in **one transaction** via `INSERT ... SELECT ... WHERE NOT EXISTS`. `src/CKYC.Data/MasterRepository.cs`. |
| **7 — attempt numbering** | ✅ Done | Removed `NextAttemptNumberAsync` (COUNT round-trip); attempt number computed in the INSERT via `SELECT COUNT(*)+1`. `src/CKYC.Data/MasterRepository.cs`. |
| **6 — O(N²) ordering** | ✅ Done | `FirstOrDefault`-per-id → `Dictionary` lookup in `BuildZipCommand` and `BuildZipLegalCommand`. |
| **5 — missing indexes** | ✅ Done | Non-unique `CREATE INDEX IF NOT EXISTS`: all `kyc_record_*(MasterRecordId)`, `master_record(Status,Id)`, retry-picker `(Status,RetryCount,LastActivity,NextRetryAt)`, `batch(BatchKey)`, `fvu_run(BatchKey)`. `src/CKYC.Data/Schema/Ddl.cs`. |
| **1 — round-trips / N+1** | ⏳ Not done | Bigger refactor (command-scoped transactions + batch queries); deferred. |
| **2 — N+1 + duplicate record-40 read** | ⏳ Not done | Touches `IndividualRepository`; deferred. |
| **8 — per-record console I/O** | ⏳ Not done | Logging-throttle; deferred. |
| **9 — full-file `Split` parsing** | ⏳ Not done | Streaming parsers; deferred (also F1/F2 in the newer report). |
| **10 — whole `Batched` load** | ⏳ Not done | `FvuCommand`; deferred. |

**Why the UNIQUE-index / `INSERT OR IGNORE` / `ON CONFLICT` fixes were skipped:** the schema
(`Ddl.cs` header) is deliberately constraint-free — *"length validation yes, other validation
no"* — so adding UNIQUE indexes could fail on existing data and violates the documented design.
Behavior-preserving equivalents were used instead (single transaction / indexed `NOT EXISTS` /
subquery attempt numbering).

**Validation:** `dotnet build` 0 warnings/0 errors; `CKYC.SpecChecks` passed; `fetch` runs
idempotently (5 → 0 inserted on rerun); attempt numbering increments correctly (retry 1 → retry 2).

**Estimated speedup:** ≈ **3–8× on the SQLite-portion of write-heavy stages** at the batch cap
(details in `performance_report.md` §4.1).

---

## 2. Workload profile and scale envelope

- Process model: one CLI invocation per pipeline step (`fetch` → `store` → `build-zip` → `fvu` → `response read`; plus `retry`, `reattempt`, `reconcile`, `status`). Each run boots the process, initializes the schema, does its stage, exits.
- Data: `master_record` is the single status row; up to **6 child tables per record** (`kyc_record_20/30/40/50/60/70`), an attempt trail per stage (`master_record_attempt`), response history (`master_record_response`), reattempt history, batch/FVU journals.
- Config: `source.generateCount = 12`; commands default `--limit 1000`.
- SQLite is deliberately the demo store; SQL Server is the documented production path (`scripts/sqlserver/schema.sql`) but **no SQL Server implementation exists in code yet** (`SqliteDatabase` only), so this review measures the SQLite path.

**Scale envelope:** everything below is negligible at N ≤ 100 records and increasingly dominant at N ≈ 1 000–10 000. The recommendations target that envelope without changing behavior.

---

## 3. Hot-path round-trip accounting (the core finding)

Every repository method calls `_db.Create()` → `new SqliteConnection(...)` + `Open()` per operation (pooling is on by default in Microsoft.Data.Sqlite, so `Open/Close` itself is cheap — the cost is the **number of statements and commits**, not the connection object).

### 3.1 `store` (step 3) — per record
`StoreService.ProcessAsync` per record:

1. HTTP `GET /api/customers/{id}` (`HttpCrmApiClient.GetCustomerAsync`)
2. `UpdateStatusAsync` → conn #1, 1 UPDATE (status → CrmFetched)
3. `LogAttemptAsync` → `NextAttemptNumberAsync` (conn #2, `COUNT(*)`), then conn #3, 1 INSERT
4. `SaveAsync` → conn #4: transaction with **6 DELETEs + 6 INSERTs** (`IndividualRepository.DeleteExistingAsync` + `InsertRecord20..70`)
5. `UpdateStatusAsync` → conn #5, 1 UPDATE (status → Saved)
6. `LogAttemptAsync` → 2 more conns (COUNT + INSERT)

**≈ 7 connections / ~12 statements per success**, every one committed individually except the save transaction. The failure path (`FailAsync`) adds `GetActivityTypeByCodeAsync` (conn), `UpdateStatusAsync`, `RecordRetryAsync`, `LogAttemptAsync` ≈ 6 more statements/conns.

### 3.2 `response read` (step 6) — per reply detail
`ResponseCommand` per detail line:

1. `ResolveMasterAsync` → `GetByBatchLineAsync` → `QueryAsync` (conn #1)
2. `AddResponseAsync` → conn #2: transaction (DELETE + INSERT + UPDATE)
3. `LogAttemptAsync` → 2 conns (COUNT + INSERT)
4. `UpdateStatusAsync` → conn #3 (for reconciled/rejected)

**≈ 5 connections / ~7 statements per detail.** A 10 000-detail CERSAI reply = ~50 000 round-trips, each with parameter setup/teardown. This loop also does the COUNT-based attempt numbering per detail, so the `master_record_attempt` insert is preceded by a COUNT over that record's history.

### 3.3 `fvu` (step 5) — per record
`FvuCommand.MarkRecordsAsync`:

- `GetByStatusAsync(Batched, int.MaxValue)` loads **all** batched rows (not just this batch's file), then filters with LINQ `r.BatchFile == uploadFileName`.
- Per record: `UpdateStatusAsync` + `LogAttemptAsync` (2 conns) → ≈ 3 statements/2 conns each, sequential.

### 3.4 `build-zip` (step 4)
- `GetByCustomerIdsAsync` — the N+1 (see §4.2).
- O(N²) ordering (`BuildZipCommand.cs:24-28`) — see §5.1.
- `MarkBatchAsync` — one `UPDATE ... WHERE Id IN (...)` then **one UPDATE per record** for `BatchRecordLine` (`MasterRepository.cs:182-193`); N statements in the transaction.
- `LogAttemptAsync` per batched record (2 conns each) and per skipped record again (`BuildZipCommand.cs:57-79`).

### 3.5 `fetch` (step 1)
`UpsertDailyAsync` (§4.1) — 1 full scan + N autocommit inserts. `CbsFailAsync` per failed id: `EnsureAsync` (SELECT+INSERT+SELECT), `GetActivityTypeByCodeAsync`, `UpdateStatusAsync`, `RecordRetryAsync`, `LogAttemptAsync` ≈ 8 statements.

---

## 4. Data layer deep dive (`CKYC.Data`)

### 4.1 `MasterRepository.UpsertDailyAsync` — the full-table dedup
```csharp
SELECT CustomerId FROM master_record        // entire column materialized
... HashSet … then one INSERT per id, no transaction
```
- Memory: O(table size) for the `HashSet` on every fetch run.
- Writes: N autocommit INSERTs → N commits → with default `synchronous=FULL`, ~2 fsyncs each.
- Dedup correctness depends on this in-memory snapshot; a `UNIQUE` index would make dedup atomic and the query unnecessary — but the schema philosophy explicitly forbids UNIQUE constraints (`Ddl.cs` header, README §"Data model"). The middle ground that preserves the philosophy: wrap in one transaction and use a **single parameterized INSERT outside the loop** (reuse prepared command), or a multi-row INSERT; or accept the constraint tradeoff in production (SQL Server path would use `MERGE`/`WHERE NOT EXISTS`).
- `EnsureAsync` also does SELECT → INSERT → SELECT (3 round-trips, 2 connections); `INSERT OR IGNORE` + `SELECT last_insert_rowid()` cuts it to 1–2.

### 4.2 `IndividualRepository.GetByCustomerIdsAsync` — N+1 (plus a duplicate read)
- 1 query for `kyc_record_20 ... IN (...)`.
- Then per individual (loop at lines 75–83): `LoadRecord30Async`, `LoadPermanentAddressAsync`, `LoadCurrentAddressAsync`, `LoadRecord50Async`, `LoadRecord60Async`, `LoadRecord70Async` — **6 queries per record**.
- `LoadPermanentAddressAsync` and `LoadCurrentAddressAsync` run **the identical statement** (`SELECT * FROM kyc_record_40 WHERE MasterRecordId=@m`) — the same row is fetched twice; both address blocks live in one row (`Perm*` + `Curr*` columns).
- Fix: 5 queries total (one per table, `WHERE MasterRecordId IN (...)`, chunked to ≤ ~500 ids per statement to stay under SQLite's variable limit), or one query per record table for the whole batch and a single in-memory grouping pass.
- `SaveAsync`'s delete-then-insert transaction is well-shaped; per-record transaction is correct, but for a 10k `store` a **command-scoped transaction** (one transaction for the whole batch with statement reuse) is 1–2 orders of magnitude faster.

### 4.3 `LogAttemptAsync` / `NextAttemptNumberAsync` — count-then-insert
```csharp
// NextAttemptNumberAsync on its own connection
SELECT COUNT(*) FROM master_record_attempt WHERE MasterRecordId=@m AND Stage=@s
// then in LogAttemptAsync, a NEW connection
INSERT INTO master_record_attempt ...
```
- 2 connections + a COUNT that grows with history, per attempt.
- **Race:** two concurrent attempts for the same (record, stage) can both read the same count → duplicate `Attempt` numbers. A `(MasterRecordId, Stage, Attempt)` unique index plus a single `INSERT ... SELECT COUNT(*)+1` would fix both the cost and the integrity issue, or compute the attempt number on the master row (`master_record.RetryCount`) which is already maintained.
- Small related issue: `RecordRetryAsync` calls `DateTime.UtcNow.ToString("o")` twice (lines 377/380) — hoist once per call.

### 4.4 Reader mapping overhead (secondary)
- `SELECT *` returns ~40 columns on `master_record`; every `MasterRecord` materializes all of them even when the caller needs 3. Wide rows + TEXT storage → larger data pages. Prefer explicit column lists where the query shape is fixed (most are).
- Heavy `r["col"]` indexer-by-name access (name → ordinal lookup + boxing per cell). Cached `GetOrdinal(...)` integers would remove the per-cell lookup; relevant mainly in the response loop (10k+ rows × ~16 string cells).
- `ReadDate`/`ReadNullableDate` use `DateTime.TryParse` (culture-sensitive, slow-ish, and CA1305 was explicitly relaxed in `.editorconfig`). For round-trip ISO-8601 storage use `DateTime.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)` or `ParseExact` — faster and unambiguous.
- `GetByCustomerIdsAsync`/`GetByCustomerIdsAsync` build dynamic `IN (@v0..@vN)` — fine for small sets; **chunk at ~500** params to avoid SQLite's variable limit (`SQLITE_MAX_VARIABLE_NUMBER`) on large batches.

### 4.5 Lookup tables re-queried in loops
`GetActivityTypeByCodeAsync` is called **per failure record** in `StoreService.FailAsync`, once in `FvuCommand`, once in `ResponseCommand`, once in `BuildZipCommand`. `activity_type` has 7 rows. Load it once into `AppContext` (a `FrozenDictionary<string, ActivityType>` from .NET 8+) and pass it around — removes 1 round-trip per failed record.

---

## 5. Schema & SQLite configuration (`Ddl.cs`, `SqliteDatabase.cs`)

### 5.1 Missing indexes
Current indexes: `master_record(CustomerId)`, `master_record(Status)`, `master_record_response(MasterRecordId)`, `master_record_attempt(MasterRecordId)`, `master_record_reattempt(MasterRecordId)`, `activity_type(Code)`, `status_master(StatusValue|Code)`, `master_record(BatchFile, BatchRecordLine)`.

Missing (query shapes that currently scan):
- **`kyc_record_20(CustomerId)`** — used by `GetByCustomerIdsAsync` `IN (...)`.
- **`kyc_record_20/30/40/50/60/70(MasterRecordId)`** — every load (`Load*Async`) and delete (`DeleteExistingAsync`) filters on `MasterRecordId`. These are the hot child-table lookups.
- **`kyc_record_20(MasterRecordId)`** — the delete-first step of every save.
- **`batch(BatchKey)`**, **`fvu_run(BatchKey)`** — keyed lookups (`--batch <key>` resolution).
- **Composite for the retry picker** (`MasterRepository.GetRetryableForActivityAsync`):
  `WHERE Status=? AND RetryCount<? AND LastActivity=? AND (NextRetryAt IS NULL OR NextRetryAt<=?) ORDER BY Id LIMIT n` → index `(Status, RetryCount, LastActivity, NextRetryAt)` (or `(Status, LastActivity, RetryCount, NextRetryAt)`).
- **`master_record(Status, Id)`** → makes `GetByStatusAsync ... ORDER BY Id LIMIT n` a pure index range scan without a sort.

**Action:** use `EXPLAIN QUERY PLAN SELECT ...` for each repository query to confirm index use before/after (SQLite CLI or `cmd.CommandText = "EXPLAIN QUERY PLAN ..."`).

### 5.2 Pragmas / connection settings — the single biggest lever
The connection string is `Data Source=...;Cache=Shared` and **no PRAGMAs are ever set**. For a write-heavy batch tool this leaves the slowest SQLite modes on:

| Setting | Default (current) | Recommended for this workload |
|---|---|---|
| `journal_mode` | DELETE | `WAL` (`PRAGMA journal_mode=WAL`) — readers don't block the writer; far fewer fsyncs |
| `synchronous` | FULL | `NORMAL` under WAL (durability is still good; FULL costs ~2 fsyncs per commit) |
| `busy_timeout` | 0 → immediate `SQLITE_BUSY` | `PRAGMA busy_timeout=5000` — protects concurrent commands (and the `Cache=Shared` multi-connection profile) |
| `cache_size` / `mmap_size` | default (negligible for the DB size, but...) | `-20000` (≈20 MB) + `mmap_size=268435456` once the DB grows |
| `temp_store` | default | `MEMORY` for the sort-heavy `ORDER BY` picker queries |

Where to set them: right after `conn.Open()` in `SqliteDatabase.Create()` (a few `ExecuteNonQuery` PRAGMA calls per pooled connection open is acceptable; better — set them once via the connection-string `Cache=`/`Default Timeout` options and a small init helper invoked per open, since pooling reuses the connection object). Also worth adding to `DatabaseSettings` so both SQLite and the future SQL Server provider can expose them.

Additional data-layer notes:
- `InitializeSchemaAsync` re-runs ~40 DDL statements + **~55 `PRAGMA table_info` probes** (one per additive migration) + seeds on **every CLI invocation** (`Program.cs:48` calls it always). Each pipeline step pays this. Use `PRAGMA user_version` as a schema-version marker and skip DDL when it matches (all statements are `IF NOT EXISTS`/idempotent today, so this is pure startup savings + fewer first-run races).
- `AppContext` never disposes `SqliteDatabase` (`ClearAllPools()` at exit is harmless but the `IDisposable` is dead code); one connection held for the command's lifetime would be cleaner than pool churn.

---

## 6. Memory & allocation findings

1. **`BuildZipCommand.cs:24-28` — O(N²):**
   ```csharp
   var orderedIndividuals = customerIds
       .Select(id => individuals.FirstOrDefault(i => i.CustomerId == id))  // scan per id
       ...
   ```
   For N saved records this is N × N comparisons. Build a `Dictionary<string, Individual>` (ordinal) once and index into it. Note `GetByCustomerIdsAsync` already built a `masterIdByCustomer` map in a similar shape — reuse the pattern.
2. **`CkycResponseParser.Parse` — whole-file line split:** `content.Split('\n', '\r')` allocates the full line array + every line copy up front. `File.ReadLines`/`ReadLinesAsync` + per-line `Split('|')` streams the file and caps memory at one line. Same for `InjectSimulatedHashes` (`FvuRunner.cs:76`, `File.ReadAllLines`).
3. **`DummyCrmDataProvider.GetCustomer` — SHA-256 per derived field:** `StableDigits`/`StableIndex` run **9 SHA-256 hashes per customer** (mobile, searchKey, PAN, Aadhaar, related-party CKYC no., employee CKYC id…). At demo scale trivial; at 10k customers that's 90k hashes. Options: compute the hash bytes once per `customerId` and derive all digits from them, or cache in a `Dictionary<string, ...>` (stable by construction). Replacing SHA-256 with xxHash is only safe if stability across processes doesn't matter (it does here — deterministic CRM).
4. **`CkycUploadWriter`:** per-record `string?[56]`/`string?[46]` arrays + `string.Join` are fine; the whole-batch `StringBuilder` could take a pre-sized capacity (`records.Count * ~800`) to avoid reallocations on 10k-record batches. `ZipFile.CreateFromDirectory(..., CompressionLevel.Optimal, ...)` on thousands of placeholder docs — `Fastest` (or `NoCompression` for the placeholders) cuts CPU meaningfully; the docs are `%PDF` placeholders.
5. **`ReconcileCommand`:** whole-report `StringBuilder` then `WriteAllTextAsync` — acceptable to ~100k rows; beyond that stream into a `StreamWriter`.
6. **Reader mapping (§4.4)** — the `SELECT *` + indexer + `TryParse` combination is the main allocation/cast hotspot in the read path.

---

## 7. Async, concurrency & I/O

**Good news (verified by grep across all `*.cs`):**
- No `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `async void`, `Task.Run` inappropriately used, no `lock` micro-synchronization, no sync-over-async on `HttpClient`/file APIs.
- `HttpClient` is created once per `AppContext` (correct single-instance usage; no socket exhaustion), with an explicit `Timeout`.
- Cancellation is threaded through (`CancellationToken`) and console Ctrl+C is handled.

**Findings:**
- **Console I/O per record:** `StoreService` (`Console.WriteLine` per saved record), `ResponseCommand` (per detail), `FvuCommand.Print` etc. On Windows, console writes block and are notoriously slow (µs–ms each); at 10k records this adds real wall-clock time. Throttle to progress-every-N or gate behind a verbose flag; keep per-record detail for failures only.
- **No parallelism — correct default for SQLite**, which serializes writers anyway. The one place bounded parallelism genuinely helps is the **CRM HTTP fetch** in `store` (network latency dominates; SQLite stays the single writer). If added: `Parallel.ForEachAsync`/`SemaphoreSlim`-bounded fetches of, say, 8–16 concurrent, feeding a **single shared connection/transaction** for the DB writes (SQLite connections are not thread-safe — serialize writes). Not a P0; the local dummy CRM makes it academic until the real CRM is wired in.
- `SimulatedFvuRunner`'s `await Task.Yield()` is harmless.
- `CommandLineFvuRunner` correctly reads stdout/stderr concurrently (avoids the classic pipe-deadlock), has a sane timeout + `Kill(entireProcessTree)` cleanup, and disposes the process. Good.

---

## 8. Build / runtime configuration & .NET 10 best practices

### 8.1 What is already right (keep)
- `net10.0`, `LangVersion latest`, `ImplicitUsings`, `Nullable`.
- `TreatWarningsAsErrors` + `EnableNETAnalyzers` + `AnalysisLevel latest` + `AnalysisMode Recommended` — already enforces most perf/correctness analyzers at build time.
- **Central Package Management** (`Directory.Packages.props`) — single source of truth; recommended.
- `Deterministic` + `ContinuousIntegrationBuild` — reproducible builds.
- Static cached `JsonSerializerOptions` instances (`SettingsLoader`, `InsertCommand`).
- Runtimeconfig explicitly disables hot reload (`MetadataUpdater.IsSupported=false`) — good for production CLI; shrinks nothing but confirms a non-developer runtime profile.
- Proper `async` pipeline and explicit `CancellationToken` flow throughout.

### 8.2 What is missing / recommended for .NET 10
| Item | Status | Note |
|---|---|---|
| **System.Text.Json source generation** | Missing | `Individual` (the CRM/enrich payload), `AppSettings`, `FvuSummary` are all bound with reflection-based serializer. Add `[JsonSerializable(...)]` contexts (`IndividualJsonContext`, `SettingsJsonContext`) → removes first-call reflection warmup, enables trimming/AOT later. |
| **`FrozenDictionary`/`FrozenSet` for lookup caches** | Missing | Load `activity_type`/`status_master` once into a frozen collection (net8+ type) and share via `AppContext`. |
| **`PRAGMA user_version` schema versioning** | Missing | Skip per-invocation DDL + 55 `table_info` probes on every CLI run. |
| **`InvariantGlobalization`** | ⚠️ optional | Cuts startup + culture overhead, but the code deliberately relies on explicit date patterns; prefer targeted invariant parsing (§4.4) over the bulk flag. |
| **`SatelliteResourceLanguages`** | optional | `en` only — trims resource assemblies; marginal for a CLI. |
| **ReadyToRun / Native AOT** | Not recommended now | Batch CLI; startup is a few ms already. AOT is complicated by `SQLitePCLRaw.bundle_e_sqlite3` native bits; revisit only if startup/warmup becomes a complaint. |
| **`ServerGarbageCollection`** | Not necessary | Single-threaded CLI stages; workstation GC is fine. The per-record allocation churn is dominated by fixable patterns above, not GC config. |
| **Dynamic PGO / tiered compilation** | Default on | No change needed; do not disable for Release. |
| **`AnalysisMode=All`** | Optional | Bump from `Recommended` to `All` to also catch e.g. `CA2012` (use `ValueTask` correctly) and allocation analyzers; must be paired with a triage pass in `.editorconfig`. |
| **`NuGetAudit=false` + `RestoreIgnoreFailedSources=true`** | ⚠️ note | Deliberate for offline builds (`build.ps1` comments). Not a perf item, but flagged because it disables vulnerability auditing — re-enable `NuGetAudit` when the box is online. |

### 8.3 SQL Server path note (production provider)
`README` advertises `database.provider=sqlserver`, but no SQL Server provider is implemented — the perf review of SQL Server is therefore **not possible yet** (ditto `scripts/sqlserver/schema.sql`). When porting, the same findings apply with extra weight: replace `SELECT *` with explicit columns, avoid per-row COUNT, use `SqlBulkCopy`/TVPs for the attempt/response inserts, and wrap whole batches in transactions. Worth an explicit statement in the report the port should not inherit the SQLite-path patterns above.

---

## 9. Measurement plan (before touching anything)

Reproduce a before/after baseline with the real pipeline. All steps use existing release build `src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe`.

```powershell
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"

# Scale test: bump the daily set, time each stage
# edit appsettings.json -> source.generateCount = 1000 (and 5000)
Measure-Command { & $exe fetch cust }
Measure-Command { & $exe store --limit 5000 }      # CRM server must be running
Measure-Command { & $exe build-zip --limit 5000 }
Measure-Command { & $exe response read }
```

Instrumentation toolkit (all work against .NET 10):
```powershell
dotnet tool install -g dotnet-counters   # live metrics: cpu, gc, exceptions
dotnet tool install -g dotnet-trace      # perfview-style traces
dotnet-trace collect --profile gc-verbose -- $exe store --limit 5000
dotnet-counters monitor --process-id <pid>
```
- Confirm SQLite query plans: run each repository query via `EXPLAIN QUERY PLAN` (before/after index PRAGMAs).
- Ideal micro-benchmark: add a disabled-trait `BenchmarkDotNet` project only if you want sub-ms numbers on the writer/parser; the CLI `Measure-Command` deltas are enough to validate the P0 fixes.

---

## 10. Prioritized recommendations

Priority = impact / effort. **P0** fixes the round-trip and scan explosion; **P1** removes structural costs; **P2** is polish.

Updated 2026-08-25: ✅ done · ⏳ not done.

### P0 — do these first
1. ✅ **SQLite pragmas + WAL** — Applied `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=5000`, `cache_size`, `temp_store=MEMORY` in `SqliteDatabase.Create()`. (`mmap_size` not set — deferred.)
2. ⏳ **Command-scoped transactions + statement reuse** — Bigger signature refactor; deferred.
3. ⏳ **Kill the N+1 and duplicate read in `GetByCustomerIdsAsync`** — Touches `IndividualRepository`; deferred.
4. ✅ **Make `UpsertDailyAsync` single-transaction and prepared** — Done via `INSERT ... SELECT ... WHERE NOT EXISTS` in one transaction (no UNIQUE index, per the no-constraints spec).
5. ✅ **`NextAttemptNumberAsync` → single INSERT with `SELECT COUNT(*)+1`** — Done (no unique index added; subquery computes the number atomically). Removed the dead method.

### P1 — structural
6. ✅ **Indexes** — Added non-unique `CREATE INDEX IF NOT EXISTS` for the child `MasterRecordId` keys, `master_record(Status,Id)`, the retry-picker composite, `batch(BatchKey)`, `fvu_run(BatchKey)`.
7. ✅ **`BuildZipCommand` O(N²) → `Dictionary` lookup** — Done in `BuildZipCommand` and `BuildZipLegalCommand`.
8. ⏳ **`FvuCommand` → query by batch file directly** — Deferred.
9. ⏳ **Cache `activity_type` / `status_master` in-memory** — Deferred.
10. ⏳ **`PRAGMA user_version` schema marker** — Deferred.
11. ⏳ **Stream parsing + chunk `IN` lists** — Deferred (F1/F2 in the newer report).
12. ⏳ **Throttle per-record console output** — Deferred.

### P2 — polish
13. ⏳ Explicit column lists + cached `GetOrdinal` + invariant date parsing in the reader mappings; hoist duplicate `UtcNow` strings.
14. ⏳ `CompressionLevel.Fastest` for placeholder docs; pre-sized `StringBuilder`; stream the reconcile CSV.
15. ⏳ Reduce per-customer SHA-256 churn in `DummyCrmDataProvider` (one hash per customer).
16. ⏳ System.Text.Json source-generated contexts for `Individual`/settings; `FrozenDictionary` for the lookup caches.
17. ⏳ Sanity items only (no behavior change): dispose `SqliteDatabase` through `AppContext` (`using var app = ...`).

### Explicitly *not* recommended
- Parallelizing SQLite writes (writer serialization defeats it).
- Native AOT / ReadyToRun at this stage.
- Switching allocator/GC settings without data.
- Replacing `DateTime` strings with `TEXT`→`INTEGER` epoch globally (the ISO-8601 TEXT choice is already good for SQLite range scans and debugging).

---

## 11. Appendix — verification notes

- Grep for sync-over-async / thread hazards (`\.Result|\.Wait\(|GetAwaiter\(\)|async void|Task\.Run|Parallel\.|lock\s*\(`): **no hits** in `src/**/*.cs`.
- Existing indexes/seeds confirmed in `Ddl.cs` lines 358–377; additive migrations list at 506–562.
- `FvuSettings`/`BatchSettings`/`SimulationSettings` knobs confirmed in `AppSettings.cs`; pipeline behavior verified against `README.md` and command sources.
- No existing `perf_*`, `benchmark*`, or analysis docs in the repo — this report is the baseline; re-run §9 after each P0/P1 landing to quantify deltas.

*Reviewed artifacts: 6 projects, 60+ source files, `Directory.Build.props`, `Directory.Packages.props`, `CentralCkyc.slnx`, `appsettings.json`, `runtimeconfig.json`, `build.ps1`, `.editorconfig`, README/BUILD_GUIDE. No changes made.*