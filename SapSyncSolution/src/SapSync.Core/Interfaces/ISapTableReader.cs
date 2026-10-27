using System.Data;
using SapSync.Core.Models;

namespace SapSync.Core.Interfaces;

public interface ISapTableReader
{
    /// <summary>Reads the requested SAP table into a <see cref="DataTable"/> whose columns are the SAP field names.</summary>
    Task<DataTable> ReadTableAsync(SapQueryRequest request, CancellationToken cancellationToken = default);
}
