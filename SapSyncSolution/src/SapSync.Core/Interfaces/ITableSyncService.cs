using SapSync.Core.Models;

namespace SapSync.Core.Interfaces;

/// <summary>Synchronizes the tables configured in the "SyncTables" catalog.</summary>
public interface ITableSyncService
{
    IReadOnlyCollection<string> TableNames { get; }

    bool IsConfigured(string name);

    Task<TableSyncResult> SyncTableAsync(string name, TableSyncRequest? overrides = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TableSyncResult>> SyncAllAsync(CancellationToken cancellationToken = default);
}
