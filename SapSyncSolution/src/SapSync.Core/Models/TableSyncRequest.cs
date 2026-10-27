using System.ComponentModel.DataAnnotations;

namespace SapSync.Core.Models;

/// <summary>Optional overrides when syncing a configured table.</summary>
public sealed class TableSyncRequest
{
    /// <summary>ABAP WHERE clause replacing the configured one, e.g. "ERDAT >= '20260101'" for a delta load.</summary>
    [MaxLength(8000)]
    public string? FilterCondition { get; set; }

    /// <summary>Stop after this many rows (0 = whole table).</summary>
    [Range(0, int.MaxValue)]
    public int MaxRows { get; set; }

    /// <summary>Overrides the configured page size.</summary>
    [Range(0, 1_000_000)]
    public int PageSize { get; set; }
}
