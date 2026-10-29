namespace SapSync.Core.Interfaces;

/// <summary>
/// Service for generating date-based filter conditions for incremental SAP table syncs.
/// Supports SAP date format (YYYYMMDD) and filters based on INERTDATE and UPDATEDATE columns.
/// </summary>
public interface IDateBasedSyncService
{
    /// <summary>
    /// Generates an ABAP WHERE clause for tables with INERTDATE and/or UPDATEDATE columns.
    /// Returns records where either INERTDATE >= yesterday or UPDATEDATE >= yesterday (in YYYYMMDD format).
    /// </summary>
    /// <param name="daysBack">Number of days back from today (default 1 = yesterday)</param>
    /// <param name="includeInertDate">Include INERTDATE in filter (default true)</param>
    /// <param name="includeUpdateDate">Include UPDATEDATE in filter (default true)</param>
    /// <returns>ABAP WHERE clause or null if no valid filters</returns>
    string? GenerateDateFilter(int daysBack = 1, bool includeInertDate = true, bool includeUpdateDate = true);

    /// <summary>
    /// Generates a filter for INERTDATE only.
    /// </summary>
    string GenerateInertDateFilter(int daysBack = 1);

    /// <summary>
    /// Generates a filter for UPDATEDATE only.
    /// </summary>
    string GenerateUpdateDateFilter(int daysBack = 1);

    /// <summary>
    /// Generates a combined filter for both INERTDATE and UPDATEDATE (OR condition).
    /// </summary>
    string GenerateCombinedDateFilter(int daysBack = 1);

    /// <summary>
    /// Merges an existing filter condition with date-based filters using OR logic.
    /// </summary>
    string? MergeWithExistingFilter(string? existingFilter, string dateFilter, bool useOrLogic = true);
}
