using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Infrastructure.Persistence;

/// <summary>
/// Duplicate-free bulk upsert: target schema is cloned into a session-scoped temp table, rows are streamed in with
/// <see cref="SqlBulkCopy"/>, then a single set-based MERGE (in a transaction) inserts new and updates changed rows.
/// </summary>
public sealed class SqlBulkWriter : ISqlBulkWriter
{
    // Types that cannot be compared with EXCEPT; change detection is disabled when one of them is synchronized.
    private static readonly HashSet<string> NonComparableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "xml", "text", "ntext", "image", "geography", "geometry",
    };

    private const string ResolveTargetSql = """
        SELECT o.object_id, s.name, o.name
        FROM sys.tables AS o
        INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        WHERE o.object_id = OBJECT_ID(@TableName, N'U');
        """;

    private const string TargetColumnsSql = """
        SELECT c.name, c.is_identity, c.is_computed, TYPE_NAME(c.system_type_id)
        FROM sys.columns AS c
        WHERE c.object_id = @ObjectId
        ORDER BY c.column_id;
        """;

    private readonly SqlSettings _settings;
    private readonly ILogger<SqlBulkWriter> _logger;

    public SqlBulkWriter(IOptions<SqlSettings> settings, ILogger<SqlBulkWriter> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            throw new InvalidOperationException("SqlSettings.ConnectionString is not configured.");
        }
    }

    public async Task<int> BulkUpsertAsync(
        DataTable data,
        string targetTable,
        IReadOnlyList<string> primaryKeyColumns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);
        ArgumentNullException.ThrowIfNull(primaryKeyColumns);
        if (primaryKeyColumns.Count == 0 || primaryKeyColumns.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty primary key column is required.", nameof(primaryKeyColumns));
        }

        if (data.Rows.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var target = await ResolveTargetAsync(connection, targetTable, cancellationToken).ConfigureAwait(false);
        var plan = BuildColumnPlan(data, target, primaryKeyColumns);

        // Generated name (no user input) – the '#' prefix makes it private to this connection/session.
        var quotedStaging = SqlIdentifier.Quote($"#TempStaging_{Guid.NewGuid():N}");
        var stagingColumnList = string.Join(", ", plan.Columns.Select(c => SqlIdentifier.Quote(c.TargetName)));

        try
        {
            // 1. Clone the target column definitions (types, lengths, collations) into an empty temp table.
            //    Only synchronized columns are cloned so identity/rowversion/defaulted columns cannot break the load.
            await ExecuteNonQueryAsync(
                connection,
                null,
                $"SELECT TOP 0 {stagingColumnList} INTO {quotedStaging} FROM {target.QuotedName};",
                cancellationToken).ConfigureAwait(false);

            // 2. Stream the in-memory rows into the staging table.
            using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, externalTransaction: null))
            {
                bulkCopy.DestinationTableName = quotedStaging;
                bulkCopy.BatchSize = _settings.BulkCopyBatchSize;
                bulkCopy.BulkCopyTimeout = _settings.BulkCopyTimeoutSeconds;
                foreach (var column in plan.Columns)
                {
                    bulkCopy.ColumnMappings.Add(column.SourceName, column.TargetName);
                }

                await bulkCopy.WriteToServerAsync(data, cancellationToken).ConfigureAwait(false);
            }

            // 3. Set-based MERGE inside a transaction.
            var mergeSql = MergeSqlBuilder.Build(
                target.QuotedName,
                quotedStaging,
                plan.Columns.Select(c => c.TargetName).ToList(),
                plan.KeyColumns,
                plan.DetectChanges);

            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await using var command = new SqlCommand(mergeSql, connection, transaction)
                {
                    CommandTimeout = _settings.CommandTimeoutSeconds,
                };
                var affected = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

                // 4. Commit.
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogDebug(
                    "MERGE into {TargetTable} affected {RowsAffected} of {RowCount} staged rows", target.QuotedName, affected, data.Rows.Count);
                return affected;
            }
            catch
            {
                await TryRollbackAsync(transaction).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            await TryDropStagingAsync(connection, quotedStaging).ConfigureAwait(false);
        }
    }

    private async Task<TargetTable> ResolveTargetAsync(SqlConnection connection, string targetTable, CancellationToken cancellationToken)
    {
        int objectId;
        string schemaName, tableName;

        await using (var command = new SqlCommand(ResolveTargetSql, connection) { CommandTimeout = _settings.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@TableName", SqlDbType.NVarChar, 776).Value = targetTable.Trim();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ArgumentException($"Target table '{targetTable}' does not exist or is not accessible.", nameof(targetTable));
            }

            objectId = reader.GetInt32(0);
            schemaName = reader.GetString(1);
            tableName = reader.GetString(2);
        }

        var columns = new List<TargetColumn>();
        await using (var command = new SqlCommand(TargetColumnsSql, connection) { CommandTimeout = _settings.CommandTimeoutSeconds })
        {
            command.Parameters.Add("@ObjectId", SqlDbType.Int).Value = objectId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                columns.Add(new TargetColumn(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3)));
            }
        }

        return new TargetTable($"{SqlIdentifier.Quote(schemaName)}.{SqlIdentifier.Quote(tableName)}", columns);
    }

    private ColumnPlan BuildColumnPlan(DataTable data, TargetTable target, IReadOnlyList<string> primaryKeyColumns)
    {
        var targetColumns = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var mapped = new List<MappedColumn>();
        var detectChanges = true;

        foreach (DataColumn sourceColumn in data.Columns)
        {
            if (!targetColumns.TryGetValue(sourceColumn.ColumnName, out var targetColumn))
            {
                _logger.LogWarning(
                    "Source column {Column} does not exist in {TargetTable} and is ignored", sourceColumn.ColumnName, target.QuotedName);
                continue;
            }

            if (!targetColumn.IsWritable)
            {
                _logger.LogWarning(
                    "Column {Column} of {TargetTable} is identity/computed/rowversion and is ignored", targetColumn.Name, target.QuotedName);
                continue;
            }

            if (NonComparableTypes.Contains(targetColumn.TypeName))
            {
                detectChanges = false;
            }

            mapped.Add(new MappedColumn(sourceColumn.ColumnName, targetColumn.Name));
        }

        var keyColumns = new List<string>(primaryKeyColumns.Count);
        foreach (var key in primaryKeyColumns.Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var match = mapped.FirstOrDefault(c => string.Equals(c.TargetName, key, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new ArgumentException(
                    $"Primary key column '{key}' must exist (and be writable) in both the SAP result and target table {target.QuotedName}.",
                    nameof(primaryKeyColumns));
            }

            keyColumns.Add(match.TargetName);
        }

        return new ColumnPlan(mapped, keyColumns, detectChanges);
    }

    private async Task ExecuteNonQueryAsync(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = _settings.CommandTimeoutSeconds };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task TryRollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqlException)
        {
            _logger.LogWarning(ex, "Rollback of MERGE transaction failed");
        }
    }

    private async Task TryDropStagingAsync(SqlConnection connection, string quotedStaging)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            await ExecuteNonQueryAsync(connection, null, $"DROP TABLE IF EXISTS {quotedStaging};", CancellationToken.None).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            // The temp table is dropped by SQL Server when the session is reset/closed anyway.
            _logger.LogDebug(ex, "Could not drop staging table {StagingTable}", quotedStaging);
        }
    }

    private sealed record TargetColumn(string Name, bool IsIdentity, bool IsComputed, string TypeName)
    {
        public bool IsWritable => !IsIdentity && !IsComputed
            && !string.Equals(TypeName, "timestamp", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TargetTable(string QuotedName, IReadOnlyList<TargetColumn> Columns);

    private sealed record MappedColumn(string SourceName, string TargetName);

    private sealed record ColumnPlan(IReadOnlyList<MappedColumn> Columns, IReadOnlyList<string> KeyColumns, bool DetectChanges);
}
