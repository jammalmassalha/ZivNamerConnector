using System.Text;

namespace SapSync.Infrastructure.Sap;

/// <summary>
/// Splits an ABAP WHERE clause into lines for the <c>OPTIONS</c> table of <c>RFC_READ_TABLE</c> (TEXT is CHAR 72).
/// Lines are broken only between tokens; whitespace inside single-quoted literals is preserved and never used
/// as a break point. Tokens longer than the line limit are hard-split as a last resort.
/// </summary>
internal static class RfcOptionsBuilder
{
    public const int MaxLineLength = 72;

    public static IReadOnlyList<string> Split(string? filterCondition, int maxLineLength = MaxLineLength)
    {
        if (maxLineLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLineLength));
        }

        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(filterCondition))
        {
            return lines;
        }

        var current = new StringBuilder(maxLineLength);
        foreach (var token in Tokenize(filterCondition))
        {
            if (current.Length > 0 && current.Length + 1 + token.Length <= maxLineLength)
            {
                current.Append(' ').Append(token);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            var remaining = token.AsSpan();
            while (remaining.Length > maxLineLength)
            {
                lines.Add(remaining[..maxLineLength].ToString());
                remaining = remaining[maxLineLength..];
            }

            current.Append(remaining);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder();
        var inLiteral = false;

        foreach (var c in text)
        {
            if (c == '\'')
            {
                // Toggling on every quote also handles escaped quotes ('') inside literals.
                inLiteral = !inLiteral;
                token.Append(c);
            }
            else if (!inLiteral && char.IsWhiteSpace(c))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
            }
            else
            {
                token.Append(c);
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }
}
