using System.Data;
using SapSync.Core.Services;

namespace SapSync.Tests;

public class ColumnMapperTests
{
    private static DataTable Table(params string[] columns)
    {
        var table = new DataTable();
        foreach (var c in columns)
        {
            table.Columns.Add(c, typeof(string));
        }

        table.Rows.Add(columns.Cast<object>().ToArray());
        return table;
    }

    [Fact]
    public void Apply_RenamesMappedColumns_KeepsDataAndUnmappedNames()
    {
        var table = Table("PATNR", "NNAME", "MANDT");

        ColumnMapper.Apply(table, new Dictionary<string, string> { ["patnr"] = "PATIENTID", ["NNAME"] = "LAST_NAME_PAT", ["EXTNR"] = "ID_NUMBER" });

        Assert.Equal(new[] { "PATIENTID", "LAST_NAME_PAT", "MANDT" }, table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
        Assert.Equal("PATNR", table.Rows[0]["PATIENTID"]);
    }

    [Fact]
    public void Apply_SupportsSwapsAndEmptyTargets()
    {
        var table = Table("A", "B");

        ColumnMapper.Apply(table, new Dictionary<string, string> { ["A"] = "B", ["B"] = "A" });
        ColumnMapper.Apply(table, new Dictionary<string, string> { ["A"] = "" });

        Assert.Equal(new[] { "B", "A" }, table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
        Assert.Equal("A", table.Rows[0]["B"]);
    }

    [Fact]
    public void Apply_RejectsDuplicateTargetsAndCollisions()
    {
        Assert.Throws<ArgumentException>(() => ColumnMapper.Validate(new Dictionary<string, string> { ["A"] = "X", ["B"] = "x" }));
        Assert.Throws<ArgumentException>(() => ColumnMapper.Validate(new Dictionary<string, string> { ["A"] = "B", ["B"] = "" }));
        Assert.Throws<ArgumentException>(() => ColumnMapper.Apply(Table("A", "MANDT"), new Dictionary<string, string> { ["A"] = "MANDT" }));
    }
}
