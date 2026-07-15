using System.Text;

namespace NSFinance.Api.Modules.Imports.Services;

internal static class StatementImportRowCursor
{
    private const string Prefix = "v1:";
    private const int MaximumCursorLength = 32;

    public static string Encode(int rowNumber)
    {
        var payload = Encoding.ASCII.GetBytes($"{Prefix}{rowNumber}");
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? cursor, out int rowNumber)
    {
        rowNumber = 0;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        var normalized = cursor.Trim();
        if (normalized.Length > MaximumCursorLength
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var base64 = normalized.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var payload = Encoding.ASCII.GetString(Convert.FromBase64String(base64));
            return payload.StartsWith(Prefix, StringComparison.Ordinal)
                && int.TryParse(payload.AsSpan(Prefix.Length), out rowNumber)
                && rowNumber > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
