using System.Globalization;

namespace NSFinance.Api.Modules.AI.Services;

internal static class FinancialAdviceFormatting
{
    public static string FormatRatio(decimal ratio)
    {
        return $"{Math.Round(ratio, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)}x";
    }

    public static string FormatPercentage(decimal ratio)
    {
        return (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    public static string FormatCurrency(decimal value, string? currency)
    {
        var prefix = string.IsNullOrWhiteSpace(currency) ? string.Empty : $"{currency} ";
        return prefix + Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
