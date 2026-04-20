namespace NSFinance.Api.Modules.AI.Services;

public enum RealWorldIntentFamily
{
    FinancialGuidance = 0,
    PlaceDiscovery = 1,
    CommerceDiscovery = 2,
    ServiceDiscovery = 3,
    ExploratoryAssistance = 4,
    MixedAssistance = 5,
    Ambiguous = 6
}

public enum RealWorldExecutionMode
{
    FocusedPlaceSearch = 0,
    FocusedThemeSearch = 1,
    ExploratoryMultiDomainSearch = 2,
    FinancialGuidanceOnly = 3,
    ClarifyLight = 4,
    MissingLocationGuard = 5,
    ProviderFailureFallback = 6
}

public enum RealWorldDiscoveryDomain
{
    Cafe = 0,
    Restaurant = 1,
    Takeaway = 2,
    PubBar = 3,
    MovieTheater = 4,
    ParkWalk = 5,
    Playground = 6,
    Pharmacy = 7,
    PetrolStation = 8,
    Gym = 9,
    ElectronicsRetail = 10,
    ConvenienceStore = 11,
    Grocery = 12,
    ShoppingGeneral = 13,
    OutdoorActivity = 14,
    EntertainmentGeneral = 15,
    NightlifeGeneral = 16,
    FoodDrinkGeneral = 17,
    ServiceGeneral = 18,
    CommerceGeneral = 19,
    ExploratoryEveningActivity = 20,
    ExploratoryFamilyActivity = 21
}

public enum RealWorldDomainFamily
{
    FoodDrink = 0,
    Entertainment = 1,
    Outdoor = 2,
    EssentialService = 3,
    RetailCommerce = 4,
    Wellness = 5,
    Meta = 6
}

public enum RealWorldFailureScenario
{
    MissingLocation = 0,
    LocationDeniedOpenSettings = 1,
    ProviderRequestFailure = 2,
    ProviderUnavailable = 3,
    NoMatchesFound = 4,
    ClarificationNeeded = 5,
    DomainNotActionable = 6,
    ExploratoryPartialResults = 7,
    InternalRoutingConflict = 8
}

public enum RealWorldInterpretationSource
{
    AiPrimary = 0,
    DeterministicFallback = 1
}

public sealed record RealWorldIntentInterpretation(
    RealWorldIntentFamily IntentFamily,
    RealWorldExecutionMode RecommendedExecutionMode,
    bool PlacesApplicable,
    bool FinancialRelated,
    bool RequiresLocation,
    bool Exploratory,
    bool ClarificationNeeded,
    bool HasNearMeLanguage,
    bool HasExplicitLocality,
    double Confidence,
    IReadOnlyList<RealWorldDiscoveryDomain> CandidateDomains,
    string? ClarificationPrompt,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> CandidateConcepts { get; init; } = [];

    public RealWorldInterpretationSource InterpretationSource { get; init; } =
        RealWorldInterpretationSource.DeterministicFallback;
}

public sealed record RealWorldExecutionPlan(
    RealWorldExecutionMode Mode,
    RealWorldIntentFamily IntentFamily,
    bool ShouldHandoffToCompanion,
    bool ShouldUsePlaces,
    bool UseDirectPlacesExecution,
    bool RequiresLocationGrounding,
    IReadOnlyList<RealWorldDiscoveryDomain> SelectedDomains,
    string? ClarificationPrompt,
    IReadOnlyList<string> ReasonCodes);

public sealed record RealWorldDomainCapability(
    RealWorldDiscoveryDomain Domain,
    RealWorldDomainFamily Family,
    IReadOnlyList<string> CanonicalConcepts,
    bool SupportsFocusedPlaceSearch,
    bool SupportsFocusedThemeSearch,
    bool SupportsExploratorySearch,
    bool SupportsCommerceDiscovery,
    bool SupportsServiceDiscovery,
    bool SupportsEssentialService,
    bool SuitableEvening,
    bool SuitableFamily,
    bool SuitableChill,
    bool SuitableActive,
    bool SuitableSocial,
    bool SuitableSolo,
    bool SuitableQuickErrand,
    bool SuitablePlannedOuting,
    bool SuitableBudgetFriendly,
    bool SuitableNightlife,
    bool SuitableIndoor,
    bool SuitableOutdoor,
    bool NearMeAppropriate,
    bool ExplicitLocalityAppropriate,
    bool ExploratoryFallbackEligible,
    bool IsGeneric);

public sealed record RealWorldDomainSelectionResult(
    IReadOnlyList<RealWorldDiscoveryDomain> SelectedDomains,
    IReadOnlyList<string> ReasonCodes);

