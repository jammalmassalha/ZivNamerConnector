using System.Data;

namespace SapSync.Core.Services;

/// <summary>Renames SAP field columns of a <see cref="DataTable"/> to their SQL Server column names.</summary>
public static class ColumnMapper
{
    /// <summary>Throws <see cref="ArgumentException"/> for empty or duplicate source/target names.</summary>
    public static void Validate(IReadOnlyDictionary<string, string>? mappings)
    {
        if (mappings is null || mappings.Count == 0)
        {
            return;
        }

        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, target) in mappings)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Column mappings must not contain an empty SAP field name.", nameof(mappings));
            }

            if (!sources.Add(source.Trim()))
            {
                throw new ArgumentException($"SAP field '{source}' is mapped more than once.", nameof(mappings));
            }

            var column = TargetName(source, target);
            if (!targets.Add(column))
            {
                throw new ArgumentException($"SQL column '{column}' is the target of more than one SAP field.", nameof(mappings));
            }
        }
    }

    /// <summary>Renames mapped columns in place. Fields without a mapping keep their SAP name.</summary>
    public static void Apply(DataTable table, IReadOnlyDictionary<string, string>? mappings)
    {
        ArgumentNullException.ThrowIfNull(table);
        Validate(mappings);
        if (mappings is null || mappings.Count == 0)
        {
            return;
        }

        var renames = new List<(DataColumn Column, string NewName)>();
        foreach (var (source, target) in mappings)
        {
            var column = table.Columns[source.Trim()];
            if (column is not null)
            {
                renames.Add((column, TargetName(source, target)));
            }
        }

        // Two passes so swaps (A→B, B→A) and renames onto existing unmapped names are detected cleanly.
        foreach (var (column, _) in renames)
        {
            column.ColumnName = "__sapsync_" + Guid.NewGuid().ToString("N");
        }

        foreach (var (column, newName) in renames)
        {
            if (table.Columns.Contains(newName))
            {
                throw new ArgumentException($"Cannot rename to '{newName}': a column with that name already exists.", nameof(mappings));
            }

            column.ColumnName = newName;
        }
    }

    private static string TargetName(string source, string? target) =>
        string.IsNullOrWhiteSpace(target) ? source.Trim() : target.Trim();
}
