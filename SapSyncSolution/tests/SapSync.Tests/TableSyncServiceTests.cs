using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;
using SapSync.Core.Services;

namespace SapSync.Tests;

public class TableSyncServiceTests
{
    private static TableSyncService Create(FakeOrchestrator orchestrator, int pageSize = 3) => new(
        orchestrator,
        Options.Create(new SyncTablesSettings
        {
            Tables = new(StringComparer.OrdinalIgnoreCase)
            {
                ["NPAT"] = new SyncTableDefinition
                {
                    TargetDbTable = "sap.NPAT",
                    PrimaryKeyColumns = new() { "PATIENTID" },
                    Fields = new() { ["PATNR"] = "PATIENTID", ["NNAME"] = "LAST_NAME_PAT" },
                    FilterCondition = "PATNR <> ''",
                    PageSize = pageSize,
                },
                ["Accidents"] = new SyncTableDefinition
                {
                    SapTableName = "ZNACCIDENT",
                    TargetDbTable = "sap.ZNACCIDENT",
                    PrimaryKeyColumns = new() { "ACCIDENT_NO" },
                    Fields = new() { ["UNFNR"] = "ACCIDENT_NO" },
                    PageSize = pageSize,
                },
            },
        }),
        NullLogger<TableSyncService>.Instance);

    [Fact]
    public async Task SyncTableAsync_PagesUntilAShortPage_AndMapsTheDefinition()
    {
        var orchestrator = new FakeOrchestrator(totalRows: 7);

        var result = await Create(orchestrator).SyncTableAsync("npat");

        Assert.True(result.Success);
        Assert.Equal("NPAT", result.Name);
        Assert.Equal(3, result.Pages);
        Assert.Equal(7, result.RowsRead);
        Assert.Equal(new[] { 0, 3, 6 }, orchestrator.Requests.Select(r => r.RowSkip));
        Assert.All(orchestrator.Requests, r => Assert.Equal(3, r.RowCount));

        var first = orchestrator.Requests[0];
        Assert.Equal("NPAT", first.TableName);
        Assert.Equal("sap.NPAT", first.TargetDbTable);
        Assert.Equal(new[] { "PATIENTID" }, first.PrimaryKeyColumns);
        Assert.Equal(new[] { "PATNR", "NNAME" }, first.Fields);
        Assert.Equal("PATIENTID", first.ColumnMappings!["patnr"]);
        Assert.Equal("PATNR <> ''", first.FilterCondition);
    }

    [Fact]
    public async Task SyncTableAsync_StopsOnAnExactMultipleWithAnEmptyPage_AndHonoursOverrides()
    {
        var orchestrator = new FakeOrchestrator(totalRows: 6);
        var full = await Create(orchestrator).SyncTableAsync("NPAT");
        Assert.Equal(3, full.Pages);
        Assert.Equal(6, full.RowsRead);

        var limited = new FakeOrchestrator(totalRows: 100);
        var result = await Create(limited).SyncTableAsync("NPAT", new TableSyncRequest { MaxRows = 5, FilterCondition = "ERDAT >= '20260101'" });

        Assert.Equal(5, result.RowsRead);
        Assert.Equal(new[] { 3, 2 }, limited.Requests.Select(r => r.RowCount));
        Assert.All(limited.Requests, r => Assert.Equal("ERDAT >= '20260101'", r.FilterCondition));
    }

    [Fact]
    public async Task SyncTableAsync_StopsAtTheFirstFailedPage()
    {
        var orchestrator = new FakeOrchestrator(totalRows: 10, failAtSkip: 3);

        var result = await Create(orchestrator).SyncTableAsync("NPAT");

        Assert.False(result.Success);
        Assert.Equal(2, result.Pages);
        Assert.Equal(SyncErrorTypes.SqlWrite, result.ErrorType);
        Assert.Equal("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task SyncAllAsync_SyncsEveryTable_UsingTheSapTableName()
    {
        var orchestrator = new FakeOrchestrator(totalRows: 1);
        var sut = Create(orchestrator);

        var results = await sut.SyncAllAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Contains(orchestrator.Requests, r => r.TableName == "ZNACCIDENT");
        Assert.True(sut.IsConfigured("accidents"));
        Assert.False(sut.IsConfigured("MARA"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.SyncTableAsync("MARA"));
    }

    private sealed class FakeOrchestrator : ISapSyncOrchestrator
    {
        private readonly int _totalRows;
        private readonly int? _failAtSkip;

        public FakeOrchestrator(int totalRows, int? failAtSkip = null)
        {
            _totalRows = totalRows;
            _failAtSkip = failAtSkip;
        }

        public List<SapQueryRequest> Requests { get; } = new();

        public Task<SyncResult> SyncAsync(SapQueryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var rows = Math.Max(0, Math.Min(request.RowCount, _totalRows - request.RowSkip));
            if (request.RowSkip == _failAtSkip)
            {
                return Task.FromResult(new SyncResult { Success = false, RowsRead = rows, ErrorType = SyncErrorTypes.SqlWrite, ErrorMessage = "boom" });
            }

            return Task.FromResult(new SyncResult { Success = true, RowsRead = rows, RowsProcessed = rows });
        }
    }
}
