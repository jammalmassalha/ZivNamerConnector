namespace SapSync.Infrastructure.Persistence;

internal static class SqlIdentifier
{
    public const int MaxLength = 128;

    /// <summary>Equivalent of T-SQL QUOTENAME: wraps in brackets and escapes ']'.</summary>
    public static string Quote(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.Length > MaxLength)
        {
            throw new ArgumentException($"'{identifier}' is not a valid SQL Server identifier.", nameof(identifier));
        }

        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }
}
