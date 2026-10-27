using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;
using SapSync.Core.Services;

namespace SapSync.Tests;

public class SapSyncOrchestratorTests
{
    private static SapQueryRequest Request() => new()
    {
        TableName = "MARA",
        TargetDbTable = "dbo.Materials",
        PrimaryKeyColumns = new List<string> { "MATNR" },
    };

    private static DataTable Rows(int count)
    {
        var table = new DataTable();
        table.Columns.Add("MATNR", typeof(string));
        for (var i = 0; i < count; i++)
        {
            table.Rows.Add(i.ToString());
        }

        return table;
    }

    [Fact]
    public async Task SyncAsync_ReadsThenUpserts_AndReportsMetrics()
    {
        var writer = new FakeWriter(affected: 2);
        var sut = new SapSyncOrchestrator(new FakeReader(() => Rows(3)), writer, NullLogger<SapSyncOrchestrator>.Instance);

        var result = await sut.SyncAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(3, result.RowsRead);
        Assert.Equal(2, result.RowsProcessed);
        Assert.Equal("dbo.Materials", writer.TargetTable);
        Assert.Equal(new[] { "MATNR" }, writer.Keys);
        Assert.Null(result.ErrorMessage);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
        Assert.True(result.ElapsedMs >= result.SapReadElapsedMs + result.SqlWriteElapsedMs - 1);
    }

    [Fact]
    public async Task SyncAsync_SkipsWriter_WhenSapReturnsNoRows()
    {
        var writer = new FakeWriter(affected: 99);
        var sut = new SapSyncOrchestrator(new FakeReader(() => Rows(0)), writer, NullLogger<SapSyncOrchestrator>.Instance);

        var result = await sut.SyncAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(0, result.RowsProcessed);
        Assert.Null(writer.TargetTable);
    }

    [Fact]
    public async Task SyncAsync_ReportsSapFailure()
    {
        var sut = new SapSyncOrchestrator(
            new FakeReader(() => throw new InvalidOperationException("logon failed")), new FakeWriter(0), NullLogger<SapSyncOrchestrator>.Instance);

        var result = await sut.SyncAsync(Request());

        Assert.False(result.Success);
        Assert.Equal(SyncErrorTypes.SapRead, result.ErrorType);
        Assert.Equal("logon failed", result.ErrorMessage);
    }

    [Fact]
    public async Task SyncAsync_ReportsSqlAndValidationFailures()
    {
        var sqlFailure = await new SapSyncOrchestrator(
            new FakeReader(() => Rows(1)), new FakeWriter(new TimeoutException("timeout")), NullLogger<SapSyncOrchestrator>.Instance).SyncAsync(Request());
        var validationFailure = await new SapSyncOrchestrator(
            new FakeReader(() => Rows(1)), new FakeWriter(new ArgumentException("bad key")), NullLogger<SapSyncOrchestrator>.Instance).SyncAsync(Request());

        Assert.Equal(SyncErrorTypes.SqlWrite, sqlFailure.ErrorType);
        Assert.Equal(1, sqlFailure.RowsRead);
        Assert.Equal(SyncErrorTypes.Validation, validationFailure.ErrorType);
    }

    [Fact]
    public async Task SyncAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = new SapSyncOrchestrator(
            new FakeReader(() => throw new OperationCanceledException()), new FakeWriter(0), NullLogger<SapSyncOrchestrator>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.SyncAsync(Request(), cts.Token));
    }

    private sealed class FakeReader : ISapTableReader
    {
        private readonly Func<DataTable> _read;

        public FakeReader(Func<DataTable> read) => _read = read;

        public Task<DataTable> ReadTableAsync(SapQueryRequest request, CancellationToken cancellationToken = default) => Task.FromResult(_read());
    }

    private sealed class FakeWriter : ISqlBulkWriter
    {
        private readonly int _affected;
        private readonly Exception? _error;

        public FakeWriter(int affected) => _affected = affected;

        public FakeWriter(Exception error) => _error = error;

        public string? TargetTable { get; private set; }

        public IReadOnlyList<string>? Keys { get; private set; }

        public Task<int> BulkUpsertAsync(DataTable data, string targetTable, IReadOnlyList<string> primaryKeyColumns, CancellationToken cancellationToken = default)
        {
            if (_error is not null)
            {
                throw _error;
            }

            TargetTable = targetTable;
            Keys = primaryKeyColumns;
            return Task.FromResult(_affected);
        }
    }
}
