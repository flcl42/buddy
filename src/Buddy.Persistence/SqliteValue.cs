using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence;

internal static class SqliteValue
{
    public static string Date(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    public static object NullableDate(DateTimeOffset? value)
    {
        return value.HasValue ? Date(value.Value) : DBNull.Value;
    }

    public static DateTimeOffset ReadDate(SqliteDataReader reader, int ordinal)
    {
        return DateTimeOffset.Parse(
            reader.GetString(ordinal),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    public static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);
    }

    public static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