public sealed record RealWorldPlacesExecutionRequest(
    string UserQuery,
    string CountryCode,
    PlaceSearchLocationContext? LocationContext,
    IReadOnlyList<RealWorldDiscoveryDomain> Domains,
    int MaxDomains,
    int MaxItemsPerDomain,
    int MaxTotalItems,
    RealWorldExecutionMode Mode,
    RealWorldPlaceRetrievalPlan? RetrievalPlan = null);

public sealed record RealWorldPlaceRetrievalPlan(
    bool Authoritative,
    bool HasNearMeSemantic,
    RealWorldExecutionMode ExecutionMode,
    IReadOnlyList<RealWorldDiscoveryDomain> SelectedDomains,
    IReadOnlyList<string> CanonicalConcepts,
    int RequestedShortlistSize,
    RealWorldIntentFamily IntentFamily = RealWorldIntentFamily.Ambiguous,
    string? CommerceProductProfile = null,
    IReadOnlyList<string>? CommerceProductHints = null,
    bool EnableImplicitLocalBias = false);

public sealed record RealWorldCommerceEligibilityResult(
    bool IsCommerceVendorRequest,
    string ProductProfile,
    IReadOnlyList<string> ProductHints,
    IReadOnlyList<RealWorldDiscoveryDomain> AllowedDomains,
    IReadOnlyList<RealWorldDiscoveryDomain> PreferredDomains,
    IReadOnlyList<RealWorldDiscoveryDomain> ExcludedDomains,
    IReadOnlyList<string> ReasonCodes);

public sealed record RealWorldDomainPlacesGroup(
    RealWorldDiscoveryDomain Domain,
    string Label,
    IReadOnlyList<PlaceSearchItem> Items,
    IReadOnlyList<string> Warnings);

public sealed record RealWorldPlacesExecutionResult(
    bool Succeeded,
    bool HasAnyResults,
    bool IsPartial,
    IReadOnlyList<RealWorldDomainPlacesGroup> Groups,
    RealWorldFailureScenario? FailureScenario,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Warnings);

public sealed record RealWorldFailureMessage(
    string ReplyText,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FollowUpIntentHints);

public interface IRealWorldIntentInterpreter
{
    Task<RealWorldIntentInterpretation> InterpretAsync(
        UserChatRequest request,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        CancellationToken cancellationToken);
}

public interface IRealWorldDeterministicFallbackBuilder
{
    RealWorldIntentInterpretation BuildSeed(
        string userMessage,
        LocalDiscoveryConstraintExtractionResult localDiscovery);

    RealWorldIntentInterpretation BuildFallback(
        string userMessage,
        LocalDiscoveryConstraintExtractionResult localDiscovery);
}

public interface IRealWorldFinancialGuardrailPolicy
{
    bool ShouldForceFinancialGuidance(string userMessage, out string reasonCode);
}

public interface IRealWorldInterpretationValidationPolicy
{
    RealWorldIntentInterpretation ValidateAndNormalize(
        string userMessage,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        RealWorldIntentInterpretation aiInterpretation,
        RealWorldIntentInterpretation deterministicFallback);
}

public sealed record RealWorldConceptNormalizationResult(
    IReadOnlyList<string> NormalizedConcepts,
    IReadOnlyList<RealWorldDiscoveryDomain> MappedDomains,
    IReadOnlyList<string> ReasonCodes);

public interface IRealWorldConceptNormalizationPolicy
{
    RealWorldConceptNormalizationResult Normalize(
        IReadOnlyList<string> candidateConcepts,
        IReadOnlyList<RealWorldDiscoveryDomain> candidateDomains);
}

public interface IRealWorldExecutionModePlanner
{
    RealWorldExecutionPlan Plan(
        string userQuery,
        RealWorldIntentInterpretation interpretation,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery);
}

public interface IRealWorldDomainCapabilityCatalog
{
    IReadOnlyList<RealWorldDomainCapability> GetDomains();

    bool TryGetDomain(RealWorldDiscoveryDomain domain, out RealWorldDomainCapability capability);
}

public interface IExploratoryDomainSelectionPolicy
{
    RealWorldDomainSelectionResult Select(
        RealWorldIntentInterpretation interpretation,
        string userQuery,
        int maxDomains);
}

public interface IRealWorldProductDomainEligibilityPolicy
{
    RealWorldCommerceEligibilityResult Evaluate(
        string userQuery,
        RealWorldIntentInterpretation interpretation,
        IReadOnlyList<RealWorldDiscoveryDomain> candidateDomains);
}

