using SapSync.Infrastructure.Sap;

namespace SapSync.Tests;

public class RfcOptionsBuilderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_ReturnsNoLines_ForEmptyFilter(string? filter)
    {
        Assert.Empty(RfcOptionsBuilder.Split(filter));
    }

    [Fact]
    public void Split_KeepsShortFilterOnSingleLine()
    {
        var lines = RfcOptionsBuilder.Split("MTART = 'FERT'");

        Assert.Equal(new[] { "MTART = 'FERT'" }, lines);
    }

    [Fact]
    public void Split_ProducesLinesOfAtMost72Characters_BreakingOnlyBetweenTokens()
    {
        var filter = string.Join(" OR ", Enumerable.Range(0, 30).Select(i => $"MATNR = '{i:D18}'"));

        var lines = RfcOptionsBuilder.Split(filter);

        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.InRange(l.Length, 1, 72));
        Assert.Equal(filter, string.Join(" ", lines));
    }

    [Fact]
    public void Split_DoesNotBreakInsideQuotedLiteralsAndPreservesTheirWhitespace()
    {
        var literal = "'A  B C D E F G H I J K L M N O P Q R S T'";
        var filter = new string('X', 60) + " = " + literal;

        var lines = RfcOptionsBuilder.Split(filter);

        Assert.Contains(literal, lines);
        Assert.All(lines, l => Assert.True(l.Length <= 72));
    }

    [Fact]
    public void Split_HandlesEscapedQuotesInsideLiterals()
    {
        var lines = RfcOptionsBuilder.Split("MAKTX = 'O''BRIEN  LTD'", 15);

        Assert.Equal(new[] { "MAKTX =", "'O''BRIEN  LTD'" }, lines);
    }

    [Fact]
    public void Split_NormalizesLineBreaksAndRepeatedWhitespace()
    {
        var lines = RfcOptionsBuilder.Split("MTART = 'FERT'\r\n   AND\tWERKS = '1000'");

        Assert.Equal(new[] { "MTART = 'FERT' AND WERKS = '1000'" }, lines);
    }

    [Fact]
    public void Split_HardSplitsTokensLongerThanTheLineLimit()
    {
        var token = new string('A', 150);

        var lines = RfcOptionsBuilder.Split(token + " = 1");

        Assert.Equal(new[] { new string('A', 72), new string('A', 72), "AAAAAA = 1" }, lines);
    }
}
