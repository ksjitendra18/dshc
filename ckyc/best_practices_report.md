# CKYC Processor — .NET 10 & Production Best-Practices Report

**Date:** 2026-08-25
**Scope:** full `src/` solution — .NET 10 (LTS), SQLite via `Microsoft.Data.Sqlite`.
**Nature:** non-functional review of production readiness against .NET 10 and modern
EF/data-access conventions.

> **Update (2026-08-25):** the data-access items DA-1 (PRAGMAs) and part of DA-4 (plain,
> non-unique indexes) have been implemented on branch `performance` after this report was
> written. The UNIQUE-index recommendations (DA-3, DA-4) were **not** applied — the schema's
> deliberate "no constraints" design forbids them. See `performance_report.md` §1.1 and
> `perf_report.md` §1.1 for the status of every change.

> **Important framing:** the codebase does **not** use Entity Framework Core. The data layer
> is hand-written ADO.NET (`Microsoft.Data.Sqlite` + repositories). This document (a) reviews
> the current raw-ADO approach against .NET 10 best practices, and (b) explains what an EF
> Core 10 migration would change, so the team can decide consciously. Sections marked
> **[EF]** specify what EF Core would give you; the rest apply as-is.

---

## 1. What is already done well

- **Modern language/runtime baseline** — `net10.0`, `LangVersion latest`, `ImplicitUsings`,
  `Nullable enable`, `TreatWarningsAsErrors`, `EnableNETAnalyzers`,
  `AnalysisMode=Recommended`, deterministic builds, CI build flag (`Directory.Build.props`).
  This is a strong, current starting point.
- **Parameterized SQL everywhere** — no string-concatenated SQL; input never risks SQL injection.
- **Transactions for multi-statement writes** — `SaveAsync`, `MarkBatchAsync`, claim/complete,
  response/download import all wrap their statements in a `BeginTransaction`.
- **Explicit small repositories + interface abstractions** (`CKYC.Core/Abstractions`),
  UI-independent (`CKYC.Crm` swap-aware, `database.provider` switch, `useRealFvu` switch).
- **Cancellation is threaded through** the pipeline and CLI (via `CT` + linked `CTS`).
- **Deterministic, culture-invariant date/format handling** for the file formats (ISO-8601 +
  `dd-MM-yyyy`), and hash via `SHA256.HashData`/`Convert.ToHexStringLower`.

---

## 2. Configuration & startup

| # | Area | Finding | Recommendation |
|---|------|---------|----------------|
| C-1 | Config validation | `AppSettings` has no validation (it even stands in for `IHttpClientFactory`-style wiring). Invalid `appsettings.json` values surface only at runtime, deep in a call. | Add `Microsoft.Extensions.Options` validation: `ValidateOnStart`, `[Required]`/range attributes (or `FluentValidation`), and validate the "mode" switches (`Crm.Mode`, `database.provider`, `fvu.useRealFvu`) at startup. |
| C-2 | Secrets / env | Connection string, FVU exe path, and output roots are hard-coded defaults in `appsettings.json`. | Use configuration providers (env vars / user secrets / `appsettings.{Environment}.json`); never commit real credentials. A connection string is not a secret here (local file), but the pattern should not encourage embedding credentials. |
| C-3 | Schema bootstrap | `InitializeSchemaAsync` runs (nearly) all DDL + migration probes on *every* startup and ignores `CreateSchemaOnStartup`. | Gate on `CreateSchemaOnStartup`; make a proper versioned-migrations story (`EF` `Migrations`, or a `PRAGMA user_version`-based runner) so startup is cheap and repeatable. |
| C-4 | Central packages | Good: `Directory.Packages.props` (CPM) pins both real packages; `RestoreIgnoreFailedSources` + local cache for offline. | Re-enable `NuGetAudit` (currently `false`) to get package vulnerability flags — security posture for production. |

---

## 3. Dependency injection, lifetimes & disposal

