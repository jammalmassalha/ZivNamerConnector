using SapSync.Core.Models;

namespace SapSync.Core.Interfaces;

public interface ISapSyncOrchestrator
{
    Task<SyncResult> SyncAsync(SapQueryRequest request, CancellationToken cancellationToken = default);
}
