using System.Text;

namespace SapSync.Infrastructure.Persistence;

/// <summary>Generates the set-based MERGE used to upsert the staging table into the target table.</summary>
internal static class MergeSqlBuilder
{
    private const string RowNumberColumn = "[__SapSyncRowNumber]";
    private const string BinaryCollation = " COLLATE Latin1_General_BIN2";

    /// <param name="quotedTarget">Already quoted target, e.g. <c>[dbo].[Materials]</c>.</param>
    /// <param name="quotedStaging">Already quoted staging table, e.g. <c>[#TempStaging_...]</c>.</param>
    /// <param name="columns">Unquoted column names present in the staging table (including keys).</param>
    /// <param name="keyColumns">Unquoted key column names (subset of <paramref name="columns"/>).</param>
    /// <param name="detectChanges">Only update rows whose values actually differ (NULL-safe via EXCEPT).</param>
    /// <param name="characterColumns">
    /// Character columns compared with a binary collation during change detection, so case/accent-only changes
    /// are not hidden by a case-insensitive column collation.
    /// </param>
    public static string Build(
        string quotedTarget,
        string quotedStaging,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns,
        bool detectChanges,
        IReadOnlyCollection<string>? characterColumns = null)
    {
        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        if (keyColumns.Count == 0)
        {
            throw new ArgumentException("At least one key column is required.", nameof(keyColumns));
        }

        var keySet = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
        if (!keySet.IsSubsetOf(columns))
        {
            throw new ArgumentException("All key columns must be part of the column list.", nameof(keyColumns));
        }

        var quotedColumns = columns.Select(SqlIdentifier.Quote).ToList();
        var quotedKeys = keyColumns.Select(SqlIdentifier.Quote).ToList();
        var updateColumns = columns.Where(c => !keySet.Contains(c)).ToList();
        var quotedUpdateColumns = updateColumns.Select(SqlIdentifier.Quote).ToList();
        var binarySet = new HashSet<string>(characterColumns ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var comparedColumns = updateColumns
            .Select(c => (Quoted: SqlIdentifier.Quote(c), Collation: binarySet.Contains(c) ? BinaryCollation : string.Empty))
            .ToList();
        var columnList = string.Join(", ", quotedColumns);

        var sql = new StringBuilder(1024);
        sql.AppendLine("SET NOCOUNT ON;");
        sql.Append("MERGE INTO ").Append(quotedTarget).AppendLine(" WITH (HOLDLOCK) AS T");
        sql.AppendLine("USING (");
        sql.Append("    SELECT ").AppendLine(columnList);
        sql.AppendLine("    FROM (");
        sql.Append("        SELECT ").Append(columnList)
           .Append(", ROW_NUMBER() OVER (PARTITION BY ").Append(string.Join(", ", quotedKeys))
           .Append(" ORDER BY (SELECT NULL)) AS ").AppendLine(RowNumberColumn);
        sql.Append("        FROM ").AppendLine(quotedStaging);
        sql.Append("    ) AS D WHERE D.").Append(RowNumberColumn).AppendLine(" = 1");
        sql.AppendLine(") AS S");
        sql.Append("ON ").AppendLine(string.Join(" AND ", quotedKeys.Select(k => $"T.{k} = S.{k}")));

        if (quotedUpdateColumns.Count > 0)
        {
            sql.Append("WHEN MATCHED");
            if (detectChanges)
            {
                sql.Append(" AND EXISTS (SELECT ")
                   .Append(string.Join(", ", comparedColumns.Select(c => $"S.{c.Quoted}{c.Collation}")))
                   .Append(" EXCEPT SELECT ")
                   .Append(string.Join(", ", comparedColumns.Select(c => $"T.{c.Quoted}{c.Collation}")))
                   .Append(')');
            }

            sql.AppendLine(" THEN");
            sql.Append("    UPDATE SET ").AppendLine(string.Join(", ", quotedUpdateColumns.Select(c => $"T.{c} = S.{c}")));
        }

        sql.AppendLine("WHEN NOT MATCHED BY TARGET THEN");
        sql.Append("    INSERT (").Append(columnList).Append(") VALUES (")
           .Append(string.Join(", ", quotedColumns.Select(c => $"S.{c}"))).AppendLine(");");
        sql.Append("SELECT @@ROWCOUNT;");

        return sql.ToString();
    }
}
