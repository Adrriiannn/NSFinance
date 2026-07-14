using System.Text.Json;

namespace NSFinance.Api.Modules.Transactions.Services;

internal static class TransactionPageCursorCodec
{
    private const int CurrentVersion = 1;
    private const int MaximumEncodedLength = 2048;

    public static string Encode(DateTime bookedAtUtc, DateTime createdUtc, Guid id)
    {
        var payload = new CursorPayload(
            CurrentVersion,
            bookedAtUtc.ToUniversalTime().Ticks,
            createdUtc.ToUniversalTime().Ticks,
            id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? encoded, out TransactionPageCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > MaximumEncodedLength)
        {
            return false;
        }

        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 = (base64.Length % 4) switch
            {
                0 => base64,
                2 => base64 + "==",
                3 => base64 + "=",
                _ => throw new FormatException("Invalid Base64URL cursor length.")
            };

            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(base64));
            if (payload is null || payload.Version != CurrentVersion || payload.Id == Guid.Empty)
            {
                return false;
            }

            cursor = new TransactionPageCursor(
                new DateTime(payload.BookedAtUtcTicks, DateTimeKind.Utc),
                new DateTime(payload.CreatedUtcTicks, DateTimeKind.Utc),
                payload.Id);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed record CursorPayload(
        int Version,
        long BookedAtUtcTicks,
        long CreatedUtcTicks,
        Guid Id);
}

internal readonly record struct TransactionPageCursor(
    DateTime BookedAtUtc,
    DateTime CreatedUtc,
    Guid Id);
