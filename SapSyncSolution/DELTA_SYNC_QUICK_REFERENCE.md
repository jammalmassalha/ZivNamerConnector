# Delta Sync Quick Reference

## Quick Start

### Sync Single Table (Last 24 Hours)
```csharp
var request = new TableSyncRequest { LookbackDays = 1 };
var result = await syncService.SyncTableAsync("NPAT", request);
```

### Sync All Tables (Last 24 Hours)
```csharp
var request = new TableSyncRequest { LookbackDays = 1 };
await syncService.SyncAllAsync(); // Uses table defaults, then apply request
```

### Sync with Backfill (7 Days)
```csharp
var request = new TableSyncRequest { LookbackDays = 7 };
var result = await syncService.SyncTableAsync("N_FALL_BEW", request);
```

## Generated Filters

### NPAT (1 day back)
```
( ERDAT >= '20261027' OR UPDAT >= '20261027' )
```

### NBEW + Cancellation (1 day back)
```
STORN = ' ' AND ( BWIDT >= '20261027' OR BWEDT >= '20261027' )
```

### N_FALL_BEW + Dual Cancellation (1 day back)
```
STORN = ' ' AND FSTORN = ' ' AND ( BWIDT >= '20261027' OR BWEDT >= '20261027' )
```

## Key Classes

| Class | Purpose |
|-------|---------|
| `DeltaSyncFilterBuilder` | Builds dual-date WHERE clauses |
| `TableSyncService` | Orchestrates delta syncs |
| `TableSyncRequest` | Request with `LookbackDays` parameter |
| `SyncTableDefinition` | Config with date field names |

## Configuration

All tables in `appsettings.json` include:
- `InsertDateField` (e.g., "ERDAT", "BWIDT")
- `UpdateDateField` (e.g., "UPDAT", "BWEDT")

## Date Format

- **SAP Format**: `YYYYMMDD` (e.g., 20261027 = Oct 27, 2026)
- **Calculation**: `DateTime.Today.AddDays(-lookbackDays)`
- **LookbackDays**:
  - `0` = Today
  - `1` = Yesterday (default)
  - `7` = Last 7 days
  - `-1` = Tomorrow

## Testing

```bash
# Run delta sync tests
dotnet test --filter DeltaSyncFilterBuilderTests

# Run RFC line limit tests
dotnet test --filter RfcOptionsBuilderTests
```

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| No rows returned | Delta window too narrow | Increase `LookbackDays` |
| Filter too long | Existing + date filter exceeds 72 chars | Simplify existing filter |
| Wrong dates | Timezone issue | Check server timezone |

## Performance

- ✅ SAP filters at RFC layer (efficient)
- ✅ Pagination works with delta filters
- ✅ Recommended SQL indexes on date columns
- ✅ No in-memory filtering
