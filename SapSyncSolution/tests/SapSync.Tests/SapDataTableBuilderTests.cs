using System.Data;
using SapSync.Infrastructure.Sap;

namespace SapSync.Tests;

public class SapDataTableBuilderTests
{
    private const char D = SapTableReader.FieldDelimiter;

    private static readonly SapFieldMetadata[] Fields =
    {
        new("MATNR", "C", 18, "Material"),
        new("MAKTX", "C", 40, "Description"),
        new("ERSDA", "D", 8, "Created on"),
        new("ERZET", "T", 6, "Time"),
        new("BRGEW", "P", 13, "Gross weight"),
        new("COUNT", "I", 10, "Counter"),
        new("RATE", "F", 24, "Rate"),
        new("NUMC", "N", 6, "Numeric text"),
    };

    [Fact]
    public void Build_ConvertsAbapTypes_AndKeepsDelimiterLikeCharactersInText()
    {
        var row = string.Join(D, "000000000000000042  ", "Bolt | 10, M8\twide ", "20240229", "235959", "  1234.567-", "  17", "1.5000000000000000E+02", "000123");

        using var table = SapDataTableBuilder.Build("MARA", Fields, new[] { row }, D, convertAbapTypes: true);

        Assert.Equal("MARA", table.TableName);
        Assert.Equal(typeof(DateTime), table.Columns["ERSDA"]!.DataType);
        Assert.Equal(typeof(decimal), table.Columns["BRGEW"]!.DataType);
        Assert.Equal("Material", table.Columns["MATNR"]!.Caption);

        var r = Assert.Single(table.Rows.Cast<DataRow>());
        Assert.Equal("000000000000000042", r["MATNR"]);
        Assert.Equal("Bolt | 10, M8\twide", r["MAKTX"]);
        Assert.Equal(new DateTime(2024, 2, 29), r["ERSDA"]);
        Assert.Equal(new TimeSpan(23, 59, 59), r["ERZET"]);
        Assert.Equal(-1234.567m, r["BRGEW"]);
        Assert.Equal(17, r["COUNT"]);
        Assert.Equal(150d, r["RATE"]);
        Assert.Equal("000123", r["NUMC"]);
        Assert.Equal(DataRowState.Unchanged, r.RowState);
    }

    [Fact]
    public void Build_MapsInitialDatesAndBlankNumbersToNull_AndPadsMissingTrailingFields()
    {
        var row = string.Join(D, "1", "", "00000000", "", "");

        using var table = SapDataTableBuilder.Build("MARA", Fields, new[] { row }, D, convertAbapTypes: true);

        var r = table.Rows[0];
        Assert.Equal(string.Empty, r["MAKTX"]);
        Assert.Equal(DBNull.Value, r["ERSDA"]);
        Assert.Equal(DBNull.Value, r["ERZET"]);
        Assert.Equal(DBNull.Value, r["BRGEW"]);
        Assert.Equal(DBNull.Value, r["COUNT"]);
        Assert.Equal(string.Empty, r["NUMC"]);
    }

    [Fact]
    public void Build_WithoutTypeConversion_ReturnsStringColumns()
    {
        var row = string.Join(D, "1", "Text", "00000000", "120000", "1.00", "5", "1.0E+00", "000001");

        using var table = SapDataTableBuilder.Build("MARA", Fields, new[] { row }, D, convertAbapTypes: false);

        Assert.All(table.Columns.Cast<DataColumn>(), c => Assert.Equal(typeof(string), c.DataType));
        Assert.Equal("00000000", table.Rows[0]["ERSDA"]);
    }

    [Fact]
    public void Build_Throws_WhenRowHasMoreValuesThanFields()
    {
        var row = string.Join(D, Enumerable.Repeat("x", Fields.Length + 1));

        Assert.Throws<InvalidDataException>(() => SapDataTableBuilder.Build("MARA", Fields, new[] { row }, D, true));
    }

    [Fact]
    public void Build_Throws_ForMalformedPackedNumber()
    {
        var row = string.Join(D, "1", "", "", "", "abc");

        Assert.Throws<FormatException>(() => SapDataTableBuilder.Build("MARA", Fields, new[] { row }, D, true));
    }
}
