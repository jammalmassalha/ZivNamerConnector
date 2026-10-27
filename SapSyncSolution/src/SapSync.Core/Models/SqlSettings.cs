using System.ComponentModel.DataAnnotations;

namespace SapSync.Core.Models;

/// <summary>Strongly typed SQL Server bulk upsert configuration (section "SqlSettings").</summary>
public sealed class SqlSettings
{
    public const string SectionName = "SqlSettings";

    /// <summary>Name of the entry in the "ConnectionStrings" section used for the target database.</summary>
    [Required]
    public string ConnectionStringName { get; set; } = "SqlServer";

    /// <summary>
    /// Resolved connection string. When not set explicitly it is taken from
    /// <c>ConnectionStrings:{ConnectionStringName}</c> at startup.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Rows per <c>SqlBulkCopy</c> round-trip.</summary>
    [Range(5000, 10000)]
    public int BulkCopyBatchSize { get; set; } = 5000;

    /// <summary>Timeout (seconds) for the bulk copy into the staging table. 0 = no limit.</summary>
    [Range(0, int.MaxValue)]
    public int BulkCopyTimeoutSeconds { get; set; } = 600;

    /// <summary>Timeout (seconds) for staging DDL and the MERGE statement. 0 = no limit.</summary>
    [Range(0, int.MaxValue)]
    public int CommandTimeoutSeconds { get; set; } = 600;
}
