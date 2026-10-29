using Microsoft.Extensions.Logging;
using Moq;
using SapSync.Core.Models;
using SapSync.Core.Services;

namespace SapSync.Tests;

/// <summary>Tests for DeltaSyncFilterBuilder - dual-date incremental sync filter generation.</summary>
public class DeltaSyncFilterBuilderTests
{
    private readonly Mock<ILogger<DeltaSyncFilterBuilder>> _mockLogger;
    private readonly DeltaSyncFilterBuilder _builder;

    public DeltaSyncFilterBuilderTests()
    {
        _mockLogger = new Mock<ILogger<DeltaSyncFilterBuilder>>();
        _builder = new DeltaSyncFilterBuilder(_mockLogger.Object);
    }

    [Fact]
    public void BuildDualDateFilter_WithValidDateFields_ReturnsDualDateClause()
    {
        var definition = new SyncTableDefinition
        {
            SapTableName = "NPAT",
            InsertDateField = "ERDAT",
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var result = _builder.BuildDualDateFilter(definition, lookbackDays: 1);

        Assert.NotNull(result);
        Assert.Contains("ERDAT", result);
        Assert.Contains("UPDAT", result);
        Assert.Contains(" OR ", result);
        Assert.Contains("( ", result);
        Assert.Contains(" )", result);
    }

    [Fact]
    public void BuildDualDateFilter_WithLookback1_UsesYesterdayDate()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "ERDAT",
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var yesterday = DateTime.Today.AddDays(-1).ToString("yyyyMMdd");
        var result = _builder.BuildDualDateFilter(definition, lookbackDays: 1);

        Assert.NotNull(result);
        Assert.Contains(yesterday, result);
    }

    [Fact]
    public void BuildDualDateFilter_WithLookback0_UsesTodayDate()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "ERDAT",
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var today = DateTime.Today.ToString("yyyyMMdd");
        var result = _builder.BuildDualDateFilter(definition, lookbackDays: 0);

