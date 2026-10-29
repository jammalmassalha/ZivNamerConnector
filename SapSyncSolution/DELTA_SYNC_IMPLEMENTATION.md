# Dual-Date Incremental Delta Sync Implementation

## Mission Overview

Implement incremental (delta) table synchronization across SAP IS-H and Z tables using dual-date filtering:
- **Filter Logic**: `(InsertDateField >= fromDate OR UpdateDateField >= fromDate)`
- **Default Behavior**: Syncs records created or modified within the last 24 hours (yesterday or later)
- **Date Format**: SAP `DATS(8)` format (`YYYYMMDD`)
- **RFC Compliance**: All filters respect the 72-character line limit for SAP `RFC_READ_TABLE` OPTIONS table

---

## 1. Target Tables & Date Field Mapping

| Table | SAP Name | Insert Date Field | Update Date Field | Special Filter | Notes |
|-------|----------|-------------------|-------------------|-----------------|-------|
| **NPAT** | Patient Master | `ERDAT` | `UPDAT` | — | Standard SAP audit fields |
| **NFAL** | Case Master | `ERDAT` | `UPDAT` | — | Standard SAP audit fields |
| **NBEW** | Movement Master | `BWIDT` | `BWEDT` | `STORN = ' '` | Business-specific date fields + cancellation flag |
| **N_FALL_BEW** | Case & Movement | `BWIDT` | `BWEDT` | `STORN = ' ' AND FSTORN = ' '` | Composite view with dual cancellation checks |
| **ZNACCIDENT** | Custom Accident | `ERDAT` | `UPDAT` | — | Standard custom table audit fields |
| **ZNMCICASE** | Custom MCI Case | `ERDAT` | `UPDAT` | — | Standard custom table audit fields |

---

## 2. Configuration Schema

### SyncTableDefinition Model Enhancement

Updated `/src/SapSync.Core/Models/SyncTablesSettings.cs`:

```csharp
public sealed class SyncTableDefinition
{
    /// <summary>SAP table name. Defaults to the catalog key.</summary>
    public string? SapTableName { get; set; }

    /// <summary>Target SQL Server table, e.g. "sap.NPAT".</summary>
    [Required]
    public string TargetDbTable { get; set; } = string.Empty;

    /// <summary>SQL key columns (after renaming) used to match rows.</summary>
    [MinLength(1)]
    public List<string> PrimaryKeyColumns { get; set; } = new();

    /// <summary>SAP field → SQL column. Only these fields are read from SAP.</summary>
    [MinLength(1)]
    public Dictionary<string, string> Fields { get; set; } = new();

    /// <summary>Default ABAP WHERE clause (may be overridden per request).</summary>
    public string? FilterCondition { get; set; }

    /// <summary>SAP field name for creation/insertion date (e.g., "ERDAT", "BWIDT").</summary>
    public string? InsertDateField { get; set; }

    /// <summary>SAP field name for update/modification date (e.g., "UPDAT", "BWEDT").</summary>
    public string? UpdateDateField { get; set; }

    /// <summary>Rows read from SAP and upserted per round-trip.</summary>
    [Range(1, 1_000_000)]
    public int PageSize { get; set; } = 50_000;
}
```

### TableSyncRequest Model Enhancement

Updated `/src/SapSync.Core/Models/TableSyncRequest.cs`:

```csharp
public sealed class TableSyncRequest
{
    /// <summary>ABAP WHERE clause replacing the configured one.</summary>
    [MaxLength(8000)]
    public string? FilterCondition { get; set; }

    /// <summary>Stop after this many rows (0 = whole table).</summary>
    [Range(0, int.MaxValue)]
    public int MaxRows { get; set; }

    /// <summary>Overrides the configured page size.</summary>
    [Range(0, 1_000_000)]
    public int PageSize { get; set; }

    /// <summary>
    /// Number of days back for delta sync (default 1 = yesterday).
    /// If set, overrides FilterCondition with: (InsertDate >= date-N OR UpdateDate >= date-N)
    /// Applies only if table has both InsertDateField and UpdateDateField configured.
    /// </summary>
    [Range(-365, 365)]
    public int? LookbackDays { get; set; }
}
```

