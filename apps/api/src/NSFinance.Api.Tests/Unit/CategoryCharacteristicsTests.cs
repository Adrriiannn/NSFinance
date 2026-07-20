using NSFinance.Api.Modules.Categories.Endpoints;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Tests.Unit;

public sealed class CategoryCharacteristicsTests
{
    [Fact]
    public void EveryDefinition_TargetsExactlyOneExistingTaxonomyNode()
    {
        var domains = NSFinanceTaxonomyCatalog.Instance.Domains;
        var categoryIds = domains
            .SelectMany(domain => domain.Categories)
            .Select(category => category.Id)
            .ToHashSet();
        var subcategoryIds = domains
            .SelectMany(domain => domain.Categories)
            .SelectMany(category => category.Subcategories)
            .Select(subcategory => subcategory.Id)
            .ToHashSet();

        Assert.NotEmpty(CategoryCharacteristicsCatalog.Definitions);

        foreach (var definition in CategoryCharacteristicsCatalog.Definitions)
        {
            var targetsCategory = definition.TaxonomyCategoryId.HasValue;
            var targetsSubcategory = definition.TaxonomySubcategoryId.HasValue;
            Assert.True(
                targetsCategory ^ targetsSubcategory,
                "a definition must target exactly one taxonomy node");

            if (targetsCategory)
            {
                Assert.Contains(definition.TaxonomyCategoryId!.Value, categoryIds);
            }
            else
            {
                Assert.Contains(definition.TaxonomySubcategoryId!.Value, subcategoryIds);
            }
        }
    }

    [Fact]
    public void Definitions_KeepContractInvariants()
    {
        foreach (var definition in CategoryCharacteristicsCatalog.Definitions)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            Assert.NotEmpty(definition.UseCases);
            Assert.NotEmpty(definition.InclusionRules);

            if (definition.ConfidenceFloor is { } floor)
            {
                Assert.InRange(floor, 0.5, 0.99);
            }
            else
            {
                // Deterministic-only categories must say so in their rules.
                Assert.Contains(
                    definition.InclusionRules,
                    rule => rule.Contains("deterministic", StringComparison.OrdinalIgnoreCase));
            }

            if (definition.AnalyticsTreatment == CharacteristicsAnalyticsTreatment.NeutralTransfer)
            {
                Assert.Null(definition.AmountProfile);
            }
        }
    }

    [Fact]
    public void MerchantSignals_AreUniqueAcrossDefinitions()
    {
        // A signal owned by two definitions would make matching ambiguous
        // (longest-pattern would decide arbitrarily between categories).
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var definition in CategoryCharacteristicsCatalog.Definitions)
        {
            var owner = definition.TaxonomySubcategoryId?.ToString()
                ?? definition.TaxonomyCategoryId?.ToString()
                ?? "?";
            foreach (var signal in definition.MerchantSignals)
            {
                var normalized = signal.Trim().ToUpperInvariant();
                Assert.False(
                    seen.TryGetValue(normalized, out var existingOwner) && existingOwner != owner,
                    $"signal '{normalized}' is claimed by both {seen.GetValueOrDefault(normalized)} and {owner}");
                seen[normalized] = owner;
            }
        }
    }

    [Fact]
    public void Endpoint_SerializesCatalogWithVersionAndWireEnums()
    {
        var result = GetCategoryCharacteristicsEndpoint.Handle();
        var ok = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<CategoryCharacteristicsResponse>>(result);
        var response = ok.Value!;

        Assert.Equal(CategoryCharacteristicsCatalog.Version, response.Version);
        Assert.Equal(CategoryCharacteristicsCatalog.Definitions.Count, response.Entries.Count);
        Assert.All(response.Entries, entry =>
        {
            Assert.Contains(entry.DirectionExpectation, new[] { "outflow", "inflow", "either" });
            Assert.Contains(
                entry.AnalyticsTreatment,
                new[] { "expense", "income", "neutral_transfer", "balance_adjustment" });
        });
        Assert.Contains(response.Entries, entry => entry.AnalyticsTreatment == "neutral_transfer");
    }
}
