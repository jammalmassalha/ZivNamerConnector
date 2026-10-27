using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;
using SapSync.Core.Services;
using SapSync.Infrastructure.Persistence;

namespace SapSync.Tests;

/// <summary>
/// Runs database/01_CreateTables.sql against SQL Server and checks it matches the "SyncTables" catalog in the API's
/// appsettings.json, then syncs SAP-shaped rows through the real writer.
/// </summary>
public sealed class SapTablesSchemaIntegrationTests : IAsyncLifetime
{
    private static readonly string[] Tables = { "NPAT", "NFAL", "NBEW", "ZNACCIDENT", "ZNMCICASE" };
    private readonly string _connectionString = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable) ?? string.Empty;

    public async Task InitializeAsync()
    {
        if (_connectionString.Length == 0)
        {
            return;
        }

        await DropTablesAsync();
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "01_CreateTables.sql"));
        await RunScriptAsync(script);
        await RunScriptAsync(script); // idempotent
    }

    public async Task DisposeAsync()
    {
        if (_connectionString.Length > 0)
        {
            await DropTablesAsync();
        }
    }

    [Fact]
    public void Catalog_ContainsTheFiveTables()
    {
        Assert.Equal(Tables.OrderBy(t => t), LoadCatalog().Tables.Keys.OrderBy(t => t));
    }

    [SqlServerFact]
    public async Task Script_MatchesCatalogColumnsAndKeys()
    {
        foreach (var (name, table) in LoadCatalog().Tables)
        {
            var columns = await QueryListAsync(
                "SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(@t)", table.TargetDbTable);
            var keys = await QueryListAsync(
                """
                SELECT c.name FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID(@t)
                """, table.TargetDbTable);

            Assert.True(columns.Count > 0, $"{name}: table {table.TargetDbTable} missing");
            Assert.Equal(columns.OrderBy(c => c), table.Fields.Values.OrderBy(c => c));
            Assert.Equal(keys.OrderBy(c => c), table.PrimaryKeyColumns.OrderBy(c => c));
        }
    }

    [SqlServerFact]
    public async Task Sync_NpatAndNbew_FromSapShapedRows_UpsertsWithoutDuplicates()
    {
        var catalog = LoadCatalog();

        var npat = new DataTable();
        foreach (var field in catalog.Tables["NPAT"].Fields.Keys)
        {
            npat.Columns.Add(field, field is "GBDAT" or "TODDT" ? typeof(DateTime) : typeof(string));
        }

        npat.Rows.Add(NpatRow(npat, "0000000001", "ישראלי", new DateTime(1980, 2, 29)));
        npat.Rows.Add(NpatRow(npat, "0000000002", "כהן", null));

        var nbew = new DataTable();
        foreach (var field in catalog.Tables["NBEW"].Fields.Keys)
        {
            nbew.Columns.Add(field, field switch
            {
                "BWIDT" or "STDAT" => typeof(DateTime),
                "BWIZT" => typeof(TimeSpan),
                _ => typeof(string),
            });
        }

        var move = nbew.NewRow();
        move["FALNR"] = "0000123456";
        move["LFDNR"] = "00001";
        move["BWIDT"] = new DateTime(2026, 10, 1);
        move["BWIZT"] = new TimeSpan(13, 45, 10);
        move["BEWTY"] = "1";
        move["ORGFA"] = "ER";
        nbew.Rows.Add(move);

        var sources = new Queue<DataTable>(new[] { npat, npat.Copy(), nbew });
        var service = new TableSyncService(
            new SapSyncOrchestrator(new QueueReader(sources), CreateWriter(), NullLogger<SapSyncOrchestrator>.Instance),
            Options.Create(catalog),
            NullLogger<TableSyncService>.Instance);

        var first = await service.SyncTableAsync("NPAT");
        var second = await service.SyncTableAsync("NPAT");
        var movements = await service.SyncTableAsync("NBEW");

        Assert.True(first.Success, first.ErrorMessage);
        Assert.Equal(2, first.RowsProcessed);
        Assert.Equal(0, second.RowsProcessed);
        Assert.True(movements.Success, movements.ErrorMessage);
        Assert.Equal(new[] { "ישראלי|1980-02-29", "כהן|" }, await QueryListAsync(
            "SELECT LAST_NAME_PAT + N'|' + ISNULL(CONVERT(NVARCHAR(10), DOB, 23), N'') FROM sap.NPAT WHERE @t = @t ORDER BY PATIENTID", "x"));
        Assert.Equal(new[] { "00001|2026-10-01|13:45:10" }, await QueryListAsync(
            "SELECT MOVEMNT_SEQNO + N'|' + CONVERT(NVARCHAR(10), MOVEMNT_DATE, 23) + N'|' + CONVERT(NVARCHAR(8), MOVEMNT_TIME) FROM sap.NBEW WHERE @t = @t", "x"));
    }

    private static object[] NpatRow(DataTable table, string patnr, string lastName, DateTime? dob)
    {
        var row = table.NewRow();
        row["PATNR"] = patnr;
        row["EXTNR"] = "012345678";
        row["NNAME"] = lastName;
        row["VNAME"] = "דנה";
        row["GSCHL"] = "2";
        row["GBDAT"] = (object?)dob ?? DBNull.Value;
        row["ORT"] = "תל אביב";
        row["ZNKUPA_NOZZ"] = "כללית";
        return row.ItemArray!;
    }

    private static SyncTablesSettings LoadCatalog()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "api", "appsettings.json"))
            .Build();
        var settings = new SyncTablesSettings();
        configuration.GetSection(SyncTablesSettings.SectionName).Bind(settings);
        return settings;
    }

    private SqlBulkWriter CreateWriter() => new(
        Options.Create(new SqlSettings { ConnectionString = _connectionString, BulkCopyBatchSize = 5000 }),
        NullLogger<SqlBulkWriter>.Instance);

    private async Task RunScriptAsync(string script)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task DropTablesAsync()
    {
        await RunScriptAsync(string.Join("\n", Tables.Select(t => $"DROP TABLE IF EXISTS [sap].[{t}];")));
    }

    private async Task<List<string>> QueryListAsync(string sql, string parameter)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@t", parameter);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private sealed class QueueReader : ISapTableReader
    {
        private readonly Queue<DataTable> _tables;

        public QueueReader(Queue<DataTable> tables) => _tables = tables;

        public Task<DataTable> ReadTableAsync(SapQueryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tables.Dequeue());
    }
}