### appsettings.json Configuration

All six target tables now include `InsertDateField` and `UpdateDateField`:

```json
{
  "SyncTables": {
    "Tables": {
      "NPAT": {
        "TargetDbTable": "dbo.NPAT",
        "PrimaryKeyColumns": ["PATIENTID"],
        "PageSize": 50000,
        "InsertDateField": "ERDAT",
        "UpdateDateField": "UPDAT",
        "Fields": { /* ... */ }
      },
      "NFAL": {
        "TargetDbTable": "dbo.NFAL",
        "PrimaryKeyColumns": ["PATCASEID"],
        "PageSize": 50000,
        "InsertDateField": "ERDAT",
        "UpdateDateField": "UPDAT",
        "Fields": { /* ... */ }
      },
      "NBEW": {
        "TargetDbTable": "dbo.NBEW",
        "PrimaryKeyColumns": ["PATCASEID", "MOVEMNT_SEQNO"],
        "PageSize": 50000,
        "InsertDateField": "BWIDT",
        "UpdateDateField": "BWEDT",
        "FilterCondition": "STORN = ' '",
        "Fields": { /* ... */ }
      },
      "N_FALL_BEW": {
        "TargetDbTable": "sap.N_FALL_BEW",
        "PrimaryKeyColumns": ["MANDT", "FALNR", "LFDBEW"],
        "PageSize": 5000,
        "InsertDateField": "BWIDT",
        "UpdateDateField": "BWEDT",
        "FilterCondition": "STORN = ' ' AND FSTORN = ' '",
        "Fields": { /* ... */ }
      },
      "ZNACCIDENT": {
        "TargetDbTable": "dbo.ZNACCIDENT",
        "PrimaryKeyColumns": ["ACCIDENT_NO"],
        "PageSize": 50000,
        "InsertDateField": "ERDAT",
        "UpdateDateField": "UPDAT",
        "Fields": { /* ... */ }
      },
      "ZNMCICASE": {
        "TargetDbTable": "dbo.ZNMCICASE",
        "PrimaryKeyColumns": ["ACCIDENT_NO", "PATCASEID"],
        "PageSize": 50000,
        "InsertDateField": "ERDAT",
        "UpdateDateField": "UPDAT",
        "Fields": { /* ... */ }
      }
    }
  }
}
```

---

## 3. Core Services

### DeltaSyncFilterBuilder

**Location**: `/src/SapSync.Core/Services/DeltaSyncFilterBuilder.cs`

Generates ABAP WHERE clauses for incremental syncs with full RFC_READ_TABLE compliance.

```csharp
public sealed class DeltaSyncFilterBuilder
{
    /// <summary>
    /// Builds dual-date filter: (insertField >= date OR updateField >= date)
    /// Merges with existing filters using AND logic.
    /// </summary>
    public string? BuildDualDateFilter(
        SyncTableDefinition tableDefinition, 
        int lookbackDays = 1,
        string? existingFilter = null)
    
    /// <summary>Validates filter fits within 72-char RFC line limits.</summary>
    public bool ValidateFilterForRfc(string? filterCondition)
    
    /// <summary>Merges existing WHERE clause with dual-date filter.</summary>
    public string MergeFilters(string existingFilter, string dualDateFilter)
}
```

**Key Features**:
- Automatic date calculation: `DateTime.Today.AddDays(-lookbackDays).ToString("yyyyMMdd")`
- Supports negative/positive/zero `lookbackDays`
- Handles missing date fields gracefully
- Merges with existing filters while preserving cancellation flags
- Comprehensive logging at INFO and WARNING levels

### Enhanced TableSyncService

**Location**: `/src/SapSync.Core/Services/TableSyncService.cs`

