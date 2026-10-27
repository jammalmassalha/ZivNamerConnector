# ZivNamerConnector – SAP → SQL Server Sync API

ASP.NET Core Web API (.NET 8 LTS) that reads SAP ERP tables through RFC (`RFC_READ_TABLE`, SAP NCo 3.1) and
upserts them into Microsoft SQL Server using a duplicate-free staging + `MERGE` pattern. CORS is preconfigured
for an Angular frontend on `http://localhost:4200`.

## Solution layout

```text
SapSyncSolution/
├── database/01_CreateTables.sql     # target tables sap.NPAT / NFAL / NBEW / ZNACCIDENT / ZNMCICASE
├── build/SapNco.props               # shared SAP NCo reference (x64) + friendly "missing NCo" error
├── lib/sapnco/                      # put sapnco.dll + sapnco_utils.dll here (not committed, SAP licensed)
├── src/
│   ├── SapSync.Core/                # domain: models, interfaces, orchestration (no SAP / SQL dependencies)
│   │   ├── Models/                  # SapSettings, SqlSettings, SapQueryRequest, SyncResult, SyncErrorTypes
│   │   ├── Interfaces/              # ISapDestinationManager, ISapTableReader, ISqlBulkWriter, ISapSyncOrchestrator
│   │   └── Services/                # SapSyncOrchestrator (Stopwatch metrics + structured logging)
│   ├── SapSync.Infrastructure/
│   │   ├── Sap/                     # SapDestinationManager, SapTableReader, OPTIONS splitter, type conversion
│   │   └── Persistence/             # SqlBulkWriter, MergeSqlBuilder
│   └── SapSync.Api/                 # SapSyncController, Program.cs (DI, CORS), appsettings[.Test|.Production].json
└── tests/SapSync.Tests/             # xUnit unit tests (+ opt-in SQL Server integration tests)
```

## Prerequisites

