using System.ComponentModel.DataAnnotations;

namespace SapSync.Core.Models;

/// <summary>Catalog of predefined SAP → SQL Server table syncs (section "SyncTables"), keyed by a sync name.</summary>
public sealed class SyncTablesSettings
{
    public const string SectionName = "SyncTables";

    public Dictionary<string, SyncTableDefinition> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One SAP table and how it is stored in SQL Server.</summary>
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

    /// <summary>SAP field → SQL column. Only these fields are read from SAP. An empty value keeps the SAP name.</summary>
    [MinLength(1)]
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default ABAP WHERE clause (may be overridden per request).</summary>
    public string? FilterCondition { get; set; }

    /// <summary>SAP field name for creation/insertion date (e.g., "ERDAT", "BWIDT"). Used for incremental syncs.</summary>
    public string? InsertDateField { get; set; }

    /// <summary>SAP field name for update/modification date (e.g., "UPDAT", "BWEDT"). Used for incremental syncs.</summary>
    public string? UpdateDateField { get; set; }

    /// <summary>Rows read from SAP and upserted per round-trip.</summary>
    [Range(1, 1_000_000)]
    public int PageSize { get; set; } = 50_000;
}
