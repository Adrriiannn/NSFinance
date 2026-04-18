using System.Globalization;
using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionProfileBaselineBuilder
{
    CompanionProfileBaseline Build(UserFinancialContextSnapshot profile);
}

public sealed class CompanionProfileBaselineBuilder : ICompanionProfileBaselineBuilder
{
    public CompanionProfileBaseline Build(UserFinancialContextSnapshot profile)
    {
        var baselineSpendByDomain = ParseSpendByDomain(profile.SpendingTendenciesJson);
        var baselineAvgDailySpend = ParseDecimalProperty(
            profile.SpendingTendenciesJson,
            "averageDailySpend");
        var baselineRecurring = ParseRecurringMonthlyTotal(profile.KnownObligationsJson);
        var planState = ParseActivePlans(profile.ActivePlansJson);
        var protectedPreferences = ParseProtectedPreferenceHints(
            profile.CategoryFlexibilityMarkersJson);

        return new CompanionProfileBaseline(
            BaselineSpendByDomain: baselineSpendByDomain,
            BaselineAverageDailySpend: baselineAvgDailySpend,
            BaselineRecurringMonthlyTotal: baselineRecurring,
            ActivePlanExpectedSpendTotal: planState.TotalExpectedSpend,
            ActivePlanCount: planState.PlanCount,
            ProtectedPreferenceHints: protectedPreferences);
    }

    private static Dictionary<int, decimal> ParseSpendByDomain(string? json)
    {
        if (!TryParseJsonObject(json, out var root))
        {
            return [];
        }

        if (!TryGetPropertyCaseInsensitive(root, "spendByDomain", out var spendByDomain)
            || spendByDomain.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<int, decimal>();
        foreach (var property in spendByDomain.EnumerateObject())
        {
            if (!int.TryParse(
                    property.Name,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var domainCode))
            {
                continue;
            }

            if (TryReadDecimal(property.Value, out var amount) && amount > 0m)
            {
                result[domainCode] = amount;
            }
        }

        return result;
    }

    private static decimal? ParseDecimalProperty(string? json, string propertyName)
    {
        if (!TryParseJsonObject(json, out var root))
        {
            return null;
        }

        if (!TryGetPropertyCaseInsensitive(root, propertyName, out var property))
        {
            return null;
        }

        return TryReadDecimal(property, out var value) ? value : null;
    }

    private static decimal ParseRecurringMonthlyTotal(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0m;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return 0m;
            }

            decimal total = 0m;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!TryGetPropertyCaseInsensitive(item, "amount", out var amountElement)
                    || !TryReadDecimal(amountElement, out var amount))
                {
                    continue;
                }

                var frequencyDays = 30m;
                if (TryGetPropertyCaseInsensitive(item, "frequencyDays", out var frequencyElement)
                    && TryReadDecimal(frequencyElement, out var parsedFrequency)
                    && parsedFrequency > 0m)
                {
                    frequencyDays = parsedFrequency;
                }

                total += amount * (30m / frequencyDays);
            }

            return Math.Round(total, 2, MidpointRounding.AwayFromZero);
        }
        catch
        {
            return 0m;
        }
    }

    private static ParsedPlanState ParseActivePlans(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParsedPlanState(0m, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new ParsedPlanState(0m, 0);
            }

            var total = 0m;
            var count = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (TryGetPropertyCaseInsensitive(item, "expectedSpendTotal", out var valueElement)
                    && TryReadDecimal(valueElement, out var expected))
                {
                    total += Math.Abs(expected);
                }

                count += 1;
            }

            return new ParsedPlanState(
                TotalExpectedSpend: Math.Round(total, 2, MidpointRounding.AwayFromZero),
                PlanCount: count);
        }
        catch
        {
            return new ParsedPlanState(0m, 0);
        }
    }

    private static IReadOnlyList<string> ParseProtectedPreferenceHints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var values = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values.Add(text.Trim());
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object
                         && TryGetPropertyCaseInsensitive(item, "tag", out var tagElement)
                         && tagElement.ValueKind == JsonValueKind.String)
                {
                    var tag = tagElement.GetString();
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        values.Add(tag.Trim());
                    }
                }
            }

            return values;
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParseJsonObject(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPropertyCaseInsensitive(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value;
            return true;
        }

        return false;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
    }

    private sealed record ParsedPlanState(decimal TotalExpectedSpend, int PlanCount);
}
