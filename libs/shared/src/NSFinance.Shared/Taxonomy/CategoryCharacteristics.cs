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

        // Savings > General Savings Transfer - inflow side. Money arriving
        // from your own external savings is a neutral transfer, never income;
        // leaving it unlabelled would inflate income aggregates.
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 180102,
            Description: "Money you moved back from savings you hold elsewhere.",
            UseCases:
            [
                "Withdrawing from an external savings vault to your current account",
                "Moving saved money back for a planned purchase"
            ],
            InclusionRules:
            [
                "Direction is inflow",
                "Reference names the user's own savings: savings keyword, vault name, or own-name reference",
                "No linked counterpart account exists"
            ],
            ExclusionRules:
            [
                "A linked-account counterpart pairing exists: Internal Transfer decides deterministically",
                "Employer or third-party references are income, not savings returns"
            ],
            MerchantSignals:
            [
                "SAVINGS",
                "VAULT",
                "MOBI SAVINGS"
            ],
            DirectionExpectation: CharacteristicsDirection.Inflow,
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
            ConfidenceFloor: null),

        // Transport > Fuel & Charging
        new(
            TaxonomyCategoryId: 12020,
            TaxonomySubcategoryId: null,
            Description: "Fuel and EV charging for your own vehicle.",
            UseCases:
            [
                "Filling the tank at a forecourt",
                "Motorway service station top-up",
                "Public EV charge session"
            ],
            InclusionRules:
            [
                "Merchant is a fuel forecourt or charging network",
                "Direction is outflow",
                "Amount fits a refuel profile rather than a small shop"
            ],
            ExclusionRules:
            [
                "Forecourt purchases with small amounts and food signals belong to Groceries or Dining",
                "Tolls and parking belong to their transport categories"
            ],
            MerchantSignals:
            [
                "CIRCLE K",
                "APPLEGREEN",
                "MAXOL",
                "TEXACO",
                "ESB ECARS",
                "IONITY"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per fill, typically 30-110 EUR"),

        // Food & Dining > Dining Out
        new(
            TaxonomyCategoryId: 13020,
            TaxonomySubcategoryId: null,
            Description: "Prepared food and drink you did not cook: restaurants, takeaway, delivery.",
            UseCases:
            [
                "Restaurant or cafe meal",
                "Takeaway or delivery order",
                "Lunch on the go"
            ],
            InclusionRules:
            [
                "Merchant prepares food or drink for immediate consumption",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Supermarket food belongs to Groceries",
                "Hotel stays with dining folios belong to travel categories"
            ],
            MerchantSignals:
            [
                "MCDONALDS",
                "SUPERMACS",
                "BOOJUM",
                "DELIVEROO",
                "JUST EAT",
                "UBER EATS",
                "STARBUCKS",
                "INSOMNIA"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 5-80 EUR"),

        // Utilities > Electricity
        new(
            TaxonomyCategoryId: 14010,
            TaxonomySubcategoryId: null,
            Description: "Electricity supply for your home.",
            UseCases:
            [
                "Monthly or bi-monthly electricity bill",
                "Prepay electricity top-up"
            ],
            InclusionRules:
            [
                "Merchant is an electricity supplier",
                "Direction is outflow",
                "Cadence is recurring monthly or bi-monthly"
            ],
            ExclusionRules:
            [
                "Dual-fuel gas charges belong to Gas when itemized separately",
                "EV charging networks belong to Fuel & Charging"
            ],
            MerchantSignals:
            [
                "ELECTRIC IRELAND",
                "SSE AIRTRICITY",
                "BORD GAIS ENERGY",
                "ENERGIA",
                "PINERGY",
                "PREPAYPOWER"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Recurring, typically 60-260 EUR per bill"),

        // Subscriptions > Streaming & Media
        new(
            TaxonomyCategoryId: 28010,
            TaxonomySubcategoryId: null,
            Description: "Recurring entertainment and media subscriptions.",
            UseCases:
            [
                "Monthly video or music streaming charge",
                "Annual media membership renewal"
            ],
            InclusionRules:
            [
                "Merchant is a streaming or media service",
                "Direction is outflow",
                "Amount repeats on a monthly or annual cadence"
            ],
            ExclusionRules:
            [
                "One-off digital purchases and rentals are shopping, not subscriptions",
                "Telecoms bundles that include TV belong to their utilities categories"
            ],
            MerchantSignals:
            [
                "NETFLIX",
                "SPOTIFY",
                "DISNEY",
                "PRIME VIDEO",
                "YOUTUBE PREMIUM",
                "NOW TV",
                "APPLE.COM/BILL"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Recurring, typically 5-25 EUR monthly"),

        // Income > Salary
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 910101,
            Description: "Pay from your employer.",
            UseCases:
            [
                "Monthly, fortnightly, or weekly payroll credit",
                "Back-pay or bonus from the same employer"
            ],
            InclusionRules:
            [
                "Direction is inflow",
                "Reference carries payroll signals or a recurring employer name",
                "Amount repeats on a payroll cadence"
            ],
            ExclusionRules:
            [
                "Transfers from your own accounts are never salary",
                "State supports and refunds belong to their own income categories"
            ],
            MerchantSignals:
            [
                "SALARY",
                "PAYROLL",
                "WAGES",
                "PAYE"
            ],
            DirectionExpectation: CharacteristicsDirection.Inflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Income,
            ConfidenceFloor: 0.8),

        // Housing > Rent
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 100101,
            Description: "Rent paid for your home.",
            UseCases:
            [
                "Monthly rent standing order to a landlord or agency",
                "Weekly rent payment"
            ],
            InclusionRules:
            [
                "Direction is outflow",
                "Amount repeats on a monthly or weekly cadence",
                "Reference names a landlord, letting agency, or rent keyword"
            ],
            ExclusionRules:
            [
                "Mortgage repayments belong to the mortgage category",
                "Transfers to your own accounts are never rent"
            ],
            MerchantSignals:
            [
                "RENT",
                "LETTING",
                "PROPERTY MANAGEMENT",
                "DAFT"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.85,
            AmountProfile: "Recurring, typically 600-2500 EUR monthly"),

        // Transport > Public Transport
        new(
            TaxonomyCategoryId: 12010,
            TaxonomySubcategoryId: null,
            Description: "Buses, trains, trams, and travel cards.",
            UseCases:
            [
                "Leap card top-up",
                "Train or bus ticket",
                "Monthly commuter ticket"
            ],
            InclusionRules:
            [
                "Merchant is a public transport operator or travel-card scheme",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Taxis and ride-hailing belong to their own transport category",
                "Fuel belongs to Fuel & Charging"
            ],
            MerchantSignals:
            [
                "LEAP",
                "TFI",
                "IRISH RAIL",
                "IARNROD EIREANN",
                "DUBLIN BUS",
                "BUS EIREANN",
                "LUAS",
                "GO-AHEAD"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Per trip or top-up, typically 2-120 EUR"),

        // Insurance > Health Insurance
        new(
            TaxonomyCategoryId: 15010,
            TaxonomySubcategoryId: null,
            Description: "Private health insurance premiums.",
            UseCases:
            [
                "Monthly health plan premium",
                "Annual policy renewal"
            ],
            InclusionRules:
            [
                "Merchant is a health insurer",
                "Direction is outflow",
                "Cadence is monthly or annual"
            ],
            ExclusionRules:
            [
                "Car, home, and travel policies belong to their own insurance categories",
                "GP, pharmacy, and hospital charges are health expenses, not premiums"
            ],
            MerchantSignals:
            [
                "VHI",
                "LAYA",
                "IRISH LIFE HEALTH",
                "LEVELHEALTH"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.85,
            AmountProfile: "Recurring, typically 40-300 EUR monthly"),

        // Utilities > Internet & Mobile
        new(
            TaxonomyCategoryId: 14040,
            TaxonomySubcategoryId: null,
            Description: "Home broadband and mobile phone plans.",
            UseCases:
            [
                "Monthly broadband bill",
                "Mobile plan charge or prepay top-up",
                "TV and broadband bundle"
            ],
            InclusionRules:
            [
                "Merchant is a telecoms provider",
                "Direction is outflow",
                "Amount repeats monthly or is a recognizable top-up"
            ],
            ExclusionRules:
            [
                "Standalone streaming services belong to Streaming & Media",
                "Device purchases are shopping, not the plan"
            ],
            MerchantSignals:
            [
                "EIR",
                "VODAFONE",
                "THREE",
                "GOMO",
                "48.IE",
                "SKY",
                "VIRGIN MEDIA",
                "TESCO MOBILE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Recurring, typically 10-120 EUR monthly")
    ];
}
