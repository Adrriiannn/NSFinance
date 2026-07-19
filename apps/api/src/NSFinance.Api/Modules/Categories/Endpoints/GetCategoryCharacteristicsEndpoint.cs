using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Endpoints;

public sealed record CategoryCharacteristicsResponse(
    int Version,
    IReadOnlyList<CategoryCharacteristicsEntryDto> Entries);

public sealed record CategoryCharacteristicsEntryDto(
    int? TaxonomyCategoryId,
    int? TaxonomySubcategoryId,
    string Description,
    IReadOnlyList<string> UseCases,
    IReadOnlyList<string> InclusionRules,
    IReadOnlyList<string> ExclusionRules,
    IReadOnlyList<string> MerchantSignals,
    string DirectionExpectation,
    string AnalyticsTreatment,
    double? ConfidenceFloor,
    string? AmountProfile);

public static class GetCategoryCharacteristicsEndpoint
{
    private static string ToWireDirection(CharacteristicsDirection direction) => direction switch
    {
        CharacteristicsDirection.Outflow => "outflow",
        CharacteristicsDirection.Inflow => "inflow",
        _ => "either"
    };

    private static string ToWireTreatment(CharacteristicsAnalyticsTreatment treatment) => treatment switch
    {
        CharacteristicsAnalyticsTreatment.Expense => "expense",
        CharacteristicsAnalyticsTreatment.Income => "income",
        CharacteristicsAnalyticsTreatment.NeutralTransfer => "neutral_transfer",
        _ => "balance_adjustment"
    };

    public static IResult Handle()
    {
        var entries = CategoryCharacteristicsCatalog.Definitions
            .Select(definition => new CategoryCharacteristicsEntryDto(
                definition.TaxonomyCategoryId,
                definition.TaxonomySubcategoryId,
                definition.Description,
                definition.UseCases,
                definition.InclusionRules,
                definition.ExclusionRules,
                definition.MerchantSignals,
                ToWireDirection(definition.DirectionExpectation),
                ToWireTreatment(definition.AnalyticsTreatment),
                definition.ConfidenceFloor,
                definition.AmountProfile))
            .ToList();

        return Results.Ok(new CategoryCharacteristicsResponse(
            CategoryCharacteristicsCatalog.Version,
            entries));
    }
}