| # | Finding | Recommendation |
|---|---------|----------------|
| DI-1 | **Hand-rolled composition root** (`AppContext`). Fine for a CLI, but it means no lifetime management, no `IOptions<T>`, no testing seams, and manual disposal. | Move wiring to the built-in container (`Microsoft.Extensions.DependencyInjection`): register repositories as singletons (they hold no mutable state beyond `ICkycDatabase`), the DB as singleton, and commands as transient. This also buys `IOptions<T>` + source-generated config binding. |
| DI-2 | `SqliteDatabase : IDisposable` is **never disposed** (`AppContext` never calls `Dispose`; `Program` disposes only the CTS). `ClearAllPools` never runs. | Dispose it (or drop the `not`-needed `IDisposable`); if using DI, register with the right lifetime and let the container dispose it. |
| DI-3 | `HttpCrmApiClient` constructs `new HttpClient` (`HttpCrmApiClient.cs:22`). Single instance today, but no pooling/handler tuning and no HTTP retry. **[EF-adjacent]** The documented .NET 10 pattern for long-lived HTTP is `IHttpClientFactory` + `AddHttpClient` + typed clients. | Use `AddHttpClient` with a named/typed client, `SocketsHttpHandler` (`PooledConnectionLifetime`, `MaxConnectionsPerServer`, `AutomaticDecompression`), a request timeout, and a light transient retry — layered under the existing record-level retry/backoff policy. |

---

## 4. Data-access conventions (current raw ADO vs EF Core)

### 4.1 Current raw-ADO review

| # | Finding | Recommendation |
|---|---------|----------------|
| DA-1 | Every method opens a fresh connection (`_db.Create()`); no shared connection, no command preparation/caching. | With Microsoft.Data.Sqlite pooling (default) the open is cheap, but: apply the PRAGMA set (see `performance_report.md` §D1) — ✅ **done** — and `SqliteCommand.Prepare()` the hottest statements (or cache prepared commands) once per connection where contention is real. |
| DA-2 | Row mapping uses `r["Col"] as string` + `DateTime.TryParse` per cell (boxing + name lookups). | Use typed getters with cached `GetOrdinal` ordinals; avoid `TryParse` on the known ISO format. |
| DA-3 | Dedup/`upload_response_file`/`import_response` rely on **check-then-insert**, which is non-atomic (see `performance_report.md` §D3/D4). | Prefer `INSERT OR IGNORE` / `ON CONFLICT` + **unique indexes**. This is both a performance and a correctness fix. |
| DA-4 | Schema is intentionally constraint-free (length only, no NOT NULL/UNIQUE/FK). | This is a deliberate spec choice, but it pushes all integrity into the app layer. At minimum add the **unique/generated indexes the queries already assume** (`master_record(CustomerId)`, `master_record_batch(MasterRecordId, BatchFile)`, response `(MasterRecordId,ResponseFileName,LineNumber)`, `upload_response_file`/`search_response_file`/`download_response_file` hash pairs, `search_request` claim). Document the trade-off. |

### 4.2 What an EF Core 10 migration would change **[EF]**

| Concern | EF Core 10 benefit |
|---------|-------------------|
| Verbosity | The 7 `INSERT` + `DELETE` per `SaveAsync` and the massive `QueryAsync` maps collapse into `DbContext` + `DbSet<T>`, auto-mapped entities, `SaveChanges` (one batch). |
| Read perf | `AsNoTracking()` on read-only pipelines; compiled models; model caching; automatic parameter caching/prepared statements. |
| Writes | `SaveChanges` batching (multiple commands per round-trip); `ExecuteUpdate`/`ExecuteDelete` for set-based updates (mirrors the `UPDATE ... WHERE Id IN` patterns without manual placeholders). |
| Migration | `migrations add` replaces the hand-built `ALTER TABLE` probe list in `Ddl.cs`, with an explicit migration history table. |
| SQL Server path | The existing `scripts/sqlserver/schema.sql` can be regenerated from the model via migrations, keeping the `sqlite`/`sqlserver` switch instead of two divergent DDL sources. |
| Concurrency | `xmin`/rowversion-style concurrency tokens on the `master_record` stage transitions if concurrent `store`/`retry` matters. |

