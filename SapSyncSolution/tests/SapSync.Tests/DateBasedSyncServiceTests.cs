using System;
using Microsoft.Extensions.Logging;
using Moq;
using SapSync.Core.Interfaces;
using SapSync.Core.Services;

namespace SapSync.Tests;

public class DateBasedSyncServiceTests
{
    private readonly Mock<ILogger<DateBasedSyncService>> _mockLogger;
    private readonly DateBasedSyncService _service;

    public DateBasedSyncServiceTests()
    {
        _mockLogger = new Mock<ILogger<DateBasedSyncService>>();
        _service = new DateBasedSyncService(_mockLogger.Object);
    }

    [Fact]
    public void GenerateDateFilter_WithDefaultParameters_ReturnsCombinedORCondition()
    {
        var yesterday = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
        var result = _service.GenerateDateFilter();

        Assert.NotNull(result);
        Assert.Contains("INERTDATE >= '", result);
        Assert.Contains("UPDATEDATE >= '", result);
        Assert.Contains(" OR ", result);
        Assert.Contains(yesterday, result);
    }

    [Fact]
    public void GenerateDateFilter_WithDaysBack0_ReturnsCurrentDay()
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        var result = _service.GenerateDateFilter(daysBack: 0);

        Assert.NotNull(result);
        Assert.Contains(today, result);
    }

    [Fact]
    public void GenerateDateFilter_With2DaysBack_Returns2DaysAgoDate()
    {
        var twoDaysAgo = DateTime.Now.AddDays(-2).ToString("yyyyMMdd");
        var result = _service.GenerateDateFilter(daysBack: 2);

        Assert.NotNull(result);
        Assert.Contains(twoDaysAgo, result);
    }

    [Fact]
    public void GenerateDateFilter_WithInertDateOnly_ReturnsInertDateCondition()
    {
        var result = _service.GenerateDateFilter(includeInertDate: true, includeUpdateDate: false);

        Assert.NotNull(result);
        Assert.Contains("INERTDATE >= '", result);
        Assert.DoesNotContain("UPDATEDATE >= '", result);
        Assert.DoesNotContain(" OR ", result);
    }

    [Fact]
    public void GenerateDateFilter_WithUpdateDateOnly_ReturnsUpdateDateCondition()
    {
        var result = _service.GenerateDateFilter(includeInertDate: false, includeUpdateDate: true);

        Assert.NotNull(result);
        Assert.DoesNotContain("INERTDATE >= '", result);
        Assert.Contains("UPDATEDATE >= '", result);
        Assert.DoesNotContain(" OR ", result);
    }

    [Fact]
    public void GenerateDateFilter_WithBothFalse_ReturnsNull()
    {
        var result = _service.GenerateDateFilter(includeInertDate: false, includeUpdateDate: false);

        Assert.Null(result);
    }

    [Fact]
    public void GenerateDateFilter_WithNegativeDaysBack_LogsWarningAndUses1()
    {
        var yesterday = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
        var result = _service.GenerateDateFilter(daysBack: -5);

        Assert.NotNull(result);
        Assert.Contains(yesterday, result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("daysBack must be >= 0")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void GenerateInertDateFilter_ReturnsInertDateOnlyFilter()
    {
        var result = _service.GenerateInertDateFilter(1);

        Assert.NotNull(result);
        Assert.Contains("INERTDATE >= '", result);
        Assert.DoesNotContain("UPDATEDATE", result);
    }

    [Fact]
    public void GenerateUpdateDateFilter_ReturnsUpdateDateOnlyFilter()
    {
        var result = _service.GenerateUpdateDateFilter(1);

        Assert.NotNull(result);
        Assert.Contains("UPDATEDATE >= '", result);
        Assert.DoesNotContain("INERTDATE", result);
    }

    [Fact]
    public void GenerateCombinedDateFilter_ReturnsBothColumnsWithOR()
    {
        var result = _service.GenerateCombinedDateFilter(1);

        Assert.NotNull(result);
        Assert.Contains("INERTDATE >= '", result);
        Assert.Contains("UPDATEDATE >= '", result);
        Assert.Contains(" OR ", result);
    }

    [Fact]
    public void MergeWithExistingFilter_BothNullOrEmpty_ReturnsDateFilter()
    {
        var dateFilter = "INERTDATE >= '20260101'";
        var result = _service.MergeWithExistingFilter(null, dateFilter);

        Assert.Equal(dateFilter, result);
    }

    [Fact]
    public void MergeWithExistingFilter_DateFilterNullOrEmpty_ReturnsExistingFilter()
    {
        var existingFilter = "STORN = ' '";
        var result = _service.MergeWithExistingFilter(existingFilter, "");

        Assert.Equal(existingFilter, result);
    }

    [Fact]
    public void MergeWithExistingFilter_BothProvided_CombinesWithOR()
    {
        var existingFilter = "STORN = ' ' AND FSTORN = ' '";
        var dateFilter = "INERTDATE >= '20260101'";
        var result = _service.MergeWithExistingFilter(existingFilter, dateFilter, useOrLogic: true);

        Assert.NotNull(result);
        Assert.Contains("STORN = ' ' AND FSTORN = ' '", result);
        Assert.Contains("INERTDATE >= '20260101'", result);
        Assert.Contains(" OR ", result);
    }

    [Fact]
    public void MergeWithExistingFilter_BothProvided_CombinesWithAND()
    {
        var existingFilter = "STORN = ' '";
        var dateFilter = "INERTDATE >= '20260101'";
        var result = _service.MergeWithExistingFilter(existingFilter, dateFilter, useOrLogic: false);

        Assert.NotNull(result);
        Assert.Contains("STORN = ' '", result);
        Assert.Contains("INERTDATE >= '20260101'", result);
        Assert.Contains(" AND ", result);
    }

    [Fact]
    public void GenerateDateFilter_UsesCorrectSapDateFormat()
    {
        var result = _service.GenerateDateFilter(daysBack: 0);
        var today = DateTime.Now.ToString("yyyyMMdd");

        Assert.NotNull(result);
        Assert.Matches(@"\b\d{8}\b", result); // Matches 8-digit date
        Assert.Contains(today, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(365)]
    public void GenerateDateFilter_VariousDaysBack_ProducesValidSapDate(int daysBack)
    {
        var result = _service.GenerateDateFilter(daysBack);
        var expectedDate = DateTime.Now.AddDays(-daysBack).ToString("yyyyMMdd");

        Assert.NotNull(result);
        Assert.Contains(expectedDate, result);
    }

    [Fact]
    public void GenerateDateFilter_LogsInformationMessage()
    {
        _service.GenerateDateFilter(daysBack: 1);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Generated date-based filter")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void MergeWithExistingFilter_LogsInformationWhenMerging()
    {
        _service.MergeWithExistingFilter("STORN = ' '", "INERTDATE >= '20260101'");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Merged existing filter")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }
}
