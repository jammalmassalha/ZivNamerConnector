using System.Data;
using System.Globalization;

namespace SapSync.Infrastructure.Sap;

/// <summary>Builds a typed <see cref="DataTable"/> from <c>RFC_READ_TABLE</c> FIELDS metadata and delimited DATA rows.</summary>
internal static class SapDataTableBuilder
{
    public static DataTable Build(
        string tableName,
        IReadOnlyList<SapFieldMetadata> fields,
        IEnumerable<string> rows,
        char delimiter,
        bool convertAbapTypes)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(rows);

        var table = new DataTable(tableName) { Locale = CultureInfo.InvariantCulture };
        foreach (var field in fields)
        {
            var column = table.Columns.Add(field.Name, SapValueConverter.GetClrType(field.AbapType, convertAbapTypes));
            column.Caption = field.Description;
            column.AllowDBNull = true;
        }

        var values = new object[fields.Count];

        // Suspend notifications, index maintenance and constraint checks while loading.
        table.BeginLoadData();
        try
        {
            foreach (var row in rows)
            {
                var parts = (row ?? string.Empty).Split(delimiter);
                if (parts.Length > fields.Count)
                {
                    throw new InvalidDataException(
                        $"A row of SAP table '{tableName}' contains {parts.Length} values but {fields.Count} fields were returned.");
                }

                for (var i = 0; i < fields.Count; i++)
                {
                    values[i] = SapValueConverter.Convert(i < parts.Length ? parts[i] : null, fields[i].AbapType, convertAbapTypes);
                }

                table.LoadDataRow(values, fAcceptChanges: true);
            }
        }
        finally
        {
            table.EndLoadData();
        }

        return table;
    }
}
