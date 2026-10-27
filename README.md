# ZivNamerConnector – SAP → SQL Server Sync API

ASP.NET Core Web API (.NET 8 LTS) that reads SAP ERP tables through RFC (`RFC_READ_TABLE`, SAP NCo 3.1) and
upserts them into Microsoft SQL Server using a duplicate-free staging + `MERGE` pattern. CORS is preconfigured
for an Angular frontend on `http://localhost:4200`.

## Solution layout

```text
SapSyncSolution/
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
│   └── SapSync.Api/                 # SapSyncController, Program.cs (DI, CORS), appsettings.json
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

## Configuration (`appsettings.json`)

| Section | Purpose |
| --- | --- |
| `ConnectionStrings:SqlServer` | Target database (name configurable via `SqlSettings:ConnectionStringName`). |
| `SapSettings` | Logon: `AppServerHost` **or** `MessageServerHost` + `LogonGroup` + `SystemId`, plus `SystemNumber`, `Client`, `User`, `Password` (use user-secrets / env var `SapSettings__Password`), `Language`, `SapRouter`. Pooling: `PoolSize`, `PeakConnectionsLimit`, `ConnectionIdleTimeout` (seconds). `ConvertAbapTypes` (default `true`) maps D/T/P/I/F to `date`/`time`/`decimal`/`int`/`float`; set `false` if the target stores SAP values as text. |
| `SqlSettings` | `BulkCopyBatchSize` (5,000–10,000), `BulkCopyTimeoutSeconds`, `CommandTimeoutSeconds`. |
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
4. Runs a single `MERGE ... WITH (HOLDLOCK)` inside a transaction, matching on the composite key. The source is de-duplicated per key with `ROW_NUMBER()`, so duplicate SAP rows can never create duplicates or primary key violations. Rows are updated only when a value actually changed (NULL-safe `EXCEPT`).
5. Commits, then drops the temp table.

**Lifetimes**: `ISapDestinationManager` is a singleton. `ISapTableReader`, `ISqlBulkWriter` and `ISapSyncOrchestrator` are scoped.

**Notes**:
- `RFC_READ_TABLE` returns at most 512 characters per row (`DATA_BUFFER_EXCEEDED` otherwise). Request fewer `fields` for wide tables.
- The whole result is held in memory. Page large tables with `rowSkip` and `rowCount`.
