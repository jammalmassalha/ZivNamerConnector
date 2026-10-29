using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Core.Services;

/// <summary>
/// Service for generating date-based filter conditions for incremental SAP table syncs.
/// Supports SAP date format (YYYYMMDD) and filters based on INERTDATE and UPDATEDATE columns.
/// </summary>
public sealed class DateBasedSyncService : IDateBasedSyncService
{
    private readonly ILogger<DateBasedSyncService> _logger;

    public DateBasedSyncService(ILogger<DateBasedSyncService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates an ABAP WHERE clause for tables with INERTDATE and/or UPDATEDATE columns.
    /// Returns records where either INERTDATE >= yesterday or UPDATEDATE >= yesterday (in YYYYMMDD format).
    /// </summary>
    /// <param name="daysBack">Number of days back from today (default 1 = yesterday)</param>
    /// <param name="includeInertDate">Include INERTDATE in filter (default true)</param>
    /// <param name="includeUpdateDate">Include UPDATEDATE in filter (default true)</param>
    /// <returns>ABAP WHERE clause or null if no valid filters</returns>
    public string? GenerateDateFilter(int daysBack = 1, bool includeInertDate = true, bool includeUpdateDate = true)
    {
        if (daysBack < 0)
        {
            _logger.LogWarning("daysBack must be >= 0, using default of 1");
            daysBack = 1;
        }

        if (!includeInertDate && !includeUpdateDate)
        {
            _logger.LogWarning("No date columns specified for filtering");
            return null;
        }

        var targetDate = DateTime.Now.AddDays(-daysBack);
        var sapDateFormat = targetDate.ToString("yyyyMMdd");

        var conditions = new List<string>();

        if (includeInertDate)
        {
            conditions.Add($"INERTDATE >= '{sapDateFormat}'");
        }

        if (includeUpdateDate)
        {
            conditions.Add($"UPDATEDATE >= '{sapDateFormat}'");
        }

        var filter = string.Join(" OR ", conditions);
        _logger.LogInformation(
            "Generated date-based filter for {DaysBack} days back (date: {SapDate}): {Filter}",
            daysBack, sapDateFormat, filter);

        return filter;
    }

    /// <summary>
    /// Generates a filter for INERTDATE only.
    /// </summary>
    public string GenerateInertDateFilter(int daysBack = 1)
    {
        return GenerateDateFilter(daysBack, includeInertDate: true, includeUpdateDate: false) 
            ?? "INERTDATE >= '20260101'";
    }

    /// <summary>
    /// Generates a filter for UPDATEDATE only.
    /// </summary>
    public string GenerateUpdateDateFilter(int daysBack = 1)
    {
        return GenerateDateFilter(daysBack, includeInertDate: false, includeUpdateDate: true) 
            ?? "UPDATEDATE >= '20260101'";
    }

    /// <summary>
    /// Generates a combined filter for both INERTDATE and UPDATEDATE (OR condition).
    /// </summary>
    public string GenerateCombinedDateFilter(int daysBack = 1)
    {
        return GenerateDateFilter(daysBack, includeInertDate: true, includeUpdateDate: true) 
            ?? "(INERTDATE >= '20260101' OR UPDATEDATE >= '20260101')";
    }

    /// <summary>
    /// Merges an existing filter condition with date-based filters using OR logic.
    /// </summary>
    public string? MergeWithExistingFilter(string? existingFilter, string dateFilter, bool useOrLogic = true)
    {
        if (string.IsNullOrWhiteSpace(existingFilter))
        {
            return dateFilter;
        }

        if (string.IsNullOrWhiteSpace(dateFilter))
        {
            return existingFilter;
        }

        var op = useOrLogic ? "OR" : "AND";
        var merged = $"({existingFilter}) {op} ({dateFilter})";
        _logger.LogInformation("Merged existing filter with date filter: {MergedFilter}", merged);
        return merged;
    }
}