- .NET 8 SDK, **64-bit** process (every project sets `<PlatformTarget>x64</PlatformTarget>`).
- **SAP .NET Connector 3.1 for .NET (Core), x64** from the SAP Support Portal (<https://support.sap.com/nco>).
  Copy `sapnco.dll` and `sapnco_utils.dll` to `SapSyncSolution/lib/sapnco/` or build with
  `-p:SapNcoPath=<folder>`. `sapnco_utils.dll` is native, so it is copied next to the application on build/publish.
  NCo also needs the Microsoft Visual C++ 2013 runtime (x64) on Windows.
- An RFC user authorized for `RFC_READ_TABLE` (authorization objects `S_RFC` and `S_TABU_DIS`/`S_TABU_NAM`).

## Build, test, run

```bash
cd SapSyncSolution
dotnet build
dotnet test                                   # SQL tests are skipped unless SAPSYNC_TEST_SQL_CONNECTION is set
dotnet user-secrets set "SapSettings:Password" "<password>" --project src/SapSync.Api
dotnet run --project src/SapSync.Api
```

Set `SAPSYNC_TEST_SQL_CONNECTION` to a connection string for a scratch database to run the SQL Server integration tests.

## SAP environments (Test / Prod)

Each SAP landscape has its own settings file, which overrides `appsettings.json`:

| Build value | ASP.NET Core environment | Settings file | Output |
| --- | --- | --- | --- |
| `Test` | `Test` | `appsettings.Test.json` (SAP test system ZIT, test database) | `bin/Test/...`, `publish/Test/` |
| `Prod` | `Production` | `appsettings.Production.json` (SAP production system ZIP, production database) | `bin/Prod/...`, `publish/Prod/` |

Build or publish each environment separately:

```bash
cd SapSyncSolution
dotnet publish src/SapSync.Api -c Release -p:SapEnvironment=Test   # -> SapSyncSolution/publish/Test/
dotnet publish src/SapSync.Api -c Release -p:SapEnvironment=Prod   # -> SapSyncSolution/publish/Prod/
```

An environment build works like this:
- **Settings files:** it ships only `appsettings.json` plus its own environment file, so a Test package contains no production settings and vice versa.
- **Locked environment:** the environment is baked into the assembly, so `ASPNETCORE_ENVIRONMENT` is ignored and a warning is logged if it disagrees. For IIS, it is also written to `web.config`.
- **Startup log:** it logs the active environment and the SAP destination, host and client.
- **Typos:** an unknown value (e.g. `-p:SapEnvironment=Staging`) fails the build.

Supply secrets per server through environment variables rather than the JSON files:
- `SapSettings__Password`
- `ConnectionStrings__SqlServer`

SAP connection per environment:

| | Test | Prod |
| --- | --- | --- |
| Destination (`DestinationName`) | `ZIT` | `ZIP` |
| Application server (`AppServerHost`) | `172.24.5.72` (zisaptst) | `172.24.5.25` (zisapci) |
| System number / client / language | `00` / `940` / `HE` | `00` / `940` / `HE` |
| User | `bapiapp` | `bapiapp` |
| Password | `SapSettings__Password` env var | `SapSettings__Password` env var |

The SAP password is deliberately not stored in the repository. Test and Prod builds refuse to start
(`SapSettings:Password is not set`) until it is supplied, e.g. on Windows / IIS:

```powershell
[Environment]::SetEnvironmentVariable("SapSettings__Password", "<password>", "Machine")
```

or for local runs: `dotnet user-secrets set "SapSettings:Password" "<password>" --project src/SapSync.Api`.

During development, plain `dotnet build` and `dotnet run` work as before. Use `dotnet run --project src/SapSync.Api --launch-profile Test` (or `Prod`) to run locally against either landscape.

## Configuration (`appsettings.json`)

| Section | Purpose |
| --- | --- |
| `ConnectionStrings:SqlServer` | Target database (name configurable via `SqlSettings:ConnectionStringName`). |
| `SapSettings` | Logon: `AppServerHost` **or** `MessageServerHost` + `LogonGroup` + `SystemId`, plus `SystemNumber`, `Client`, `User`, `Password` (use user-secrets / env var `SapSettings__Password`), `Language`, `SapRouter`. Pooling: `PoolSize`, `PeakConnectionsLimit`, `ConnectionIdleTimeout` (seconds). `ConvertAbapTypes` (default `true`) maps D/T/P/I/F to `date`/`time`/`decimal`/`int`/`float`; set `false` if the target stores SAP values as text. |
| `SqlSettings` | `BulkCopyBatchSize` (5,000–10,000), `BulkCopyTimeoutSeconds`, `CommandTimeoutSeconds`. |
| `SyncTables:Tables` | Catalog of predefined syncs (see [SAP IS-H tables](#sap-is-h-tables-namer)): `SapTableName` (defaults to the key), `TargetDbTable`, `PrimaryKeyColumns` (SQL names), `Fields` (SAP field → SQL column), optional `FilterCondition`, `PageSize`. |
| `Cors:AllowedOrigins` | Angular origins (default `http://localhost:4200`); credentials, all methods and headers allowed. |

Settings are validated at startup (`ValidateDataAnnotations` + `ValidateOnStart`).

## API

`POST /api/sapsync/sync`

```json
{
  "tableName": "MARA",
  "targetDbTable": "dbo.Materials",
  "primaryKeyColumns": ["MATNR"],
  "fields": ["MATNR", "MTART", "MATKL", "ERSDA"],
  "filterCondition": "MTART = 'FERT' AND ERSDA >= '20240101'",
  "rowCount": 50000,
  "rowSkip": 0
}
```

Response (`SyncResult`):

```json
{
  "success": true,
  "sapTableName": "MARA",
  "targetDbTable": "dbo.Materials",
  "rowsRead": 50000,
  "rowsProcessed": 1234,
  "sapReadElapsedMs": 8123,
  "sqlWriteElapsedMs": 911,
  "elapsedMs": 9040,
  "startedAtUtc": "2026-01-01T10:00:00+00:00",
  "completedAtUtc": "2026-01-01T10:00:09+00:00",
  "errorMessage": null,
  "errorType": null
}
```

Status codes: `200` success, `400` invalid request (`errorType: "Validation"`), `502` SAP failure
(`"SapRead"`), `500` SQL failure (`"SqlWrite"`).

## SAP IS-H tables (Namer)

1. Create the tables once per database (Test and Prod): run `SapSyncSolution/database/01_CreateTables.sql`
   (idempotent; creates schema `sap`).
2. Sync via the API – the field list, column names and keys come from `SyncTables` in `appsettings.json`.

| SAP table | SQL table | Key (SQL) | Columns (SAP field → SQL column) |
| --- | --- | --- | --- |
| NPAT – מטופלים | `sap.NPAT` | `PATIENTID` | PATNR→PATIENTID, EXTNR→ID_NUMBER, NNAME→LAST_NAME_PAT, VNAME→FRST_NAME_PAT, GSCHL→SEX, GBDAT→DOB, GLAND→BIRTHCTRY, FAMST→MAR_STAT, KONFE→REL_DENOM, TODDT→DT_O_DEATH_FROM, ORT→CITY, TELF1→PHONE, ZNKUPA_NOZZ→HMO |
| NFAL – מקרים | `sap.NFAL` | `PATCASEID` | FALNR→PATCASEID, PATNR→PATIENTID, FALAR→CASETYPE, ERDAT→CREATION_DATE, STUSR→CANCEL_USER, STDAT→CANCEL_DATE |
| NBEW – תזוזת מקרים | `sap.NBEW` | `PATCASEID, MOVEMNT_SEQNO` | FALNR→PATCASEID, LFDNR→MOVEMNT_SEQNO, BWIDT→MOVEMNT_DATE, BWIZT→MOVEMNT_TIME, BEWTY→MOVEMNT_CTGRY, UNFNR→ACCIDENT_NO, ORGFA→DEPARTMENT, ORGPF→NURS_TREAT_OU, STDAT→CANCEL_DATE, STUSR→CANCEL_USER |
| ZNACCIDENT – אירועים | `sap.ZNACCIDENT` | `ACCIDENT_NO` | UNFNR→ACCIDENT_NO, UNFKZ→ACCIDENT, ZNMCI→MCI, ZNUNFDT2→ACCIDENT_DATE, ZUNFZT2→ACCIDENT_TIME, ZNENDDATE→END_DATE, ZNENDTIME→END_TIME |
| ZNMCICASE – מקרים באירוע | `sap.ZNMCICASE` | `ACCIDENT_NO, PATCASEID` | UNFNR→ACCIDENT_NO, FALNR→PATCASEID, ZNMAININJURY→MAIN_INJURY, ZNMEDSTATUS→MEDICAL_STATUS |

Types: CHAR/NUMC → `NVARCHAR(n)` (Hebrew; NUMC keeps leading zeros), DATS → `DATE`, TIMS → `TIME(0)`; empty SAP
dates (`00000000`) are stored as `NULL`. No foreign keys, so tables can be synced in any order or partially.

Endpoints:

| Endpoint | Description |
| --- | --- |
| `GET /api/sapsync/tables` | Configured table names. |
| `POST /api/sapsync/tables/{name}/sync` | Sync one table page by page (`PageSize` rows per RFC call / MERGE transaction). Optional body: `{ "filterCondition": "ERDAT >= '20260101'", "maxRows": 0, "pageSize": 0 }`. Returns `TableSyncResult` (pages, rowsRead, rowsProcessed, elapsedMs, error, per-page `SyncResult`s); `404` for unknown names. |
| `POST /api/sapsync/tables/sync-all` | Sync all configured tables; `200` if all succeed, otherwise `207` with per-table results. |

Notes / assumptions to verify against your SAP system:

* **Keys** – in standard IS-H, NFAL and NBEW are also keyed by `EINRI` (institution). With a single institution the
  keys above are unique; otherwise add `"EINRI": "EINRI"` to `Fields`, the column to the script and to `PrimaryKeyColumns`.
  The ZNMCICASE key (`UNFNR + FALNR`) is assumed – adjust if the Z-table's key differs.
* **`ZUNFZT2`** (event time in ZNACCIDENT) is used exactly as listed; if the field is actually `ZNUNFZT2`, change the
  key in `SyncTables:Tables:ZNACCIDENT:Fields`.
* **Large tables** – `RFC_READ_TABLE` re-reads skipped rows for each page, so for regular runs prefer a delta filter
  (e.g. `ERDAT >= '20260101'` on NFAL, `BWIDT >= ...` on NBEW) over full reloads.

## How it works

**SAP destination**: `SapDestinationManager` is a singleton that implements `IDestinationConfiguration`. It calls
`RfcDestinationManager.RegisterDestinationConfiguration` exactly once per process, using double-checked locking.
The pooling parameters are passed through to NCo.

**Extraction**: `SapTableReader` calls `RFC_READ_TABLE` and works as follows:
- It uses the ASCII Unit Separator (`\u001F`) as `DELIMITER`, so `|`, `,` and tab characters in text never break parsing.
- It splits `FilterCondition` into lines of at most 72 characters for the `OPTIONS` table. Lines break only between tokens, and quoted literals stay intact.
- It loads rows into a typed `DataTable` between `BeginLoadData()` and `EndLoadData()`.

**Upsert**: `SqlBulkWriter` runs these steps, all on one connection:
1. Resolves the target table through `OBJECT_ID` and `sys.columns`. This rejects unknown tables and columns, and every identifier is bracket-quoted.
2. Clones the column definitions into `#TempStaging_{Guid}` with `SELECT TOP 0 <cols> INTO`. It clones only the synchronized columns, which avoids problems with identity, rowversion and defaulted columns.
3. Streams the rows into the temp table with `SqlBulkCopy` (`SqlBulkCopyOptions.Default`, configured batch size).
4. Runs a single `MERGE ... WITH (HOLDLOCK)` inside a transaction, matching on the composite key. The source is de-duplicated per key with `ROW_NUMBER()`, so duplicate SAP rows can never create duplicates or primary key violations. Rows are updated only when a value actually changed. The comparison is a NULL-safe `EXCEPT`, and text columns are compared with a binary collation so case-only changes are detected. NULL key values, such as initial SAP dates, are rejected.
5. Commits, then drops the temp table.

**Lifetimes**: `ISapDestinationManager` is a singleton. `ISapTableReader`, `ISqlBulkWriter` and `ISapSyncOrchestrator` are scoped.

**Notes**:
- `RFC_READ_TABLE` returns at most 512 characters per row (`DATA_BUFFER_EXCEEDED` otherwise). Request fewer `fields` for wide tables.
- The whole result is held in memory. Page large tables with `rowSkip` and `rowCount`.
- **Security:** the endpoint has no authentication. Add authentication/authorization (e.g. `[Authorize]` with your identity provider) before exposing it. Grant the SQL login access only to the tables it should synchronize, and the SAP RFC user only to the tables it may read.
