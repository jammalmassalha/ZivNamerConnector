using System.Data;

namespace SapSync.Core.Interfaces;

public interface ISqlBulkWriter
{
    /// <summary>
    /// Upserts <paramref name="data"/> into <paramref name="targetTable"/> matching on <paramref name="primaryKeyColumns"/>.
    /// </summary>
    /// <returns>Number of rows inserted or updated.</returns>
    Task<int> BulkUpsertAsync(
        DataTable data,
        string targetTable,
        IReadOnlyList<string> primaryKeyColumns,
        CancellationToken cancellationToken = default);
}
