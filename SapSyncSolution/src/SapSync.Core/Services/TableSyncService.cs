using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Core.Services;

/// <summary>
/// Syncs configured tables page by page (RFC_READ_TABLE ROWSKIPS / ROWCOUNT) so large tables are never held in memory
/// at once. Each page is upserted in its own transaction.
/// Supports incremental (delta) syncs using dual-date filtering (InsertDate >= date OR UpdateDate >= date).
/// </summary>
public sealed class TableSyncService : ITableSyncService
{
    private readonly ISapSyncOrchestrator _orchestrator;
    private readonly DeltaSyncFilterBuilder _filterBuilder;
    private readonly ILogger<TableSyncService> _logger;
    private readonly Dictionary<string, SyncTableDefinition> _tables;

    public TableSyncService(ISapSyncOrchestrator orchestrator, IOptions<SyncTablesSettings> settings, ILogger<TableSyncService> logger, ILoggerFactory loggerFactory)
        : this(orchestrator, new DeltaSyncFilterBuilder(loggerFactory.CreateLogger<DeltaSyncFilterBuilder>()), settings, logger)
    {
    }

    internal TableSyncService(ISapSyncOrchestrator orchestrator, DeltaSyncFilterBuilder filterBuilder, IOptions<SyncTablesSettings> settings, ILogger<TableSyncService> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _filterBuilder = filterBuilder ?? throw new ArgumentNullException(nameof(filterBuilder));
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
        
        // Resolve filter: LookbackDays takes precedence for dual-date delta syncs
        var filter = ResolveFilter(definition, overrides);

        _logger.LogInformation(
            "Starting sync for table {TableName} with filter: {Filter}",
            tableName, string.IsNullOrWhiteSpace(filter) ? "(none)" : filter);

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
                FilterCondition = filter,
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

    /// <summary>
    /// Resolves the effective filter condition based on overrides and table configuration.
    /// LookbackDays in the override triggers dual-date delta filtering.
    /// </summary>
    private string? ResolveFilter(SyncTableDefinition definition, TableSyncRequest? overrides)
    {
        // If LookbackDays is explicitly set, build dual-date filter
        if (overrides?.LookbackDays.HasValue == true)
        {
            var lookbackDays = overrides.LookbackDays.Value;
            var existingFilter = definition.FilterCondition;
            return _filterBuilder.BuildDualDateFilter(definition, lookbackDays, existingFilter);
        }

        // Otherwise, use explicit FilterCondition or table's default
        var filter = !string.IsNullOrWhiteSpace(overrides?.FilterCondition)
            ? overrides.FilterCondition
            : definition.FilterCondition;

        // Replace @FromDate token if present (for dual-date filters in config)
        if (!string.IsNullOrWhiteSpace(filter) && filter.Contains("@FromDate", StringComparison.Ordinal))
        {
            var fromDate = DateTime.Today.AddDays(-1).ToString("yyyyMMdd");
            filter = filter.Replace("@FromDate", fromDate, StringComparison.Ordinal);
            _logger.LogInformation("Resolved @FromDate token to {FromDate} in filter", fromDate);
        }

        return filter;
    }
}