**Constraints to note before adopting EF:**
- EF needs real keys/relationships — the length-only, unconstrained schema would need at least
  PK/FK/unique modeling (conflicts with the "no constraints" spec; would need an explicit decision).
- `Microsoft.Data.Sqlite` via EF is supported; the SQL Server provider keeps the dual-provider intent.
- EF's JSON columns (`ToJson`) could replace the `RawRequestJson`/`SummaryJson` TEXT columns.

This is a **material architectural decision**, not a drop-in: it changes the domain-layer
purity (entity POCOs in `CKYC.Core` vs EF-owned entities). Recommend a deliberate cost/benefit
before committing.

---

## 5. Concurrency & threading

- **SQLite single-writer.** Any parallelization (`store`, `retry`, parallel `search-process`)
  must be preceded by `busy_timeout` + WAL (see `performance_report.md` §D1). Currently the
  pipeline is strictly sequential, so there is no contention today.
- **Claim/atomic claim token** (`SearchRepository.ClaimAsync`) is a good design (claim →
  process → complete/fail with optimistic `WHERE ClaimToken=` guards). Keep it; add a `UNIQUE`
  where practical.
- **No locking race today**, but `NextAttemptNumberAsync` (COUNT-then-INSERT) and the
  check-then-insert idempotency paths would race under two workers.

---

## 6. Testing & quality gates

| # | Finding | Recommendation |
|---|---------|----------------|
| T-1 | Only `tests/CKYC.SpecChecks` (a console harness `Program.cs`, not a unit-test framework). No tests for repositories/services. | Add `xUnit`/`NUnit`. For repositories, use in-memory/`Data Source=:memory:` (or a temp file) SQLite with the `AppContext`-equivalent wiring; for SQL Server, use Testcontainers. Extract testable seams from `StoreService`/`RetryService`. |
| T-2 | No CI orchestrating build+test in the repo (only `build.ps1`). | Add a CI job: `dotnet build` (Release) + `dotnet test` + analyzers. Keep `TreatWarningsAsErrors`. |
| T-3 | `AnalysisMode=Recommended` could be `All` for stricter shipping quality. | Consider `AnalysisMode=All` or per-project suppression; at minimum keep `Recommended`. |
| T-4 | File-format correctness (`.UPL` layout, search `.SRC`, response parsing) is validated only manually. | Property/golden tests against the reference Excel layouts; the `Dcl`/parser + writer pair is the right place for snapshot tests. |

---

## 7. Observability, logging & error handling

| # | Finding | Recommendation |
|---|---------|----------------|
| O-1 | **No structured logging** — `Console.WriteLine` throughout. | `ILogger` + `JsonConsole` in `.NET 10`; semantic templates; correlation id per record/batch (`CustomerId`, `BatchKey`, `claim.Token`) via `AsyncLocal`/`DiagnosticContext`. Keep the plain-console output for the CLI UX if desired, but log elsewhere too. |
| O-2 | **No metrics/tracing.** | Add an `ActivitySource` per pipeline stage (`store`, `retry`, `build-zip`, `fvu`, `search-process`, response/download import) + counters (records processed/failed, retry rate, per-stage duration) exported via OpenTelemetry. Cheap, high value for a batch system. |
| O-3 | **Catch/return patterns** — `SaveAsync` swallows exceptions into result objects; `CommandLineFvuRunner` returns a failed result on exception; `CkycResponseParser`/readers throw `InvalidDataException`. | This is coherent, but ensure: (1) real failures are always visible (structured log + a nonzero CLI exit), (2) no `catch (Exception)` hides a catastrophic error without logging, (3) a consistent `ILogger` path. |
| O-4 | `Program.cs` prints `ex.ToString()` only when `SaveErrorsEnabled` — a debug knob leaking stack traces in prod config. | Gate verbose exception output behind an explicit `verbose`/`debug` flag, not simulation settings. |

---

## 8. I/O & resource management

