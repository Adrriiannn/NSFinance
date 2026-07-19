namespace NSFinance.Shared.Taxonomy;

// Category characteristics (TAX-001), implementing the accepted Category
// Characteristics Contract: per-category descriptions, use-cases, testable
// inclusion/exclusion rules, merchant signals, direction expectations, and
// truth-protecting analytics treatments that constrained AI assignment
// (CAT-001) judges against. The catalog lives beside the taxonomy it
// describes and is versioned; every AI assignment records the version used.

public enum CharacteristicsDirection
{
    Outflow,
    Inflow,
    Either
}

public enum CharacteristicsAnalyticsTreatment
{
    Expense,
    Income,
    NeutralTransfer,
    BalanceAdjustment
}

public sealed record CategoryCharacteristicsDefinition(
    int? TaxonomyCategoryId,
    int? TaxonomySubcategoryId,
    string Description,
    IReadOnlyList<string> UseCases,
    IReadOnlyList<string> InclusionRules,
    IReadOnlyList<string> ExclusionRules,
    IReadOnlyList<string> MerchantSignals,
    CharacteristicsDirection DirectionExpectation,
    CharacteristicsAnalyticsTreatment AnalyticsTreatment,
    // Null for deterministic-only categories that AI may label but never infer.
    double? ConfidenceFloor,
    string? AmountProfile = null);

public static class CategoryCharacteristicsCatalog
{
    public const int Version = 1;

    public static readonly IReadOnlyList<CategoryCharacteristicsDefinition> Definitions =
    [
        // Food & Dining > Groceries
        new(
            TaxonomyCategoryId: 13010,
            TaxonomySubcategoryId: null,
            Description: "Day-to-day supermarket and food shopping.",
            UseCases:
            [
                "Weekly big shop",
                "Top-up shop",
                "Butcher, bakery, or greengrocer visit"
            ],
            InclusionRules:
            [
                "Merchant is a grocery retailer",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Prepared restaurant or takeaway food belongs to Dining",
                "Fuel-station purchases whose amount matches a fuel profile belong to Fuel"
            ],
            MerchantSignals:
            [
                "TESCO",
                "DUNNES",
                "LIDL",
                "ALDI",
                "SUPERVALU",
                "CENTRA",
                "SPAR"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Weekly, typically 20-150 EUR"),

        // Savings > General Savings Transfer (external counterpart)
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 180102,
            Description: "Money you moved into savings you hold elsewhere.",
            UseCases:
            [
                "Monthly standing order to an external savings vault",
                "Round-up sweep to savings",
                "Payday automatic save"
            ],
            InclusionRules:
            [
                "Direction is outflow",
                "Counterpart looks like the user's own savings: own-name reference, savings keyword, or recurring round amount",
                "No linked counterpart account exists"
            ],
            ExclusionRules:
            [
                "A linked-account counterpart pairing exists: Internal Transfer decides deterministically",
                "A third-party payee name is not savings"
            ],
            MerchantSignals:
            [
                "SAVINGS",
                "VAULT",
                "MOBI SAVINGS",
                "REVOLUT"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.NeutralTransfer,
            ConfidenceFloor: 0.8),

        // Transfers > Internal Transfers (deterministic-only)
        new(
            TaxonomyCategoryId: 92010,
            TaxonomySubcategoryId: null,
            Description: "Movement between two of your linked accounts.",
            UseCases:
            [
                "Moving money from current to a linked second account",
                "Balancing between two linked accounts"
            ],
            InclusionRules:
            [
                "A deterministic relationship pairing exists; AI labels the pair but never infers this category without it"
            ],
            ExclusionRules:
            [
                "Without a deterministic pairing, judge savings or external transfer characteristics instead"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Either,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.NeutralTransfer,
            ConfidenceFloor: null)
    ];
}
