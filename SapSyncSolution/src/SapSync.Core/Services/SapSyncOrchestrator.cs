using System.Data;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Core.Services;

/// <summary>Coordinates SAP extraction and SQL Server bulk upsert, capturing timings and structured logs.</summary>
public sealed class SapSyncOrchestrator : ISapSyncOrchestrator
{
    private readonly ISapTableReader _reader;
    private readonly ISqlBulkWriter _writer;
    private readonly ILogger<SapSyncOrchestrator> _logger;

    public SapSyncOrchestrator(ISapTableReader reader, ISqlBulkWriter writer, ILogger<SapSyncOrchestrator> logger)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SyncResult> SyncAsync(SapQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Request values are written to logs: strip line breaks to prevent log forging.
        var sapTable = SanitizeForLog(request.TableName);
        var targetTable = SanitizeForLog(request.TargetDbTable);
        var startedAt = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        long sapElapsedMs = 0, sqlElapsedMs = 0;
        var rowsRead = 0;
        var stage = SyncErrorTypes.SapRead;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["SapTable"] = sapTable,
            ["TargetTable"] = targetTable,
        });

        try
        {
            _logger.LogInformation(
                "Starting SAP sync {SapTable} -> {TargetTable} (RowSkip={RowSkip}, RowCount={RowCount})",
                sapTable, targetTable, request.RowSkip, request.RowCount);

            // Validate mappings before calling SAP so configuration errors fail fast.
            ColumnMapper.Validate(request.ColumnMappings);

            var stepWatch = Stopwatch.StartNew();
            using DataTable data = await _reader.ReadTableAsync(request, cancellationToken).ConfigureAwait(false);
            sapElapsedMs = stepWatch.ElapsedMilliseconds;
            rowsRead = data.Rows.Count;

            _logger.LogInformation(
                "Read {RowsRead} rows / {ColumnCount} columns from SAP table {SapTable} in {SapElapsedMs} ms",
                rowsRead, data.Columns.Count, sapTable, sapElapsedMs);

            ColumnMapper.Apply(data, request.ColumnMappings);

            var rowsProcessed = 0;
            if (rowsRead > 0)
            {
                stage = SyncErrorTypes.SqlWrite;
                stepWatch.Restart();
                rowsProcessed = await _writer
                    .BulkUpsertAsync(data, request.TargetDbTable, request.PrimaryKeyColumns, cancellationToken)
                    .ConfigureAwait(false);
                sqlElapsedMs = stepWatch.ElapsedMilliseconds;

                _logger.LogInformation(
                    "Upserted {RowsProcessed} rows into {TargetTable} in {SqlElapsedMs} ms",
                    rowsProcessed, targetTable, sqlElapsedMs);
            }
            else
            {
                _logger.LogInformation("No rows returned from SAP table {SapTable}; SQL upsert skipped", sapTable);
            }

            total.Stop();
            _logger.LogInformation(
                "SAP sync {SapTable} -> {TargetTable} completed in {ElapsedMs} ms", sapTable, targetTable, total.ElapsedMilliseconds);

            return new SyncResult
            {
                Success = true,
                SapTableName = request.TableName,
                TargetDbTable = request.TargetDbTable,
                RowsRead = rowsRead,
                RowsProcessed = rowsProcessed,
                SapReadElapsedMs = sapElapsedMs,
                SqlWriteElapsedMs = sqlElapsedMs,
                ElapsedMs = total.ElapsedMilliseconds,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("SAP sync {SapTable} -> {TargetTable} was cancelled", sapTable, targetTable);
            throw;
        }
        catch (Exception ex)
        {
            total.Stop();
            var errorType = ex is ArgumentException ? SyncErrorTypes.Validation : stage;
            _logger.LogError(
                ex,
                "SAP sync {SapTable} -> {TargetTable} failed during {Stage} after {ElapsedMs} ms",
                sapTable, targetTable, errorType, total.ElapsedMilliseconds);

            return new SyncResult
            {
                Success = false,
                SapTableName = request.TableName,
                TargetDbTable = request.TargetDbTable,
                RowsRead = rowsRead,
                SapReadElapsedMs = sapElapsedMs,
                SqlWriteElapsedMs = sqlElapsedMs,
                ElapsedMs = total.ElapsedMilliseconds,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message,
                ErrorType = errorType,
            };
        }
    }

    private static string SanitizeForLog(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
}