Now includes delta sync support via `LookbackDays` parameter:

```csharp
public async Task<TableSyncResult> SyncTableAsync(
    string name, 
    TableSyncRequest? overrides = null, 
    CancellationToken cancellationToken = default)
```

**Resolution Logic** (in priority order):
1. If `overrides.LookbackDays` is set → use dual-date filter
2. Else if `overrides.FilterCondition` is set → use explicit filter
3. Else → use table's default `FilterCondition`

---

## 4. RFC_READ_TABLE Compliance

### 72-Character Line Limit

The existing `RfcOptionsBuilder` (unchanged) handles tokenization and line breaking:

```csharp
internal static class RfcOptionsBuilder
{
    public const int MaxLineLength = 72;
    
    /// <summary>Splits ABAP WHERE clause into RFC OPTIONS lines.</summary>
    public static IReadOnlyList<string> Split(string? filterCondition, int maxLineLength = MaxLineLength)
}
```

**Example Filter Breakdown**:

Input:
```
( ERDAT >= '20261027' OR UPDAT >= '20261027' ) AND STORN = ' '
```

Output (single line, within 72 chars):
```
"( ERDAT >= '20261027' OR UPDAT >= '20261027' ) AND STORN = ' '"  // 63 chars
```

Complex input:
```
STORN = ' ' AND FSTORN = ' ' AND ( BWIDT >= '20261027' OR BWEDT >= '20261027' )
```

Output (possibly split across lines):
```
"STORN = ' ' AND FSTORN = ' ' AND ( BWIDT >= '20261027' OR"  // 72 chars
"BWEDT >= '20261027' )"  // 24 chars
```

---

## 5. Usage Examples

### Programmatic: Sync Single Table with Delta (Last 24 Hours)

```csharp
var syncService = provider.GetRequiredService<ITableSyncService>();

var request = new TableSyncRequest { LookbackDays = 1 };
var result = await syncService.SyncTableAsync("NPAT", request);

Console.WriteLine($"Synced {result.RowsProcessed} rows in {result.ElapsedMs}ms");
```

### Programmatic: Sync with Backfill (Last 7 Days)

```csharp
var request = new TableSyncRequest { LookbackDays = 7 };
var result = await syncService.SyncTableAsync("N_FALL_BEW", request);
```

### Programmatic: Sync with Custom Filter

```csharp
var request = new TableSyncRequest 
{ 
    FilterCondition = "( ERDAT >= '20261020' OR UPDAT >= '20261020' ) AND STORN = ' '"
};
var result = await syncService.SyncTableAsync("NBEW", request);
```

### REST API: Sync Single Table by Date

```bash
POST /api/sync/delta/table/NPAT?lookbackDays=1
Content-Type: application/json
Authorization: Bearer {token}

# Response
{
  "name": "NPAT",
  "success": true,
  "pages": 3,
  "rowsRead": 125000,
  "rowsProcessed": 125000,
  "elapsedMs": 8500
}
```

### REST API: Sync All Tables with Delta

```bash
POST /api/sync/delta/all-tables?lookbackDays=1
```

---

## 6. Filter Generation Examples

### NPAT (Patient Master) - Yesterday or Later

**Generated Filter** (1 day back):
```
( ERDAT >= '20261027' OR UPDAT >= '20261027' )
```

### NFAL (Case Master) - Last 7 Days

**Generated Filter** (7 days back):
```
( ERDAT >= '20261021' OR UPDAT >= '20261021' )
```

### NBEW (Movement) - Last 24 Hours + Cancellation Check

**Existing Filter**:
```
STORN = ' '
```

**Generated Dual-Date Filter** (1 day back):
```
( BWIDT >= '20261027' OR BWEDT >= '20261027' )
```

**Merged Result**:
```
STORN = ' ' AND ( BWIDT >= '20261027' OR BWEDT >= '20261027' )
```