        Assert.NotNull(result);
        Assert.Contains(today, result);
    }

    [Fact]
    public void BuildDualDateFilter_WithLookback7_Uses7DaysAgoDate()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "ERDAT",
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var sevenDaysAgo = DateTime.Today.AddDays(-7).ToString("yyyyMMdd");
        var result = _builder.BuildDualDateFilter(definition, lookbackDays: 7);

        Assert.NotNull(result);
        Assert.Contains(sevenDaysAgo, result);
    }

    [Fact]
    public void BuildDualDateFilter_MissingInsertDateField_ReturnsExistingFilterOnly()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = null,
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };
        var existingFilter = "STORN = ' '";

        var result = _builder.BuildDualDateFilter(definition, 1, existingFilter);

        Assert.Equal(existingFilter, result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("lacks InsertDateField")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildDualDateFilter_MissingUpdateDateField_ReturnsExistingFilterOnly()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "ERDAT",
            UpdateDateField = null,
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var result = _builder.BuildDualDateFilter(definition, 1, null);

        Assert.Null(result);
    }

    [Fact]
    public void BuildDualDateFilter_WithExistingFilter_MergesWithAnd()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "BWIDT",
            UpdateDateField = "BWEDT",
            TargetDbTable = "sap.N_FALL_BEW",
            PrimaryKeyColumns = { "MANDT", "FALNR", "LFDBEW" },
            Fields = { ["BWIDT"] = "BWIDT" },
            FilterCondition = "STORN = ' ' AND FSTORN = ' '"
        };

        var result = _builder.BuildDualDateFilter(definition, 1, definition.FilterCondition);

        Assert.NotNull(result);
        Assert.Contains("STORN = ' ' AND FSTORN = ' '", result);
        Assert.Contains("BWIDT", result);
        Assert.Contains("BWEDT", result);
        Assert.Contains(" AND ", result);
    }

    [Fact]
    public void BuildDualDateFilter_DateFormatIsYyyymmdd()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "ERDAT",
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var result = _builder.BuildDualDateFilter(definition, 1);

        Assert.NotNull(result);
        Assert.Matches(@"'\d{8}'", result); // Matches 8-digit date in quotes
    }

    [Fact]
    public void BuildDualDateFilter_FieldNamesAreTrimmed()
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "  ERDAT  ",
            UpdateDateField = "  UPDAT  ",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var result = _builder.BuildDualDateFilter(definition, 1);

        Assert.NotNull(result);
        Assert.Contains("ERDAT >=", result);
        Assert.Contains("UPDAT >=", result);
    }

    [Fact]
    public void ValidateFilterForRfc_WithValidFilter_ReturnsTrue()
    {
        var filter = "( ERDAT >= '20261027' OR UPDAT >= '20261027' )";
        var result = _builder.ValidateFilterForRfc(filter);

        Assert.True(result);
    }

    [Fact]
    public void ValidateFilterForRfc_WithEmptyFilter_ReturnsTrue()
    {
        var result = _builder.ValidateFilterForRfc(null);
        Assert.True(result);

        result = _builder.ValidateFilterForRfc("");
        Assert.True(result);
    }

    [Fact]
    public void ValidateFilterForRfc_WithLongLine_ReturnsFalse()
    {
        var longFilter = new string('A', 100); // Exceeds 72-char RFC limit
        var result = _builder.ValidateFilterForRfc(longFilter);

        Assert.False(result);
    }

    [Fact]
    public void ValidateFilterForRfc_WithShorterLines_ReturnsTrue()
    {
        var filter = "STORN = ' ' AND FSTORN = ' ' AND ( ERDAT >= '20261027' OR UPDAT >= '20261027' )";
        var result = _builder.ValidateFilterForRfc(filter);

        // Depends on tokenization; split across lines, each should be <= 72 chars
        Assert.True(result);
    }

    [Fact]
    public void MergeFilters_BothNull_ReturnsDateFilter()
    {
        var dualDateFilter = "( ERDAT >= '20261027' OR UPDAT >= '20261027' )";
        var result = _builder.MergeFilters(null, dualDateFilter);

        Assert.Equal(dualDateFilter, result);
    }

    [Fact]
    public void MergeFilters_ExistingFilterNull_ReturnsDateFilter()
    {
        var dualDateFilter = "( ERDAT >= '20261027' OR UPDAT >= '20261027' )";
        var result = _builder.MergeFilters("", dualDateFilter);

        Assert.Equal(dualDateFilter, result);
    }

    [Fact]
    public void MergeFilters_DateFilterNull_ReturnsExisting()
    {
        var existingFilter = "STORN = ' '";
        var result = _builder.MergeFilters(existingFilter, null);

        Assert.Equal(existingFilter, result);
    }

    [Fact]
    public void MergeFilters_BothProvided_CombinesWithAnd()
    {
        var existingFilter = "STORN = ' '";
        var dateFilter = "( ERDAT >= '20261027' OR UPDAT >= '20261027' )";
        var result = _builder.MergeFilters(existingFilter, dateFilter);

        Assert.NotNull(result);
        Assert.Contains(existingFilter, result);
        Assert.Contains(dateFilter, result);
        Assert.Contains(" AND ", result);
    }

    [Fact]
    public void BuildDualDateFilter_LogsInformationMessages()
    {
        var definition = new SyncTableDefinition
        {
            SapTableName = "NFAL",
            InsertDateField = "ERDAT",
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NFAL",
            PrimaryKeyColumns = { "PATCASEID" },
            Fields = { ["FALNR"] = "PATCASEID" }
        };

        _builder.BuildDualDateFilter(definition, 1);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Built dual-date filter")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildDualDateFilter_WithExistingFilter_LogsMergeMessage()
    {
        var definition = new SyncTableDefinition
        {
            SapTableName = "N_FALL_BEW",
            InsertDateField = "BWIDT",
            UpdateDateField = "BWEDT",
            TargetDbTable = "sap.N_FALL_BEW",
            PrimaryKeyColumns = { "MANDT", "FALNR", "LFDBEW" },
            Fields = { ["BWIDT"] = "BWIDT" }
        };

        _builder.BuildDualDateFilter(definition, 1, "STORN = ' '");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Merged dual-date filter")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(365)]
    public void BuildDualDateFilter_VariousLookbackDays_ProducesValidDate(int daysBack)
    {
        var definition = new SyncTableDefinition
        {
            InsertDateField = "ERDAT",
            UpdateDateField = "UPDAT",
            TargetDbTable = "dbo.NPAT",
            PrimaryKeyColumns = { "PATIENTID" },
            Fields = { ["PATNR"] = "PATIENTID" }
        };

        var expectedDate = DateTime.Today.AddDays(-daysBack).ToString("yyyyMMdd");
        var result = _builder.BuildDualDateFilter(definition, daysBack);

        Assert.NotNull(result);
        Assert.Contains(expectedDate, result);
    }

    [Fact]
    public void BuildDualDateFilter_ComplexExistingFilter_MergesProperly()
    {
        var definition = new SyncTableDefinition
        {
            SapTableName = "NBEW",
            InsertDateField = "BWIDT",
            UpdateDateField = "BWEDT",
            TargetDbTable = "dbo.NBEW",
            PrimaryKeyColumns = { "PATCASEID", "MOVEMNT_SEQNO" },
            Fields = { ["BWIDT"] = "MOVEMNT_DATE" }
        };
        var complexFilter = "(STORN = ' ' OR STORN = 'X') AND LFDNR > 0";

        var result = _builder.BuildDualDateFilter(definition, 1, complexFilter);

        Assert.NotNull(result);
        Assert.Contains(complexFilter, result);
        Assert.Contains("BWIDT", result);
    }
}
