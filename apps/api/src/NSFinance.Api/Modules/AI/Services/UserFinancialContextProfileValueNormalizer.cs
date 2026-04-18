using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

internal static class UserFinancialProfileValueNormalizer
{
    public static string NormalizeCountry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "ZZ";
        }

        return raw.Trim().ToUpperInvariant();
    }

    public static string NormalizeCurrency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "EUR";
        }

        var cleaned = raw.Trim().ToUpperInvariant();
        return cleaned.Length == 3 ? cleaned : "EUR";
    }

    public static string NormalizeAdviceStyle(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "conservative" => "conservative",
            "flexible" => "flexible",
            _ => "balanced"
        };
    }

    public static string NormalizeJsonOrDefault(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        try
        {
            _ = JsonDocument.Parse(raw);
            return raw;
        }
        catch
        {
            return fallback;
        }
    }

    public static string? DeriveIncomeRange(decimal incomeLast30Days)
    {
        if (incomeLast30Days <= 0m)
        {
            return null;
        }

        return incomeLast30Days switch
        {
            < 2000m => "0-2000",
            < 4000m => "2000-4000",
            < 7000m => "4000-7000",
            _ => "7000+"
        };
    }
}