### N_FALL_BEW (Case & Movement View) - Complex Merge

**Existing Filter**:
```
STORN = ' ' AND FSTORN = ' '
```

**Generated Dual-Date Filter** (1 day back):
```
( BWIDT >= '20261027' OR BWEDT >= '20261027' )
```

**Merged Result**:
```
STORN = ' ' AND FSTORN = ' ' AND ( BWIDT >= '20261027' OR BWEDT >= '20261027' )
```

---

## 7. Unit Tests

### DeltaSyncFilterBuilderTests (`tests/SapSync.Tests/DeltaSyncFilterBuilderTests.cs`)

- ✅ Dual-date filter generation with default parameters
- ✅ Lookback day variations (0, 1, 7, 30, 365, -30)
- ✅ SAP date format validation (YYYYMMDD)
- ✅ Missing date field handling
- ✅ Filter merging with AND logic
- ✅ Complex existing filter preservation
- ✅ RFC line limit validation
- ✅ Logging verification

**Run Tests**:
```bash
cd SapSyncSolution/tests/SapSync.Tests
dotnet test --filter DeltaSyncFilterBuilderTests
```

### RfcOptionsBuilderTests Enhancements (`tests/SapSync.Tests/RfcOptionsBuilderTests.cs`)

- ✅ Dual-date filter RFC line compliance
- ✅ Dual-date + cancellation flag tests
- ✅ Complex multi-flag filter validation
- ✅ Date field preservation
- ✅ Empty date handling ('00000000')
- ✅ Parentheses and boolean operator preservation

**Run Tests**:
```bash
cd SapSyncSolution/tests/SapSync.Tests
dotnet test --filter RfcOptionsBuilderTests
```

---

## 8. MSSQL Target Table Indexes

Recommended nonclustered indexes for fast delta queries:

```sql
-- NPAT indexes
CREATE NONCLUSTERED INDEX IX_NPAT_InsertUpdate_Dates
ON dbo.NPAT(ERDAT, UPDAT)
INCLUDE (PATIENTID);

-- NFAL indexes
CREATE NONCLUSTERED INDEX IX_NFAL_InsertUpdate_Dates
ON dbo.NFAL(ERDAT, UPDAT)
INCLUDE (PATCASEID);

-- NBEW indexes
CREATE NONCLUSTERED INDEX IX_NBEW_MovementDates_Storn
ON dbo.NBEW(BWIDT, BWEDT)
INCLUDE (PATCASEID, MOVEMNT_SEQNO)
WHERE STORN = ' ';

-- N_FALL_BEW indexes
CREATE NONCLUSTERED INDEX IX_NFALLBEW_MovementDates_Flags
ON sap.N_FALL_BEW(BWIDT, BWEDT)
INCLUDE (FALNR, LFDBEW)
WHERE STORN = ' ' AND FSTORN = ' ';

-- ZNACCIDENT indexes
CREATE NONCLUSTERED INDEX IX_ZNACCIDENT_InsertUpdate_Dates
ON dbo.ZNACCIDENT(ERDAT, UPDAT)
INCLUDE (ACCIDENT_NO);

-- ZNMCICASE indexes
CREATE NONCLUSTERED INDEX IX_ZNMCICASE_InsertUpdate_Dates
ON dbo.ZNMCICASE(ERDAT, UPDAT)
INCLUDE (ACCIDENT_NO, PATCASEID);
```

---

## 9. Date Format Specification

### SAP DATS(8) Format

- **Format**: `YYYYMMDD`
- **Example**: October 27, 2026 = `20261027`
- **Empty/Unset**: `00000000` or space (' ')
- **Timezone**: Uses server's local timezone via `DateTime.Now` (not UTC)

### Lookback Days Parameter

| Value | Range | Example Date (as of Oct 29, 2026) |
|-------|-------|----------------------------------|
| `-7` | 7 days in the future | `20261105` |
| `-1` | Tomorrow | `20261030` |
| `0` | Today | `20261029` |
| `1` | Yesterday | `20261028` |
| `7` | Last 7 days | `20261022` |
| `30` | Last 30 days | `20260929` |
| `365` | Last year | `20251029` |

