using System.Text;
using Microsoft.Extensions.Logging;
using SapSync.Core.Models;

namespace SapSync.Core.Services;

/// <summary>
/// Builds ABAP WHERE clauses for incremental (delta) table syncs using dual-date filtering.
/// Supports SAP date format (YYYYMMDD) and ensures RFC_READ_TABLE compatibility (72-char lines).
/// </summary>
public sealed class DeltaSyncFilterBuilder
{
    private readonly ILogger<DeltaSyncFilterBuilder> _logger;

    public DeltaSyncFilterBuilder(ILogger<DeltaSyncFilterBuilder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a dual-date WHERE clause: (insertDateField >= @date OR updateDateField >= @date)
    /// The clause respects the 72-character line limit for SAP RFC_READ_TABLE OPTIONS.
    /// </summary>
    /// <param name="tableDefinition">Table configuration with field names</param>
    /// <param name="lookbackDays">Days back from today (default 1 = yesterday)</param>
    /// <param name="existingFilter">Optional existing WHERE clause to merge</param>
    /// <returns>ABAP WHERE clause or null if table lacks date fields</returns>
    public string? BuildDualDateFilter(SyncTableDefinition tableDefinition, int lookbackDays = 1, string? existingFilter = null)
    {
        if (tableDefinition == null)
            throw new ArgumentNullException(nameof(tableDefinition));

        if (string.IsNullOrWhiteSpace(tableDefinition.InsertDateField) || 
            string.IsNullOrWhiteSpace(tableDefinition.UpdateDateField))
        {
            _logger.LogWarning(
                "Table {TableName} lacks InsertDateField or UpdateDateField; cannot build dual-date filter",
                tableDefinition.SapTableName ?? "unknown");
            return existingFilter;
        }

        var fromDate = DateTime.Today.AddDays(-lookbackDays).ToString("yyyyMMdd");
        var insertField = tableDefinition.InsertDateField.Trim();
        var updateField = tableDefinition.UpdateDateField.Trim();

        var dualDateClause = $"( {insertField} >= '{fromDate}' OR {updateField} >= '{fromDate}' )";

        _logger.LogInformation(
            "Built dual-date filter for {TableName}: lookback={Days} days (date={Date}). Clause: {Filter}",
            tableDefinition.SapTableName ?? "unknown", lookbackDays, fromDate, dualDateClause);

        if (string.IsNullOrWhiteSpace(existingFilter))
        {
            return dualDateClause;
        }

        var merged = MergeFilters(existingFilter, dualDateClause);
        _logger.LogInformation(
            "Merged dual-date filter with existing filter for {TableName}: {MergedFilter}",
            tableDefinition.SapTableName ?? "unknown", merged);

        return merged;
    }

    /// <summary>
    /// Merges an existing WHERE clause with a dual-date clause using AND logic.
    /// </summary>
    public string MergeFilters(string existingFilter, string dualDateFilter)
    {
        if (string.IsNullOrWhiteSpace(existingFilter))
            return dualDateFilter;

        if (string.IsNullOrWhiteSpace(dualDateFilter))
            return existingFilter;

        return $"{existingFilter} AND {dualDateFilter}";
    }

    /// <summary>
    /// Validates that a WHERE clause fits within SAP RFC line constraints (72 chars per line).
    /// </summary>
    /// <param name="filterCondition">ABAP WHERE clause to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public bool ValidateFilterForRfc(string? filterCondition)
    {
        if (string.IsNullOrWhiteSpace(filterCondition))
            return true;

        // Split using the same tokenization as RfcOptionsBuilder to check line lengths
        var lines = SplitIntoRfcLines(filterCondition);
        var allValid = lines.All(line => line.Length <= 72);

        if (!allValid)
        {
            _logger.LogError(
                "Filter condition exceeds RFC line limit (72 chars). Lines: {LineCount}, Max: {MaxLength}",
                lines.Count, lines.Max(l => l.Length));
        }

        return allValid;
    }

    /// <summary>
    /// Splits a WHERE clause into RFC-compliant lines (max 72 chars each).
    /// </summary>
    private static List<string> SplitIntoRfcLines(string filterCondition)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(filterCondition))
            return lines;

        var current = new StringBuilder(72);
        foreach (var token in Tokenize(filterCondition))
        {
            if (current.Length > 0 && current.Length + 1 + token.Length <= 72)
            {
                current.Append(' ').Append(token);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            var remaining = token.AsSpan();
            while (remaining.Length > 72)
            {
                lines.Add(remaining[..72].ToString());
                remaining = remaining[72..];
            }

            current.Append(remaining);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>Tokenizes a WHERE clause, preserving literals (content within single quotes).</summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder();
        var inLiteral = false;

        foreach (var c in text)
        {
            if (c == '\'')
            {
                inLiteral = !inLiteral;
                token.Append(c);
            }
            else if (!inLiteral && char.IsWhiteSpace(c))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
            }
            else
            {
                token.Append(c);
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }
}
