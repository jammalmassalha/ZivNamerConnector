using SapSync.Infrastructure.Persistence;

namespace SapSync.Tests;

public class MergeSqlBuilderTests
{
    [Fact]
    public void Build_GeneratesDeduplicatedMergeOnCompositeKey()
    {
        var sql = MergeSqlBuilder.Build(
            "[dbo].[Stock]", "[#TempStaging_x]", new[] { "MATNR", "WERKS", "LABST" }, new[] { "MATNR", "WERKS" }, detectChanges: true);

        Assert.Contains("MERGE INTO [dbo].[Stock] WITH (HOLDLOCK) AS T", sql);
        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY [MATNR], [WERKS] ORDER BY (SELECT NULL))", sql);
        Assert.Contains("FROM [#TempStaging_x]", sql);
        Assert.Contains("ON T.[MATNR] = S.[MATNR] AND T.[WERKS] = S.[WERKS]", sql);
        Assert.Contains("WHEN MATCHED AND EXISTS (SELECT S.[LABST] EXCEPT SELECT T.[LABST]) THEN", sql);
        Assert.Contains("UPDATE SET T.[LABST] = S.[LABST]", sql);
        Assert.Contains("WHEN NOT MATCHED BY TARGET THEN", sql);
        Assert.Contains("INSERT ([MATNR], [WERKS], [LABST]) VALUES (S.[MATNR], S.[WERKS], S.[LABST]);", sql);
        Assert.EndsWith("SELECT @@ROWCOUNT;", sql);
    }

    [Fact]
    public void Build_WithoutChangeDetection_UpdatesAllMatchedRows()
    {
        var sql = MergeSqlBuilder.Build("[dbo].[T]", "[#S]", new[] { "K", "V" }, new[] { "K" }, detectChanges: false);

        Assert.Contains("WHEN MATCHED THEN", sql);
        Assert.DoesNotContain("EXCEPT", sql);
    }

    [Fact]
    public void Build_OmitsUpdateClause_WhenAllColumnsAreKeys()
    {
        var sql = MergeSqlBuilder.Build("[dbo].[T]", "[#S]", new[] { "K1", "K2" }, new[] { "K1", "K2" }, detectChanges: true);

        Assert.DoesNotContain("WHEN MATCHED", sql);
        Assert.Contains("WHEN NOT MATCHED BY TARGET THEN", sql);
    }

    [Fact]
    public void Build_EscapesIdentifiers()
    {
        var sql = MergeSqlBuilder.Build("[dbo].[T]", "[#S]", new[] { "K]x", "/BIC/V" }, new[] { "K]x" }, detectChanges: false);

        Assert.Contains("[K]]x]", sql);
        Assert.Contains("[/BIC/V]", sql);
    }

    [Fact]
    public void Build_Throws_WhenKeyIsNotAColumn()
    {
        Assert.Throws<ArgumentException>(() =>
            MergeSqlBuilder.Build("[dbo].[T]", "[#S]", new[] { "A" }, new[] { "B" }, detectChanges: true));
    }
}