public interface IRealWorldPlacesExecutionService
{
    Task<RealWorldPlacesExecutionResult> ExecuteAsync(
        RealWorldPlacesExecutionRequest request,
        CancellationToken cancellationToken);
}

public interface IRealWorldFailureMessageBuilder
{
    RealWorldFailureMessage Build(
        RealWorldFailureScenario scenario,
        bool exploratory,
        string? clarificationPrompt = null);
}

public static class RealWorldDomainMetadata
{
    public static string ToLabel(this RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.Cafe => "Cafes",
            RealWorldDiscoveryDomain.Restaurant => "Restaurants",
            RealWorldDiscoveryDomain.Takeaway => "Takeaways",
            RealWorldDiscoveryDomain.PubBar => "Pubs & Bars",
            RealWorldDiscoveryDomain.MovieTheater => "Cinemas",
            RealWorldDiscoveryDomain.ParkWalk => "Parks & Walks",
            RealWorldDiscoveryDomain.Playground => "Playgrounds",
            RealWorldDiscoveryDomain.Pharmacy => "Pharmacies",
            RealWorldDiscoveryDomain.PetrolStation => "Petrol Stations",
            RealWorldDiscoveryDomain.Gym => "Gyms",
            RealWorldDiscoveryDomain.ElectronicsRetail => "Electronics Stores",
            RealWorldDiscoveryDomain.ConvenienceStore => "Convenience Stores",
            RealWorldDiscoveryDomain.Grocery => "Grocery Stores",
            RealWorldDiscoveryDomain.ShoppingGeneral => "Shopping",
            RealWorldDiscoveryDomain.OutdoorActivity => "Outdoor Activities",
            RealWorldDiscoveryDomain.EntertainmentGeneral => "Entertainment",
            RealWorldDiscoveryDomain.NightlifeGeneral => "Nightlife",
            RealWorldDiscoveryDomain.FoodDrinkGeneral => "Food & Drink",
            RealWorldDiscoveryDomain.ServiceGeneral => "Local Services",
            RealWorldDiscoveryDomain.CommerceGeneral => "Shops",
            RealWorldDiscoveryDomain.ExploratoryEveningActivity => "Evening Ideas",
            RealWorldDiscoveryDomain.ExploratoryFamilyActivity => "Family Ideas",
            _ => "Places"
        };
    }

    public static string ToQueryPhrase(this RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.Cafe => "coffee shops",
            RealWorldDiscoveryDomain.Restaurant => "restaurants",
            RealWorldDiscoveryDomain.Takeaway => "takeaways",
            RealWorldDiscoveryDomain.PubBar => "pubs and bars",
            RealWorldDiscoveryDomain.MovieTheater => "movie theaters",
            RealWorldDiscoveryDomain.ParkWalk => "parks for walking",
            RealWorldDiscoveryDomain.Playground => "playgrounds",
            RealWorldDiscoveryDomain.Pharmacy => "pharmacies",
            RealWorldDiscoveryDomain.PetrolStation => "petrol stations",
            RealWorldDiscoveryDomain.Gym => "gyms",
            RealWorldDiscoveryDomain.ElectronicsRetail => "electronics stores",
            RealWorldDiscoveryDomain.ConvenienceStore => "convenience stores",
            RealWorldDiscoveryDomain.Grocery => "grocery stores",
            RealWorldDiscoveryDomain.ShoppingGeneral => "shops",
            RealWorldDiscoveryDomain.OutdoorActivity => "outdoor attractions",
            RealWorldDiscoveryDomain.EntertainmentGeneral => "entertainment venues",
            RealWorldDiscoveryDomain.NightlifeGeneral => "nightlife places",
            RealWorldDiscoveryDomain.FoodDrinkGeneral => "food and drink places",
            RealWorldDiscoveryDomain.ServiceGeneral => "local services",
            RealWorldDiscoveryDomain.CommerceGeneral => "stores",
            RealWorldDiscoveryDomain.ExploratoryEveningActivity => "things to do tonight",
            RealWorldDiscoveryDomain.ExploratoryFamilyActivity => "family places",
            _ => "places"
        };
    }
}

public static class RealWorldInterpreterFallbackReasonCodes
{
    public const string Prefix = "real_world_interpreter_fallback:";
    public const string AiUnavailable = Prefix + "ai_unavailable";
    public const string AiCallFailed = Prefix + "ai_call_failed";
    public const string InvalidPayload = Prefix + "invalid_payload";
    public const string UnknownVocabulary = Prefix + "unknown_vocabulary";
    public const string LowConfidence = Prefix + "low_confidence";
    public const string ValidationInconsistent = Prefix + "validation_inconsistent";
    public const string PlannerDowngrade = Prefix + "planner_downgrade";
}

