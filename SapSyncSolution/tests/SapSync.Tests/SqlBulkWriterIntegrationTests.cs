using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SapSync.Core.Models;
using SapSync.Infrastructure.Persistence;

namespace SapSync.Tests;

/// <summary>Runs only when SAPSYNC_TEST_SQL_CONNECTION points to a SQL Server instance (tables are created/dropped).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "SAPSYNC_TEST_SQL_CONNECTION";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to run SQL Server integration tests.";
        }
    }
}

public sealed class SqlBulkWriterIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable) ?? string.Empty;
    private readonly string _table = $"SapSyncTest_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (_connectionString.Length == 0)
        {
            return;
        }

        await ExecuteAsync($"""
            CREATE TABLE dbo.[{_table}] (
                Id INT IDENTITY(1,1) NOT NULL,
                MATNR NVARCHAR(18) NOT NULL,
                WERKS NVARCHAR(4) NOT NULL,
                MAKTX NVARCHAR(40) NULL,
                ERSDA DATE NULL,
                LABST DECIMAL(13,3) NULL,
                LoadedAt DATETIME2 NOT NULL CONSTRAINT [DF_{_table}] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_{_table}] PRIMARY KEY (MATNR, WERKS));
            INSERT INTO dbo.[{_table}] (MATNR, WERKS, MAKTX, ERSDA, LABST) VALUES
                (N'M1', N'1000', N'Old', '2020-01-01', 1),
                (N'M2', N'1000', N'Unchanged', NULL, 2);
            """);
    }

    public async Task DisposeAsync()
    {
        if (_connectionString.Length > 0)
        {
            await ExecuteAsync($"DROP TABLE IF EXISTS dbo.[{_table}];");
        }
    }

    [SqlServerFact]
    public async Task BulkUpsertAsync_InsertsNewRows_UpdatesChangedRows_AndNeverDuplicates()
    {
        var writer = CreateWriter();
        var data = Source(
            ("M1", "1000", "New | text, with\tdelims", new DateTime(2024, 5, 1), 10.5m),
            ("M2", "1000", "Unchanged", null, 2m),
            ("M3", "2000", "Inserted", null, null),
            ("M3", "2000", "Inserted", null, null)); // duplicate key in the source

        var affected = await writer.BulkUpsertAsync(data, $"[dbo].[{_table}]", new[] { "matnr", "WERKS" });

        Assert.Equal(2, affected); // M1 updated, M3 inserted, M2 unchanged
        var rows = await ReadAllAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(("New | text, with\tdelims", (DateTime?)new DateTime(2024, 5, 1), (decimal?)10.5m), rows["M1"]);
        Assert.Equal(("Unchanged", (DateTime?)null, (decimal?)2m), rows["M2"]);

        // Re-running the same sync is idempotent.
        Assert.Equal(0, await writer.BulkUpsertAsync(data, $"dbo.{_table}", new[] { "MATNR", "WERKS" }));
        Assert.Equal(3, (await ReadAllAsync()).Count);
    }

    [SqlServerFact]
    public async Task BulkUpsertAsync_RejectsUnknownTargetsAndKeys()
    {
        var writer = CreateWriter();
        var data = Source(("M1", "1000", "x", null, null));

        await Assert.ThrowsAsync<ArgumentException>(() => writer.BulkUpsertAsync(data, "dbo.DoesNotExist; DROP TABLE x", new[] { "MATNR" }));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.BulkUpsertAsync(data, $"dbo.{_table}", new[] { "NOPE" }));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.BulkUpsertAsync(data, $"dbo.{_table}", new[] { "Id" }));
    }

    private SqlBulkWriter CreateWriter() => new(
        Options.Create(new SqlSettings { ConnectionString = _connectionString, BulkCopyBatchSize = 5000 }),
        NullLogger<SqlBulkWriter>.Instance);

    private static DataTable Source(params (string Matnr, string Werks, string Maktx, DateTime? Ersda, decimal? Labst)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("MATNR", typeof(string));
        table.Columns.Add("WERKS", typeof(string));
        table.Columns.Add("MAKTX", typeof(string));
        table.Columns.Add("ERSDA", typeof(DateTime));
        table.Columns.Add("LABST", typeof(decimal));
        table.Columns.Add("MANDT", typeof(string)); // not in target -> ignored
        foreach (var r in rows)
        {
            table.Rows.Add(r.Matnr, r.Werks, r.Maktx, (object?)r.Ersda ?? DBNull.Value, (object?)r.Labst ?? DBNull.Value, "100");
        }

        return table;
    }

    private async Task<Dictionary<string, (string?, DateTime?, decimal?)>> ReadAllAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT MATNR, MAKTX, ERSDA, LABST FROM dbo.[{_table}]", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<string, (string?, DateTime?, decimal?)>();
        while (await reader.ReadAsync())
        {
            result.Add(
                reader.GetString(0),
                (reader.IsDBNull(1) ? null : reader.GetString(1),
                 reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                 reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
        }

        return result;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
