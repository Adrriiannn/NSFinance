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
    public const int Version = 2;

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
            AmountProfile: "Per visit, typically 5-60 EUR")
    ];
}
