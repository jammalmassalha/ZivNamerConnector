using System.Globalization;

namespace SapSync.Infrastructure.Sap;

/// <summary>Maps ABAP data types (the TYPE column of <c>RFC_READ_TABLE</c> FIELDS) to CLR types and values.</summary>
internal static class SapValueConverter
{
    private const NumberStyles IntegerStyle = NumberStyles.Integer | NumberStyles.AllowTrailingSign;

    public static Type GetClrType(string abapType, bool convertAbapTypes)
    {
        if (!convertAbapTypes)
        {
            return typeof(string);
        }

        return abapType switch
        {
            "D" => typeof(DateTime),
            "T" => typeof(TimeSpan),
            "I" or "b" or "s" => typeof(int),
            "8" => typeof(long),
            "P" => typeof(decimal),
            "F" => typeof(double),
            _ => typeof(string), // C, N (NUMC keeps leading zeros), X, g, y, ...
        };
    }

    public static object Convert(string? raw, string abapType, bool convertAbapTypes)
    {
        var type = GetClrType(abapType, convertAbapTypes);
        if (type == typeof(string))
        {
            // ABAP character semantics: trailing blanks are not significant.
            return raw?.TrimEnd() ?? string.Empty;
        }

        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return DBNull.Value;
        }

        switch (abapType)
        {
            case "D":
                // Initial ("00000000") or invalid dates have no SQL equivalent.
                return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : DBNull.Value;

            case "T":
                return TimeSpan.TryParseExact(value, "hhmmss", CultureInfo.InvariantCulture, out var time)
                    ? time
                    : DBNull.Value;

            case "I":
            case "b":
            case "s":
                return int.TryParse(value, IntegerStyle, CultureInfo.InvariantCulture, out var i) ? i : throw InvalidValue(value, abapType);

            case "8":
                return long.TryParse(value, IntegerStyle, CultureInfo.InvariantCulture, out var l) ? l : throw InvalidValue(value, abapType);

            case "P":
                // Packed numbers are rendered with a trailing sign, e.g. "123.45-".
                return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : throw InvalidValue(value, abapType);

            case "F":
                return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowTrailingSign, CultureInfo.InvariantCulture, out var f)
                    ? f
                    : throw InvalidValue(value, abapType);

            default:
                return value;
        }
    }

    private static FormatException InvalidValue(string value, string abapType) =>
        new($"Value '{value}' cannot be converted from ABAP type '{abapType}'.");
}