---

## 10. Error Handling & Logging

### Log Levels

- **INFO**: Filter generation, merges, successful syncs
- **WARNING**: Missing date fields, RFC validation issues
- **ERROR**: Sync failures, unexpected exceptions

### Example Log Output

```
[INF] Built dual-date filter for NPAT: lookback=1 days (date=20261027). Clause: ( ERDAT >= '20261027' OR UPDAT >= '20261027' )
[INF] Merged dual-date filter with existing filter for N_FALL_BEW: STORN = ' ' AND FSTORN = ' ' AND ( BWIDT >= '20261027' OR BWEDT >= '20261027' )
[INF] Starting sync for table NPAT with filter: ( ERDAT >= '20261027' OR UPDAT >= '20261027' )
[INF] Table sync NPAT: 3 pages, 125000 rows read, 125000 rows inserted/updated in 8500 ms (success: True)
```

---

## 11. Backward Compatibility

✅ **Existing Functionality Preserved**:
- Tables without `InsertDateField`/`UpdateDateField` still work
- Existing `FilterCondition` in config respected if no `LookbackDays`
- `RfcOptionsBuilder` unchanged (100% backward compatible)
- All existing tests pass

✅ **New Functionality Opt-In**:
- `LookbackDays` is optional; default is full table sync
- Date fields are optional per table
- Dual-date sync only activates when explicitly requested

---

## 12. Performance Considerations

1. **SAP Filtering**: Date filters evaluated at RFC layer (efficient at source)
2. **Indexing**: Recommended SQL indexes on date columns speed up subsequent MERGE operations
3. **Pagination**: Dual-date filters work seamlessly with existing ROWSKIPS/ROWCOUNT pagination
4. **Memory**: No in-memory filtering; all data reduction happens in SAP

---

## 13. Migration / Deployment Checklist

- [ ] Deploy updated code (DeltaSyncFilterBuilder, TableSyncService)
- [ ] Update `appsettings.json` with InsertDateField/UpdateDateField for all 6 tables
- [ ] Create recommended SQL indexes in target database
- [ ] Run unit tests locally: `dotnet test --filter "DeltaSyncFilterBuilder|RfcOptionsBuilder"`
- [ ] Deploy to Dev environment and test one table's delta sync
- [ ] Verify generated filters in SAP RFC logs (max 72 chars per line)
- [ ] Deploy to Staging; test with historical backfill (LookbackDays=7)
- [ ] Deploy to Production with day-1 delta (LookbackDays=1)
- [ ] Monitor logs for INFO/WARNING messages; verify row counts expected

---

## 14. Troubleshooting

### Filter exceeds 72 characters
- Simplify existing filter (break complex ORs into separate syncs)
- Use table default FilterCondition; don't merge with custom complex filters

### No rows returned from delta sync
- Verify date fields actually exist in SAP table
- Check SAP dates in source table (may all be '00000000' if new table)
- Manually query SAP with same filter using SE16N or RFC_READ_TABLE

### Different row counts vs. full sync
- Expected: Delta returns subset; full sync returns all
- Verify lookbackDays calculation: `DateTime.Today.AddDays(-N)`
- Check timestamp in SAP; may have timezone offset

---

## References

- [SAP RFC_READ_TABLE Documentation](https://help.sap.com/doc/SAPSCM/latest/en-US/8a/ba00cbf5554ccbe10000000a174cb4/content.htm)
- [SAP DATS Data Type](https://help.sap.com/doc/ABAP2.71/en-US/ABAP_DATE_TYPES_EN.PDF)
- SapSync.Core/Services/DeltaSyncFilterBuilder.cs (implementation)
- tests/SapSync.Tests/DeltaSyncFilterBuilderTests.cs (test coverage)
