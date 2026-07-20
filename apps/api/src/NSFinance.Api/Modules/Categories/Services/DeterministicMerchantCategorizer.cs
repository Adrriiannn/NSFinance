using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

// CAT-001 deterministic pass: matches normalized statement text against the
// category characteristics catalog's merchant signals. Pure and side-effect
// free - callers decide what to do with a match. AI assignment only sees
// transactions this pass could not decide.

public sealed record DeterministicCategoryMatch(
    int? TaxonomyCategoryId,
    int? TaxonomySubcategoryId,
    string MatchedSignal,
    int CharacteristicsVersion);

public static class DeterministicMerchantCategorizer
{
    // Mirrors the mobile display normalizer's rules server-side.
    public static string NormalizeStatementText(string rawDescription)
    {
        var text = (rawDescription ?? string.Empty).Trim().TrimStart('*').Trim();
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^(?:VDC|VDP|POSC?|CNC)[-* ]\s*",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ");
        return text.ToUpperInvariant();
    }

    public static DeterministicCategoryMatch? Match(string rawDescription, decimal amount)
    {
        var normalized = NormalizeStatementText(rawDescription);
        if (normalized.Length < 2)
        {
            return null;
        }

        DeterministicCategoryMatch? best = null;
        var bestSignalLength = 0;

        foreach (var definition in CategoryCharacteristicsCatalog.Definitions)
        {
            // Deterministic-only relationship categories (null floor with empty
            // signals) are decided by the relationship engine, never by text.
            if (definition.MerchantSignals.Count == 0)
            {
                continue;
            }

            var directionSatisfied = definition.DirectionExpectation switch
            {
                CharacteristicsDirection.Outflow => amount < 0,
                CharacteristicsDirection.Inflow => amount > 0,
                _ => true
            };

            if (!directionSatisfied)
            {
                continue;
            }

            foreach (var signal in definition.MerchantSignals)
            {
                var normalizedSignal = signal.ToUpperInvariant();
                if (!normalized.Contains(normalizedSignal, StringComparison.Ordinal))
                {
                    continue;
                }

                // Longest signal wins so "IRISH LIFE HEALTH" beats "LIFE".
                if (normalizedSignal.Length > bestSignalLength)
                {
                    bestSignalLength = normalizedSignal.Length;
                    best = new DeterministicCategoryMatch(
                        definition.TaxonomyCategoryId,
                        definition.TaxonomySubcategoryId,
                        signal,
                        CategoryCharacteristicsCatalog.Version);
                }
            }
        }

        return best;
    }
}
