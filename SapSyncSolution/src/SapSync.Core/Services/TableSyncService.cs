using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Core.Services;

/// <summary>
/// Syncs configured tables page by page (RFC_READ_TABLE ROWSKIPS / ROWCOUNT) so large tables are never held in memory
/// at once. Each page is upserted in its own transaction.
/// </summary>
public sealed class TableSyncService : ITableSyncService
{
    private readonly ISapSyncOrchestrator _orchestrator;
    private readonly ILogger<TableSyncService> _logger;
    private readonly Dictionary<string, SyncTableDefinition> _tables;

    public TableSyncService(ISapSyncOrchestrator orchestrator, IOptions<SyncTablesSettings> settings, ILogger<TableSyncService> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tables = new Dictionary<string, SyncTableDefinition>(
            settings?.Value?.Tables ?? new Dictionary<string, SyncTableDefinition>(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> TableNames => _tables.Keys;

    public bool IsConfigured(string name) => !string.IsNullOrWhiteSpace(name) && _tables.ContainsKey(name);

    public async Task<TableSyncResult> SyncTableAsync(string name, TableSyncRequest? overrides = null, CancellationToken cancellationToken = default)
    {
        if (!_tables.TryGetValue(name ?? string.Empty, out var definition))
        {
            throw new KeyNotFoundException($"Table '{name}' is not configured in {SyncTablesSettings.SectionName}.");
        }

        var tableName = _tables.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        var pageSize = overrides?.PageSize > 0 ? overrides.PageSize : definition.PageSize;
        var maxRows = overrides?.MaxRows ?? 0;
        var filter = string.IsNullOrWhiteSpace(overrides?.FilterCondition) ? definition.FilterCondition : overrides!.FilterCondition;

        var watch = Stopwatch.StartNew();
        var pages = new List<SyncResult>();
        var rowsRead = 0;
        var rowsProcessed = 0;
        SyncResult? failed = null;

        while (true)
        {
            var count = maxRows > 0 ? Math.Min(pageSize, maxRows - rowsRead) : pageSize;
            var request = new SapQueryRequest
            {
                TableName = string.IsNullOrWhiteSpace(definition.SapTableName) ? tableName : definition.SapTableName,
                TargetDbTable = definition.TargetDbTable,
                PrimaryKeyColumns = definition.PrimaryKeyColumns.ToList(),
                Fields = definition.Fields.Keys.ToList(),
                ColumnMappings = new Dictionary<string, string>(definition.Fields, StringComparer.OrdinalIgnoreCase),
               // FilterCondition = filter,
                RowSkip = rowsRead,
                RowCount = count,
            };

            var page = await _orchestrator.SyncAsync(request, cancellationToken).ConfigureAwait(false);
            pages.Add(page);
            rowsRead += page.RowsRead;
            rowsProcessed += page.RowsProcessed;

            if (!page.Success)
            {
                failed = page;
                break;
            }

            if (page.RowsRead < count || (maxRows > 0 && rowsRead >= maxRows))
            {
                break;
            }
        }

        watch.Stop();
        _logger.LogInformation(
            "Table sync {SyncTable}: {Pages} pages, {RowsRead} rows read, {RowsProcessed} rows inserted/updated in {ElapsedMs} ms (success: {Success})",
            tableName, pages.Count, rowsRead, rowsProcessed, watch.ElapsedMilliseconds, failed is null);

        return new TableSyncResult
        {
            Name = tableName,
            Success = failed is null,
            Pages = pages.Count,
            RowsRead = rowsRead,
            RowsProcessed = rowsProcessed,
            ElapsedMs = watch.ElapsedMilliseconds,
            ErrorMessage = failed?.ErrorMessage,
            ErrorType = failed?.ErrorType,
            PageResults = pages,
        };
    }

    public async Task<IReadOnlyList<TableSyncResult>> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<TableSyncResult>(_tables.Count);
        foreach (var name in _tables.Keys)
        {
            // A failing table does not stop the others; each result reports its own status.
            results.Add(await SyncTableAsync(name, null, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
