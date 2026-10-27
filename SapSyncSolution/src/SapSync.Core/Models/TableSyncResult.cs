namespace SapSync.Core.Models;

/// <summary>Aggregated result of a paged sync of one configured table.</summary>
public sealed class TableSyncResult
{
    public string Name { get; init; } = string.Empty;

    public bool Success { get; init; }

    public int Pages { get; init; }

    public int RowsRead { get; init; }

    public int RowsProcessed { get; init; }

    public long ElapsedMs { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorType { get; init; }

    /// <summary>Per-page details.</summary>
    public IReadOnlyList<SyncResult> PageResults { get; init; } = Array.Empty<SyncResult>();
}
