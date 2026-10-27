namespace SapSync.Core.Models;

/// <summary>Outcome and performance metrics of a single SAP → SQL Server synchronization.</summary>
public sealed class SyncResult
{
    public bool Success { get; init; }

    public string SapTableName { get; init; } = string.Empty;

    public string TargetDbTable { get; init; } = string.Empty;

    /// <summary>Rows extracted from SAP.</summary>
    public int RowsRead { get; init; }

    /// <summary>Rows inserted or updated by the MERGE (unchanged rows are not counted).</summary>
    public int RowsProcessed { get; init; }

    public long SapReadElapsedMs { get; init; }

    public long SqlWriteElapsedMs { get; init; }

    public long ElapsedMs { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorType { get; init; }
}
