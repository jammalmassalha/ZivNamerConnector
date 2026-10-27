using System.ComponentModel.DataAnnotations;

namespace SapSync.Core.Models;

/// <summary>Describes which SAP table to read and which SQL Server table to upsert it into.</summary>
public sealed class SapQueryRequest
{
    /// <summary>SAP table name, e.g. "MARA" or "/BIC/AZSALES00".</summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9_/]{1,30}$", ErrorMessage = "TableName must be a valid SAP table name.")]
    public string TableName { get; set; } = string.Empty;

    /// <summary>Target SQL Server table, e.g. "dbo.Materials" or "[dbo].[Materials]".</summary>
    [Required]
    [MaxLength(300)]
    public string TargetDbTable { get; set; } = string.Empty;

    /// <summary>Columns forming the (composite) key used to match rows in the MERGE.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one primary key column is required.")]
    public List<string> PrimaryKeyColumns { get; set; } = new();

    /// <summary>Optional SAP field list. When empty, all fields of the table are read.</summary>
    public List<string>? Fields { get; set; }

    /// <summary>
    /// Optional SAP field → SQL column renames (e.g. "PATNR" → "PATIENTID"), applied before the upsert.
    /// <see cref="PrimaryKeyColumns"/> refer to the SQL column names.
    /// </summary>
    public Dictionary<string, string>? ColumnMappings { get; set; }

    /// <summary>Optional ABAP WHERE clause, e.g. "MTART = 'FERT' AND ERSDA >= '20240101'".</summary>
    [MaxLength(8000)]
    public string? FilterCondition { get; set; }

    /// <summary>Maximum number of rows to read (0 = all rows).</summary>
    [Range(0, int.MaxValue)]
    public int RowCount { get; set; }

    /// <summary>Number of rows to skip (for paging large tables).</summary>
    [Range(0, int.MaxValue)]
    public int RowSkip { get; set; }
}
