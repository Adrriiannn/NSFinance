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
    // Version 2 (2026-07-20): pass six added ten everyday-Ireland categories
    // and moved the coffee-first cafes from Dining to Coffee Shops. Seeding
    // retargets changed seed rows exactly once per version bump.
    // Version 3 (2026-07-20): pass seven added the digital economy - Gaming,
    // Electronics, Software & Digital Tools, Web Services - written directly
    // from the first live growth run, whose judge verified those merchants
    // and honestly abstained for lack of matching definitions.
    // Version 4 (2026-07-21): no definition changes - bumped so seeding's
    // merged direction semantics propagate: signals shared by the
    // outflow/inflow savings pair retarget to direction "either" (the earlier
    // collapse left savings arrivals uncategorized in production).
    // Version 5+ (2026-07-22): the full-coverage program - every category
    // gets a definition, domain by domain, per the user's completeness
    // directive. "Other ..." catch-all categories deliberately get none: the
    // AI must abstain honestly rather than dump uncertainty into a bucket.
    // Definitions without merchant signals are AI-lane only: no seeds, but
    // the judge may assign them (services whose merchants are too varied to
    // enumerate).
    public const int Version = 5;

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
                "Hotel stays with dining folios belong to travel categories",
                "Coffee-first cafes belong to Coffee Shops"
            ],
            MerchantSignals:
            [
                "MCDONALDS",
                "SUPERMACS",
                "BOOJUM",
                "DELIVEROO",
                "JUST EAT",
                "UBER EATS"
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
            AmountProfile: "Recurring, typically 10-120 EUR monthly"),

        // Transport > Air Travel > Flights
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 120801,
            Description: "Flight tickets and airline booking fees.",
            UseCases:
            [
                "Booking a flight with an airline",
                "Flight purchased through a booking site"
            ],
            InclusionRules:
            [
                "Merchant is an airline or flight booking service",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Hotels and accommodation belong to their travel categories",
                "Airport food and shops belong to Dining or shopping"
            ],
            MerchantSignals:
            [
                "RYANAIR",
                "AER LINGUS",
                "VOLA.RO",
                "SKYSCANNER",
                "KIWI.COM",
                "EDREAMS"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Per booking, typically 30-600 EUR"),

        // Insurance > Motor > Car Insurance
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 150401,
            Description: "Car insurance premiums.",
            UseCases:
            [
                "Monthly motor premium",
                "Annual policy or renewal payment"
            ],
            InclusionRules:
            [
                "Merchant is a motor insurer or broker",
                "Direction is outflow",
                "Cadence is monthly or annual"
            ],
            ExclusionRules:
            [
                "Health and home policies belong to their own insurance categories",
                "Motor tax and NCT fees are motoring costs, not insurance"
            ],
            MerchantSignals:
            [
                "AXA",
                "ALLIANZ",
                "AVIVA",
                "LIBERTY INSURANCE",
                "ITS4WOMEN",
                "CHILL INSURANCE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Recurring, typically 30-200 EUR monthly"),

        // Personal Care > Grooming & Beauty
        new(
            TaxonomyCategoryId: 19010,
            TaxonomySubcategoryId: null,
            Description: "Grooming, beauty, and personal upkeep purchases.",
            UseCases:
            [
                "Barber or salon visit",
                "Grooming products ordered online"
            ],
            InclusionRules:
            [
                "Merchant sells grooming or beauty products or services",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Pharmacy healthcare items belong to health categories",
                "Gym and fitness memberships belong to Gym Membership"
            ],
            MerchantSignals:
            [
                "MANSCAPED",
                "BOOTS",
                "THE BARBER",
                "SALON"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7),

        // Personal Care > Fitness > Gym Membership
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 190401,
            Description: "Recurring gym and fitness memberships.",
            UseCases:
            [
                "Monthly gym direct debit",
                "Annual membership renewal"
            ],
            InclusionRules:
            [
                "Merchant is a gym or fitness studio",
                "Direction is outflow",
                "Amount repeats on a membership cadence"
            ],
            ExclusionRules:
            [
                "One-off class passes and sports gear are not memberships"
            ],
            MerchantSignals:
            [
                "FLYEFIT",
                "GYM PLUS",
                "BEN DUNNE",
                "WESTWOOD",
                "ANYTIME FITNESS"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Recurring, typically 20-80 EUR monthly"),

        // Food & Dining > Coffee Shops > Coffee Shops
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 130301,
            Description: "Coffee-first cafes: takeaway coffee, pastries, a quick sit-in.",
            UseCases:
            [
                "Morning takeaway coffee",
                "Cafe catch-up",
                "Coffee and a pastry"
            ],
            InclusionRules:
            [
                "Merchant is primarily a coffee shop or cafe chain",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Restaurants and takeaway meals belong to Dining",
                "Supermarket coffee beans belong to Groceries"
            ],
            MerchantSignals:
            [
                "STARBUCKS",
                "INSOMNIA",
                "COSTA COFFEE",
                "CAFFE NERO",
                "BUTLERS",
                "ESQUIRES"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 3-15 EUR"),

        // Transport > Public Transport > Taxi / Ride-hailing
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 120108,
            Description: "Taxis and ride-hailing trips.",
            UseCases:
            [
                "Night out taxi home",
                "Airport run",
                "Ride-hailing app trip"
            ],
            InclusionRules:
            [
                "Merchant is a taxi or ride-hailing operator",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Food delivery from the same brands belongs to Dining",
                "Buses, trams, and trains belong to Public Transport"
            ],
            MerchantSignals:
            [
                "FREE NOW",
                "FREENOW",
                "UBER",
                "BOLT.EU"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Per trip, typically 8-40 EUR"),

        // Transport > Parking & Tolls
        new(
            TaxonomyCategoryId: 12050,
            TaxonomySubcategoryId: null,
            Description: "Parking charges and road tolls.",
            UseCases:
            [
                "City car park",
                "Motorway toll top-up",
                "Street parking app payment"
            ],
            InclusionRules:
            [
                "Merchant operates parking facilities or toll roads",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Fines and penalties are not routine parking spend"
            ],
            MerchantSignals:
            [
                "EFLOW",
                "APCOA",
                "Q-PARK",
                "PARKING TAG"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Small, typically 2-30 EUR"),

        // Utilities > Gas & Heating
        new(
            TaxonomyCategoryId: 14020,
            TaxonomySubcategoryId: null,
            Description: "Gas supply and home heating fuel.",
            UseCases:
            [
                "Monthly gas bill",
                "Heating oil or bottled gas refill"
            ],
            InclusionRules:
            [
                "Merchant is a gas or heating fuel supplier",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Dual-fuel brands that also sell electricity stay unassigned here: the bill split is not knowable from the merchant alone",
                "Petrol-station fuel belongs to Fuel"
            ],
            MerchantSignals:
            [
                "FLOGAS",
                "CALOR"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Recurring or seasonal, typically 40-250 EUR"),

        // Utilities > Waste > Bin / Refuse Collection
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 140303,
            Description: "Household bin and refuse collection services.",
            UseCases:
            [
                "Monthly or quarterly bin charges",
                "Lift-fee top-up"
            ],
            InclusionRules:
            [
                "Merchant is a waste collection provider",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Skip hire for renovations belongs to home improvement costs"
            ],
            MerchantSignals:
            [
                "PANDA WASTE",
                "CITY BIN",
                "BARNA RECYCLING",
                "KWD RECYCLING"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Recurring, typically 10-40 EUR"),

        // Home & Garden > DIY & Improvement Supplies
        new(
            TaxonomyCategoryId: 11030,
            TaxonomySubcategoryId: null,
            Description: "DIY, hardware, and home improvement supplies.",
            UseCases:
            [
                "Paint and tools run",
                "Timber or fixings for a project",
                "Builders merchant order"
            ],
            InclusionRules:
            [
                "Merchant sells DIY, hardware, or building supplies",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Furniture and decor belong to furnishing categories",
                "Professional trade labour is a service, not supplies"
            ],
            MerchantSignals:
            [
                "WOODIES",
                "B&Q",
                "SCREWFIX",
                "CHADWICKS"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Per visit, typically 10-200 EUR"),

        // Entertainment > Cinema & Events > Cinema
        new(
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: 210101,
            Description: "Cinema tickets and in-cinema spend.",
            UseCases:
            [
                "Film tickets",
                "Cinema snacks bought at the counter"
            ],
            InclusionRules:
            [
                "Merchant is a cinema operator",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Streaming subscriptions belong to Streaming"
            ],
            MerchantSignals:
            [
                "ODEON",
                "CINEWORLD",
                "OMNIPLEX",
                "IMC CINEMA",
                "VUE CINEMA"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Per visit, typically 8-40 EUR"),

        // Shopping > Clothing
        new(
            TaxonomyCategoryId: 23010,
            TaxonomySubcategoryId: null,
            Description: "Clothing and everyday fashion purchases.",
            UseCases:
            [
                "Wardrobe top-up",
                "Seasonal clothes shop",
                "Online fashion order"
            ],
            InclusionRules:
            [
                "Merchant primarily sells clothing",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Mixed general retailers that also sell groceries stay unassigned here",
                "Sportswear bought for a gym habit is still Clothing unless it is equipment"
            ],
            MerchantSignals:
            [
                "PENNEYS",
                "ZARA",
                "H&M",
                "TK MAXX",
                "NEW LOOK",
                "RIVER ISLAND"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 10-150 EUR"),

        // Pets > Pet Food & Supplies
        new(
            TaxonomyCategoryId: 25010,
            TaxonomySubcategoryId: null,
            Description: "Pet food, supplies, and accessories.",
            UseCases:
            [
                "Monthly pet food shop",
                "Toys, bedding, or litter"
            ],
            InclusionRules:
            [
                "Merchant is a pet supplies retailer",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Vet visits belong to Veterinary Care",
                "Pet insurance belongs to Insurance"
            ],
            MerchantSignals:
            [
                "PETSTOP",
                "PETMANIA",
                "MAXI ZOO",
                "PETWORLD"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Per visit, typically 10-80 EUR"),

        // Health > Prescriptions & Medications
        new(
            TaxonomyCategoryId: 16040,
            TaxonomySubcategoryId: null,
            Description: "Pharmacy purchases: prescriptions and over-the-counter medicine.",
            UseCases:
            [
                "Monthly prescription collection",
                "Over-the-counter remedies"
            ],
            InclusionRules:
            [
                "Merchant is a pharmacy or the statement text names one",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Health-and-beauty chains with large grooming ranges belong to Grooming & Beauty",
                "GP and consultant fees are medical services, not pharmacy spend"
            ],
            MerchantSignals:
            [
                "PHARMACY",
                "LLOYDS PHARMACY",
                "MCCABES",
                "MCCAULEY",
                "CHEMIST"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 5-60 EUR"),

        // Entertainment > Gaming
        new(
            TaxonomyCategoryId: 21020,
            TaxonomySubcategoryId: null,
            Description: "Games and in-game content: storefronts, server platforms, digital purchases.",
            UseCases:
            [
                "Game purchase on a digital storefront",
                "In-game or game-server payment",
                "Console store top-up"
            ],
            InclusionRules:
            [
                "Merchant is a game storefront, platform, or game-content payment processor",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Recurring gaming memberships belong to Gaming Subscription",
                "Gaming hardware belongs to Electronics"
            ],
            MerchantSignals:
            [
                "TEBEX",
                "STEAM",
                "PLAYSTATION",
                "NINTENDO",
                "XBOX",
                "EPIC GAMES",
                "RIOT GAMES"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per purchase, typically 5-80 EUR"),

        // Shopping > Electronics
        new(
            TaxonomyCategoryId: 23030,
            TaxonomySubcategoryId: null,
            Description: "Consumer electronics and appliances from electronics retailers.",
            UseCases:
            [
                "New phone, laptop, or accessory",
                "Home appliance purchase"
            ],
            InclusionRules:
            [
                "Merchant is an electronics or appliance retailer",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Software and app subscriptions belong to Software & Digital Tools",
                "Supermarket purchases belong to Groceries even when they include gadgets"
            ],
            MerchantSignals:
            [
                "CURRYS",
                "HARVEY NORMAN",
                "DID ELECTRICAL",
                "POWER CITY",
                "EXPERT.IE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Per purchase, typically 20-1500 EUR"),

        // Subscriptions > Software & Digital Tools
        new(
            TaxonomyCategoryId: 28020,
            TaxonomySubcategoryId: null,
            Description: "Software, app, and AI tool subscriptions and licences.",
            UseCases:
            [
                "Office or creative suite subscription",
                "AI assistant subscription",
                "Developer tooling plan"
            ],
            InclusionRules:
            [
                "Merchant sells software, apps, or digital productivity tools",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Video and music streaming belong to Streaming",
                "Web hosting and domains belong to Software & Services"
            ],
            MerchantSignals:
            [
                "MICROSOFT",
                "ADOBE",
                "OPENAI",
                "CHATGPT",
                "GITHUB",
                "CANVA"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Recurring, typically 5-60 EUR monthly"),

        // Business Expenses > Software & Services (hosting, domains, web infrastructure)
        new(
            TaxonomyCategoryId: 29030,
            TaxonomySubcategoryId: null,
            Description: "Web infrastructure: hosting, domain names, site builders, cloud services.",
            UseCases:
            [
                "Annual domain renewal",
                "Monthly website hosting",
                "Site builder subscription"
            ],
            InclusionRules:
            [
                "Merchant provides hosting, domains, or web infrastructure",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "General productivity software belongs to Software & Digital Tools"
            ],
            MerchantSignals:
            [
                "GODADDY",
                "BLACKNIGHT",
                "NAMECHEAP",
                "SQUARESPACE",
                "WIX.COM"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Recurring or annual, typically 5-40 EUR"),

        // ---- Full-coverage pass: Housing (100) ----

        // Housing > Rent & Mortgage
        new(
            TaxonomyCategoryId: 10010,
            TaxonomySubcategoryId: null,
            Description: "Keeping a roof: mortgage payments, ground rent, housing association charges.",
            UseCases:
            [
                "Monthly mortgage direct debit",
                "Ground rent or leasehold charge",
                "Room or shared-housing payment"
            ],
            InclusionRules:
            [
                "Payment secures the home you live in",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Rent to a landlord follows the Rent subcategory definition",
                "Property taxes belong to Property Taxes & Fees"
            ],
            MerchantSignals:
            [
                "MORTGAGE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Recurring monthly, typically 800-2500 EUR"),

        // Housing > Property Taxes & Fees
        new(
            TaxonomyCategoryId: 10020,
            TaxonomySubcategoryId: null,
            Description: "Taxes and fees attached to owning or occupying property.",
            UseCases:
            [
                "Local Property Tax payment to Revenue",
                "Management company annual fee",
                "Land registry fee"
            ],
            InclusionRules:
            [
                "Charge exists because of property ownership or occupancy",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Income taxes belong to the Taxes domain",
                "Repairs and improvements belong to their own categories"
            ],
            MerchantSignals:
            [
                "LPT",
                "PROPERTY TAX"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.8,
            AmountProfile: "Annual or phased, typically 90-1000 EUR"),

        // Housing > Home Maintenance & Repairs
        new(
            TaxonomyCategoryId: 10030,
            TaxonomySubcategoryId: null,
            Description: "Keeping the home working: trades, repairs, cleaning, pest control.",
            UseCases:
            [
                "Plumber or electrician call-out",
                "Appliance repair",
                "Home cleaning service"
            ],
            InclusionRules:
            [
                "Service fixes or maintains the existing home",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Materials you buy yourself belong to DIY & Improvement Supplies",
                "Upgrades and renovations belong to Home Improvements"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per job, typically 60-500 EUR"),

        // Housing > Home Improvements & Renovation
        new(
            TaxonomyCategoryId: 10040,
            TaxonomySubcategoryId: null,
            Description: "Making the home better: renovation projects and contractor work.",
            UseCases:
            [
                "Kitchen or bathroom renovation invoice",
                "Insulation or window upgrade",
                "Painter and decorator"
            ],
            InclusionRules:
            [
                "Work upgrades the home beyond its previous state",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Like-for-like fixes belong to Home Maintenance & Repairs",
                "Self-bought materials belong to DIY & Improvement Supplies"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per project, typically 200-20000 EUR"),

        // Housing > Furniture & Appliances
        new(
            TaxonomyCategoryId: 10050,
            TaxonomySubcategoryId: null,
            Description: "Furnishing the home: furniture, mattresses, large and small appliances.",
            UseCases:
            [
                "Sofa or bed purchase",
                "Washing machine replacement",
                "Home office desk"
            ],
            InclusionRules:
            [
                "Merchant sells furniture or household appliances",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Consumer electronics belong to Electronics",
                "Small decor items belong to Home Décor & Furnishings"
            ],
            MerchantSignals:
            [
                "IKEA",
                "EZ LIVING",
                "DFS ",
                "HOMESTORE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per purchase, typically 40-2000 EUR"),

        // Housing > Moving & Temporary Housing
        new(
            TaxonomyCategoryId: 10060,
            TaxonomySubcategoryId: null,
            Description: "Changing homes: movers, van hire, storage, temporary stays.",
            UseCases:
            [
                "Moving company",
                "Self-storage unit rent",
                "Short-term accommodation between homes"
            ],
            InclusionRules:
            [
                "Cost exists because of a move or between-homes period",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Holiday accommodation belongs to Travel"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7),

        // Housing > Security & Home Services
        new(
            TaxonomyCategoryId: 10070,
            TaxonomySubcategoryId: null,
            Description: "Protecting the home: alarms, monitoring, cameras, building services.",
            UseCases:
            [
                "Monitored alarm subscription",
                "CCTV purchase and fitting",
                "Key cutting"
            ],
            InclusionRules:
            [
                "Merchant provides home security or building services",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Insurance belongs to Home & Property Insurance"
            ],
            MerchantSignals:
            [
                "PHONEWATCH"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Recurring, typically 20-60 EUR monthly"),

        // ---- Full-coverage pass: Home & Garden (110) ----

        // Home & Garden > Home Supplies & Consumables
        new(
            TaxonomyCategoryId: 11010,
            TaxonomySubcategoryId: null,
            Description: "Things the household uses up: cleaning, laundry, bulbs, batteries.",
            UseCases:
            [
                "Cleaning products top-up",
                "Light bulbs and batteries",
                "Laundry supplies"
            ],
            InclusionRules:
            [
                "Consumable household goods rather than food",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Supermarket runs that are mostly food belong to Groceries",
                "Toiletries belong to Personal Care"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.65,
            AmountProfile: "Per visit, typically 5-40 EUR"),

        // Home & Garden > Home Décor & Furnishings
        new(
            TaxonomyCategoryId: 11020,
            TaxonomySubcategoryId: null,
            Description: "Making the home look right: decor, candles, soft furnishings, art.",
            UseCases:
            [
                "Decor shop visit",
                "Seasonal decorations",
                "Cushions and throws"
            ],
            InclusionRules:
            [
                "Merchant sells decorative household goods",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Furniture and appliances belong to Furniture & Appliances"
            ],
            MerchantSignals:
            [
                "HOMESENSE",
                "SOSTRENE GRENE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 10-150 EUR"),

        // Home & Garden > Garden & Outdoor
        new(
            TaxonomyCategoryId: 11040,
            TaxonomySubcategoryId: null,
            Description: "The garden: plants, compost, tools, outdoor furniture.",
            UseCases:
            [
                "Garden centre visit",
                "Plants and compost",
                "Outdoor furniture for summer"
            ],
            InclusionRules:
            [
                "Merchant is a garden centre or goods are for the garden",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Hired landscaping services belong to Seasonal & Outdoor Maintenance",
                "General DIY materials belong to DIY & Improvement Supplies"
            ],
            MerchantSignals:
            [
                "GARDEN CENTRE",
                "GARDEN CENTER"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Seasonal, typically 10-200 EUR"),

        // Home & Garden > Seasonal & Outdoor Maintenance
        new(
            TaxonomyCategoryId: 11050,
            TaxonomySubcategoryId: null,
            Description: "Hired outdoor upkeep: landscaping, tree work, gutters, power washing.",
            UseCases:
            [
                "Landscaper invoice",
                "Tree surgeon",
                "Gutter cleaning"
            ],
            InclusionRules:
            [
                "Service maintains the outside of the property",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Goods you buy for the garden belong to Garden & Outdoor"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7),

        // Home & Garden > Workshop & Projects
        new(
            TaxonomyCategoryId: 11060,
            TaxonomySubcategoryId: null,
            Description: "Hobby-grade making at home: workshop supplies and personal projects.",
            UseCases:
            [
                "Workshop consumables",
                "Materials for a personal build project"
            ],
            InclusionRules:
            [
                "Purchase serves a home project rather than house maintenance",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "House-improvement materials belong to DIY & Improvement Supplies",
                "Craft materials belong to Arts & Crafts"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.65),

        // ---- Full-coverage pass: Transportation (120) ----

        // Transportation > Car Ownership
        new(
            TaxonomyCategoryId: 12030,
            TaxonomySubcategoryId: null,
            Description: "Owning the car itself: purchase payments, motor tax, ownership admin.",
            UseCases:
            [
                "Motor tax payment",
                "Car purchase deposit or payment",
                "Change-of-ownership fees"
            ],
            InclusionRules:
            [
                "Cost exists because you own the vehicle, not because you used or fixed it",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Fuel belongs to Fuel & Charging",
                "Repairs belong to Car Maintenance & Repairs",
                "Insurance belongs to Vehicle Insurance",
                "Car loans belong to Auto & Vehicle Loans"
            ],
            MerchantSignals:
            [
                "MOTOR TAX",
                "MOTORTAX"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Annual or per purchase, typically 100-30000 EUR"),

        // Transportation > Car Maintenance & Repairs
        new(
            TaxonomyCategoryId: 12040,
            TaxonomySubcategoryId: null,
            Description: "Keeping the car going: garages, tyres, servicing, parts.",
            UseCases:
            [
                "Annual service",
                "Tyre replacement",
                "Breakdown repair"
            ],
            InclusionRules:
            [
                "Merchant is a garage, tyre centre, or parts seller",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "The NCT test itself belongs to Driving & Licensing",
                "Fuel-station shop purchases follow the fuel or grocery profile"
            ],
            MerchantSignals:
            [
                "ADVANCE PITSTOP",
                "FASTFIT",
                "KWIK FIT"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 50-800 EUR"),

        // Transportation > Cycling & Micro-Mobility
        new(
            TaxonomyCategoryId: 12060,
            TaxonomySubcategoryId: null,
            Description: "Bikes, e-bikes, scooters: buying, fixing, and shared schemes.",
            UseCases:
            [
                "Bike shop purchase or repair",
                "Shared bike or e-scooter scheme charge"
            ],
            InclusionRules:
            [
                "Cost serves two-wheeled or micro-mobility transport",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Public transport fares belong to Public Transport"
            ],
            MerchantSignals:
            [
                "BLEEPERBIKE",
                "MOBY BIKES",
                "TIER",
                "DUBLINBIKES"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Small recurring or one-off, typically 2-1500 EUR"),

        // Transportation > Driving & Licensing
        new(
            TaxonomyCategoryId: 12070,
            TaxonomySubcategoryId: null,
            Description: "Being allowed to drive: licences, tests, lessons, NCT.",
            UseCases:
            [
                "NCT test fee",
                "Driving licence renewal",
                "Driving lessons"
            ],
            InclusionRules:
            [
                "Cost relates to driving permission or competence",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Motor tax belongs to Car Ownership",
                "Repairs to pass the test belong to Car Maintenance & Repairs"
            ],
            MerchantSignals:
            [
                "NCTS",
                "NDLS",
                "DRIVING SCHOOL"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Occasional, typically 30-600 EUR"),

        // Transportation > Long-Distance Transport
        new(
            TaxonomyCategoryId: 12080,
            TaxonomySubcategoryId: null,
            Description: "Getting far: intercity coaches, trains, ferries.",
            UseCases:
            [
                "Intercity coach ticket",
                "Ferry crossing",
                "Long-distance rail"
            ],
            InclusionRules:
            [
                "Journey is intercity or international surface travel",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Flights follow the Flights subcategory definition",
                "Commuter fares belong to Public Transport"
            ],
            MerchantSignals:
            [
                "IRISH FERRIES",
                "STENA",
                "AIRCOACH",
                "CITYLINK",
                "FLIXBUS"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per trip, typically 10-200 EUR"),

        // ---- Full-coverage pass: Food & Dining remainder (130) ----

        // Food & Dining > Drinks & Social Food
        new(
            TaxonomyCategoryId: 13030,
            TaxonomySubcategoryId: null,
            Description: "Social drinking and nightlife: pubs, bars, rounds, club nights.",
            UseCases:
            [
                "Round at the pub",
                "Cocktail bar",
                "Night out food and drinks"
            ],
            InclusionRules:
            [
                "Merchant is a pub, bar, or nightlife venue",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Coffee-first cafes follow the Coffee Shops definition",
                "Off-licence bottles for home follow the grocery profile",
                "Restaurant meals belong to Dining Out"
            ],
            MerchantSignals:
            [
                "WETHERSPOON",
                "THE TEMPLE BAR",
                "TAVERN",
                " BAR ",
                "PUB "
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.65,
            AmountProfile: "Per night, typically 8-120 EUR"),

        // Food & Dining > Special Dietary Spending
        new(
            TaxonomyCategoryId: 13040,
            TaxonomySubcategoryId: null,
            Description: "Diet-specific food: sports nutrition, baby food, specialty diets.",
            UseCases:
            [
                "Protein and sports nutrition order",
                "Gluten-free specialty shop",
                "Baby food stock-up"
            ],
            InclusionRules:
            [
                "Merchant specializes in dietary-specific food",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Ordinary supermarket baskets belong to Groceries",
                "Vitamins and supplements belong to Preventive & Wellness"
            ],
            MerchantSignals:
            [
                "MYPROTEIN",
                "BULK.COM"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per order, typically 15-80 EUR"),

        // Food & Dining > Meal Services
        new(
            TaxonomyCategoryId: 13050,
            TaxonomySubcategoryId: null,
            Description: "Prepared-food services: meal kits, meal-prep subscriptions, catering.",
            UseCases:
            [
                "Weekly meal-kit box",
                "Meal-prep subscription",
                "Event catering"
            ],
            InclusionRules:
            [
                "Merchant delivers recurring prepared or kit meals, or caters events",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "One-off restaurant delivery belongs to Dining Out"
            ],
            MerchantSignals:
            [
                "HELLOFRESH",
                "GOUSTO",
                "DROP CHEF"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Recurring weekly, typically 30-90 EUR"),

        // Food & Dining > Tobacco & Nicotine
        new(
            TaxonomyCategoryId: 13070,
            TaxonomySubcategoryId: null,
            Description: "Cigarettes, tobacco, vaping, and nicotine products.",
            UseCases:
            [
                "Cigarettes at the till",
                "Vape shop visit",
                "Nicotine pouches"
            ],
            InclusionRules:
            [
                "Merchant is a tobacconist or vape shop, or the purchase is nicotine products",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Mixed convenience baskets follow the grocery profile unless the merchant is nicotine-specific"
            ],
            MerchantSignals:
            [
                "VAPE",
                "TOBACCO",
                "ECIG"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per purchase, typically 8-60 EUR"),

        // ---- Full-coverage pass: Utilities & Communications remainder (140) ----

        // Utilities > Water & Waste
        new(
            TaxonomyCategoryId: 14030,
            TaxonomySubcategoryId: null,
            Description: "Water in, waste out: water charges, sewage, septic services.",
            UseCases:
            [
                "Water bill",
                "Septic tank emptying"
            ],
            InclusionRules:
            [
                "Merchant supplies water or removes waste for the home",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Bin collection follows the Bin / Refuse Collection definition"
            ],
            MerchantSignals:
            [
                "IRISH WATER",
                "UISCE EIREANN"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Recurring or occasional, typically 20-250 EUR"),

        // Utilities > TV & Media Utilities
        new(
            TaxonomyCategoryId: 14050,
            TaxonomySubcategoryId: null,
            Description: "Television as a utility: TV licence, cable and satellite packages.",
            UseCases:
            [
                "TV licence payment",
                "Satellite TV package"
            ],
            InclusionRules:
            [
                "Cost is broadcast or bundled TV service, not on-demand streaming",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Streaming subscriptions belong to Streaming & Media",
                "Broadband-led bundles belong to Internet & Communications"
            ],
            MerchantSignals:
            [
                "TV LICENCE",
                "TV LICENSE",
                "AN POST TV"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Annual or monthly, typically 13-160 EUR"),

        // Utilities > Utility Setup & Service Fees
        new(
            TaxonomyCategoryId: 14060,
            TaxonomySubcategoryId: null,
            Description: "The joins between utilities: deposits, connections, late fees.",
            UseCases:
            [
                "Connection or reconnection fee",
                "Utility deposit",
                "Late payment fee on a bill"
            ],
            InclusionRules:
            [
                "Charge is an administrative utility fee rather than usage",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Ordinary usage bills belong to their utility's category"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.65),

        // ---- Full-coverage pass: Insurance remainder (150) ----

        // Insurance > Life & Disability
        new(
            TaxonomyCategoryId: 15020,
            TaxonomySubcategoryId: null,
            Description: "Insuring the person: life cover, income protection, serious illness.",
            UseCases:
            [
                "Monthly life policy premium",
                "Income protection premium"
            ],
            InclusionRules:
            [
                "Policy pays on death, illness, or lost income",
                "Direction is outflow",
                "Amount repeats on a premium cadence"
            ],
            ExclusionRules:
            [
                "Health cover belongs to Health Insurance"
            ],
            MerchantSignals:
            [
                "IRISH LIFE ASSURANCE",
                "ROYAL LONDON"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Recurring monthly, typically 15-120 EUR"),

        // Insurance > Home & Property Insurance
        new(
            TaxonomyCategoryId: 15030,
            TaxonomySubcategoryId: null,
            Description: "Insuring the home and its contents.",
            UseCases:
            [
                "Annual home insurance premium",
                "Contents-only renter policy"
            ],
            InclusionRules:
            [
                "Policy covers buildings or contents",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Mortgage protection follows Life & Disability when sold as life cover"
            ],
            MerchantSignals:
            [
                "HOME INSURANCE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Annual or monthly, typically 20-600 EUR"),

        // Insurance > Vehicle Insurance (category level; the Car Insurance
        // subcategory definition keeps its own signals)
        new(
            TaxonomyCategoryId: 15040,
            TaxonomySubcategoryId: null,
            Description: "Insuring vehicles: car, van, motorbike policies.",
            UseCases:
            [
                "Motor policy renewal",
                "Monthly instalment on car insurance"
            ],
            InclusionRules:
            [
                "Policy covers a vehicle",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Breakdown-club memberships follow their own merchant profile",
                "Named motor insurers follow the Car Insurance subcategory definition"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Annual or monthly, typically 30-250 EUR"),

        // Insurance > Travel & Event Insurance
        new(
            TaxonomyCategoryId: 15050,
            TaxonomySubcategoryId: null,
            Description: "Short-horizon cover: travel policies, event and gadget cover.",
            UseCases:
            [
                "Single-trip travel insurance",
                "Annual multi-trip policy"
            ],
            InclusionRules:
            [
                "Policy covers a trip, event, or item for a bounded period",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Ongoing home or vehicle policies belong to their categories"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.65),

        // Insurance > Pet & Other Insurance
        new(
            TaxonomyCategoryId: 15060,
            TaxonomySubcategoryId: null,
            Description: "Insuring the pets and the odds and ends.",
            UseCases:
            [
                "Monthly pet policy premium"
            ],
            InclusionRules:
            [
                "Policy covers a pet or a niche risk",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Vet bills themselves belong to Veterinary Care"
            ],
            MerchantSignals:
            [
                "PETINSURE"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Recurring monthly, typically 10-60 EUR"),

        // ---- Full-coverage pass: Healthcare remainder (160) ----

        // Healthcare > Primary Care & Specialists
        new(
            TaxonomyCategoryId: 16010,
            TaxonomySubcategoryId: null,
            Description: "Seeing the doctor: GP visits, consultants, clinics.",
            UseCases:
            [
                "GP visit fee",
                "Consultant appointment",
                "Out-of-hours clinic"
            ],
            InclusionRules:
            [
                "Merchant is a medical practice or clinic",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Medicines belong to Prescriptions & Medications",
                "Dental and vision have their own categories"
            ],
            MerchantSignals:
            [
                "MEDICAL CENTRE",
                "CLINIC",
                "WEBDOCTOR"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 30-250 EUR"),

        // Healthcare > Dental Care
        new(
            TaxonomyCategoryId: 16020,
            TaxonomySubcategoryId: null,
            Description: "Teeth: checkups, hygienist, treatment, orthodontics.",
            UseCases:
            [
                "Dental checkup and cleaning",
                "Filling or extraction",
                "Orthodontic instalment"
            ],
            InclusionRules:
            [
                "Merchant is a dental practice",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Toothpaste and brushes belong to Personal Care"
            ],
            MerchantSignals:
            [
                "DENTAL",
                "DENTIST",
                "SMILES"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit, typically 40-400 EUR"),

        // Healthcare > Vision & Eye Care
        new(
            TaxonomyCategoryId: 16030,
            TaxonomySubcategoryId: null,
            Description: "Eyes: tests, glasses, lenses.",
            UseCases:
            [
                "Eye test",
                "New glasses",
                "Contact lens subscription"
            ],
            InclusionRules:
            [
                "Merchant is an optician or eye-care provider",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Fashion sunglasses without prescription belong to Shoes & Accessories"
            ],
            MerchantSignals:
            [
                "SPECSAVERS",
                "VISION EXPRESS",
                "OPTICIAN"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.75,
            AmountProfile: "Occasional, typically 25-400 EUR"),

        // Healthcare > Medical Equipment & Supplies
        new(
            TaxonomyCategoryId: 16050,
            TaxonomySubcategoryId: null,
            Description: "Medical kit at home: devices, mobility aids, supplies.",
            UseCases:
            [
                "Blood pressure monitor",
                "Mobility aid purchase"
            ],
            InclusionRules:
            [
                "Purchase is a medical device or supply for home use",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Medicines belong to Prescriptions & Medications"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.65),

        // Healthcare > Hospital & Surgery
        new(
            TaxonomyCategoryId: 16060,
            TaxonomySubcategoryId: null,
            Description: "Hospital care: admissions, procedures, A&E charges.",
            UseCases:
            [
                "Emergency department charge",
                "Procedure excess or co-pay"
            ],
            InclusionRules:
            [
                "Merchant is a hospital or surgical facility",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Insurance premiums belong to Health Insurance"
            ],
            MerchantSignals:
            [
                "HOSPITAL"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Occasional, typically 100-1000 EUR"),

        // Healthcare > Mental & Behavioral Health
        new(
            TaxonomyCategoryId: 16070,
            TaxonomySubcategoryId: null,
            Description: "Minding the mind: therapy, counselling, psychiatry.",
            UseCases:
            [
                "Weekly therapy session",
                "Online counselling subscription"
            ],
            InclusionRules:
            [
                "Merchant provides mental-health care",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Meditation apps belong to Software & Digital Tools unless clinical"
            ],
            MerchantSignals:
            [
                "COUNSELLING",
                "PSYCHOLOG",
                "BETTERHELP"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Recurring, typically 50-120 EUR per session"),

        // Healthcare > Preventive & Wellness
        new(
            TaxonomyCategoryId: 16080,
            TaxonomySubcategoryId: null,
            Description: "Staying well: vitamins, supplements, screenings, physio.",
            UseCases:
            [
                "Vitamins and supplements",
                "Physiotherapy session",
                "Health screening"
            ],
            InclusionRules:
            [
                "Purchase maintains health rather than treats illness",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Sports nutrition belongs to Special Dietary Spending",
                "Gym membership belongs to Gym Membership"
            ],
            MerchantSignals:
            [
                "HOLLAND & BARRETT",
                "PHYSIO"
            ],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.7,
            AmountProfile: "Per visit or purchase, typically 10-90 EUR"),

        // Healthcare > Travel & Accessibility
        new(
            TaxonomyCategoryId: 16090,
            TaxonomySubcategoryId: null,
            Description: "Getting to care and living accessibly: medical transport, accessibility aids.",
            UseCases:
            [
                "Transport to treatment",
                "Accessibility adaptation cost"
            ],
            InclusionRules:
            [
                "Cost enables access to medical care or accessible living",
                "Direction is outflow"
            ],
            ExclusionRules:
            [
                "Ordinary taxis belong to Taxi / Ride-hailing"
            ],
            MerchantSignals: [],
            DirectionExpectation: CharacteristicsDirection.Outflow,
            AnalyticsTreatment: CharacteristicsAnalyticsTreatment.Expense,
            ConfidenceFloor: 0.65)
    ];
}
