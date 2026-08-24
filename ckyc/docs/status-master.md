# Status master — `master_record.Status`

Reference lookup for the single "current stage" flag on the `master_record` table.
Today `Status` is persisted as an **INTEGER** (value 0–10, the `MasterRecordStatus` enum).
This doc proposes a **status master** table that maps each value to a short 2–3 char code
and a proper description, so the flag can be read as `PND`, `CRM`, `SAV`, … without
changing the underlying numeric storage.

> Side-by-side comparison — numeric value kept alongside the short code so we can decide
> later which one to keep as the persisted flag.

## Mapping (value ↔ code ↔ name ↔ description)

| Value | Code | Enum / Name | Description | Terminal |
|------:|:----:|-------------|-------------|:--------:|
| 0 | PND | Pending | Newly fetched daily source customer; awaiting CRM enrichment. | no |
| 1 | CRM | CrmFetched | CRM data fetched successfully for this customer; awaiting save to record tables. | no |
| 2 | SAV | Saved | Individual details persisted to the record tables; awaiting batch generation. | no |
| 3 | BAT | Batched | Record enqueued into the generated `.UPL` batch file; awaiting upload. | no |
| 4 | FVP | FvuPassed | Batch submitted to the FVU and passed validation; ready for CERSAI upload. | yes |
| 5 | FVF | FvuFailed | Batch submitted to the FVU and failed validation; needs operator attention. | no |
| 6 | FLD | Failed | Permanent failure (e.g. could not be saved after retries); needs manual intervention. | yes |
| 7 | UPL | Uploaded | Batch uploaded/submitted to CERSAI; awaiting a response file. | no |
| 8 | RSP | ResponseRead | At least one CERSAI response file has been read for this record. | no |
| 9 | RCN | Reconciled | Record reconciled (matched/resolved against the CERSAI reply). | yes |
| 10 | REJ | Rejected | Record permanently rejected by CERSAI. | yes |

## Status-master table (DDL)

Mirrors the existing `activity_type` master convention ("length validation yes,
other validation no", nullable columns, seeded idempotently).

```sql
CREATE TABLE IF NOT EXISTS status_master (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    StatusValue    INTEGER,
    Code           VARCHAR(3),
    Name           VARCHAR(50),
    Description    VARCHAR(500),
    IsTerminal     INTEGER,
    IsActive       INTEGER,
    CreatedAt      TEXT
);
CREATE INDEX IF NOT EXISTS ix_status_value ON status_master(StatusValue);
CREATE INDEX IF NOT EXISTS ix_status_code   ON status_master(Code);
```

## Seed (idempotent — insert only if the value is not already present)

```sql
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 0,'PND','Pending','Newly fetched daily source customer; awaiting CRM enrichment.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=0);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 1,'CRM','CrmFetched','CRM data fetched successfully for this customer; awaiting save to record tables.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=1);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 2,'SAV','Saved','Individual details persisted to the record tables; awaiting batch generation.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=2);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 3,'BAT','Batched','Record enqueued into the generated .UPL batch file; awaiting upload.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=3);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 4,'FVP','FvuPassed','Batch submitted to the FVU and passed validation; ready for CERSAI upload.',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=4);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 5,'FVF','FvuFailed','Batch submitted to the FVU and failed validation; needs operator attention.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=5);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 6,'FLD','Failed','Permanent failure (e.g. could not be saved after retries); needs manual intervention.',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=6);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 7,'UPL','Uploaded','Batch uploaded/submitted to CERSAI; awaiting a response file.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=7);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 8,'RSP','ResponseRead','At least one CERSAI response file has been read for this record.',0,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=8);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 9,'RCN','Reconciled','Record reconciled (matched/resolved against the CERSAI reply).',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=9);
INSERT INTO status_master (StatusValue, Code, Name, Description, IsTerminal, IsActive, CreatedAt)
SELECT 10,'REJ','Rejected','Record permanently rejected by CERSAI.',1,1,strftime('%Y-%m-%dT%H:%M:%SZ','now')
WHERE NOT EXISTS (SELECT 1 FROM status_master WHERE StatusValue=10);
```

## Wiring status

This status master keeps `Status` as the **numeric value** (0–10) and adds a `<-> Code`
mapping, so the existing `(int)` casts and SQL comparisons stay valid. It does **not**
require the INT→VARCHAR migration.

It is now wired in like `activity_type`:

- DDL (`status_master` table + indexes) — `src/CKYC.Data/Schema/Ddl.cs` (SQLite) and
  `scripts/sqlserver/schema.sql` (SQL Server mirror).
- Seed (idempotent `StatusValue` guard) — `Ddl.StatusMasterSeedStatements`, applied by
  `src/CKYC.Data/SqliteDatabase.cs` `InitializeSchemaAsync()`.
- Domain model — `src/CKYC.Core/Domain/StatusMaster.cs`.
- Repository — `GetStatusMastersAsync()` / `GetStatusMasterByValueAsync(int)` declared on
  `IMasterRepository` (`src/CKYC.Core/Abstractions/Services.cs`) and implemented in
  `src/CKYC.Data/MasterRepository.cs` (with `ReadStatusMaster`).

The `IsTerminal` column mirrors `MasterRecordStatusExtensions.IsTerminal()`.

If you later decide to store the short code as the flag, the same table is the source
for the INT→VARCHAR(3) migration of `master_record.Status`,
`master_record_attempt.Status` and `master_record_reattempt.PreviousStatus`.
