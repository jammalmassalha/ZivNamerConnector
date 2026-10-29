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

    // ============ Dual-Date Delta Sync RFC Tests ============

    [Fact]
    public void Split_DualDateFilter_ForNPAT_FitsIntoRfcLines()
    {
        // Typical NPAT dual-date filter
        var filter = "( ERDAT >= '20261027' OR UPDAT >= '20261027' )";

        var lines = RfcOptionsBuilder.Split(filter);

        Assert.Single(lines);
        Assert.Equal(filter, lines[0]);
        Assert.InRange(lines[0].Length, 1, 72);
    }

    [Fact]
    public void Split_DualDateFilter_WithCancellationFlag_FitsIntoRfcLines()
    {
        // NBEW dual-date filter with cancellation check
        var filter = "( BWIDT >= '20261027' OR BWEDT >= '20261027' ) AND STORN = ' '";

        var lines = RfcOptionsBuilder.Split(filter);

        Assert.Single(lines);
        Assert.Equal(filter, lines[0]);
        Assert.InRange(lines[0].Length, 1, 72);
    }

    [Fact]
    public void Split_ComplexDualDateFilter_WithMultipleCancellationFlags_FitsIntoRfcLines()
    {
        // N_FALL_BEW dual-date filter with multiple cancellation flags
        var filter = "STORN = ' ' AND FSTORN = ' ' AND ( BWIDT >= '20261027' OR BWEDT >= '20261027' )";

        var lines = RfcOptionsBuilder.Split(filter);

        // Complex filter may span multiple lines
        Assert.True(lines.Count >= 1);
        Assert.All(lines, l => Assert.InRange(l.Length, 1, 72));
        
        // Verify it can be reconstructed
        var reconstructed = string.Join(" ", lines);
        Assert.Contains("STORN = ' '", reconstructed);
        Assert.Contains("FSTORN = ' '", reconstructed);
        Assert.Contains("BWIDT >=", reconstructed);
        Assert.Contains("BWEDT >=", reconstructed);
    }

    [Fact]
    public void Split_AllDateFieldsPreserved_InDualDateFilter()
    {
        var filter = "( ERDAT >= '20261027' OR UPDAT >= '20261027' )";

        var lines = RfcOptionsBuilder.Split(filter);

        var joined = string.Join(" ", lines);
        Assert.Contains("ERDAT", joined);
        Assert.Contains("UPDAT", joined);
        Assert.Contains("20261027", joined);
    }

    [Fact]
    public void Split_ZeroDateFormat_PreservedInFilter()
    {
        // Empty/unchanged dates in SAP may be '00000000'
        var filter = "( ERDAT >= '00000000' OR UPDAT >= '00000000' )";

        var lines = RfcOptionsBuilder.Split(filter);

        Assert.Single(lines);
        Assert.Contains("'00000000'", lines[0]);
    }

    [Fact]
    public void Split_ParenthesesAndBooleanOperators_PreservedInDeltaFilter()
    {
        var filter = "( ERDAT >= '20261027' OR UPDAT >= '20261027' ) AND STORN = ' '";

        var lines = RfcOptionsBuilder.Split(filter);

        var joined = string.Join(" ", lines);
        Assert.Contains("(", joined);
        Assert.Contains(")", joined);
        Assert.Contains(" OR ", joined);
        Assert.Contains(" AND ", joined);
    }
}