| # | Finding | Recommendation |
|---|---------|----------------|
| IO-1 | Sync file I/O under `Async`-named methods (`CkycBatchGenerator.GenerateAsync`, writers/readers use `ReadToEndAsync` but heavy `Split`). | Prefer streaming (line-by-line) + `.Async` I/O; consistent async signatures (see `performance_report.md` §F1/F2/F4). |
| IO-2 | Zip/asset handling uses `System.IO.Compression` correctly, but `DownloadResponseReader` hashes artifacts by loading each into a `byte[]` (`memory.ToArray()`). | Stream into `SHA256.HashData(Stream)` / `CopyToAsync` without materializing the buffer for large artifacts. |
| IO-3 | Hard-coded absolute paths (`D:\...\runtime`, `output`, `search`) in appsettings defaults. | Output roots should come from config/env, resolved to relative/validated paths, and created once at startup. |

---

## 9. Security (fast checklist)

- **SQL injection:** no risk — parameterized SQL throughout. ✅
- **Package audit:** `NuGetAudit=false` — turn on. ⚠️
- **Config/secrets:** defaults are demo paths; production must use env/secret providers. ⚠️
- **HTTP:** `HttpCrmApiClient` talks HTTP locally; production CRM must be HTTPS + TLS, with
  certificate validation and a scoped timeout. ⚠️
- **Zip-slip:** `DownloadResponseReader`/`UploadResponseReader` write *entry name* paths
  (`support_docs/{document}`) — `IsSafeDocumentName` already guards the batch generator, but
  double-check any path derived from zip entries or file names is normalized under the target
  root. (Server-side path traversal protection.) ⚠️
- **Logging sensitive data:** DOB/PAN/mobile are written to console/logs in places — trim PII
  from logs in production. ⚠️

---

## 10. Suggested "production-ready" checklist (single source of truth)

**Config & lifecycle**
- [ ] `IOptions<T>` validation + `ValidateOnStart`; environment-based providers; no secrets in repo.
- [ ] Gate startup schema bootstrap on `CreateSchemaOnStartup`; versioned migrations.
- [ ] Enable `NuGetAudit`; keep `TreatWarningsAsErrors`; bump to `AnalysisMode=All` if feasible.
- [ ] DI container for the composition root; correct lifetimes; dispose `SqliteDatabase`.

**Data**  *(updated 2026-08-25 — see `performance_report.md` §1.1 for details)*
- [x] Per-connection PRAGMAs (WAL, `synchronous=NORMAL`, `busy_timeout`) — done in `SqliteDatabase.Create()`.
- [ ] Unique indexes for the app-relying uniqueness paths; `INSERT OR IGNORE`/`ON CONFLICT`.
      *(Skipped intentionally: the schema's "no constraints" spec forbids UNIQUE indexes, which
      could fail on existing data. Behavior-preserving single-transaction/indexed-`NOT EXISTS`
      substitutes were used for the `UpsertDailyAsync` dedup and the attempt counter.)*
- [ ] Decide explicitly on EF Core 10 vs raw ADO (see §4.2) and document the choice.

**Concurrency**
- [x] Prepare for SQLite single-writer before any parallelism (WAL + busy_timeout) — PRAGMAs are
      now applied per connection, so `store`/`retry` can be parallelized later without
      `SQLITE_BUSY` churn.
- [ ] Make `claim`/idempotency writes atomic via unique keys — still deferred (needs unique
      indexes / `ON CONFLICT`, which the schema forbids).

**Files & I/O**
- [ ] Stream file writes/parsing; async I/O; avoid whole-file `Split`/`ReadToEnd` in hot paths.
- [ ] Hash via streaming; guard zip-derived paths against zip-slip.

**Testing & CI**
- [ ] xUnit + SQLite in-memory repository tests; Testcontainers for SQL Server; golden tests
      for `.UPL`/`.SRC`/response layouts; CI builds + tests.

**Observability**
- [ ] `ILogger` + structured JSON logs with correlation ids; OpenTelemetry
      `ActivitySource` + counters; verbose-stack gated by a debug flag, not simulation settings.