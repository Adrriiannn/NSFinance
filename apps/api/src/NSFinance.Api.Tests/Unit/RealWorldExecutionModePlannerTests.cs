using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldExecutionModePlannerTests
{
    private readonly IRealWorldDomainCapabilityCatalog domainCatalog = new RealWorldDomainCapabilityCatalog();
    private readonly RealWorldExecutionModePlanner planner;

    public RealWorldExecutionModePlannerTests()
    {
        planner = new RealWorldExecutionModePlanner(new ExploratoryDomainSelectionPolicy(domainCatalog));
    }

    [Fact]
    public void Plan_NearMeWithoutGrounding_UsesMissingLocationGuard()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.PlaceDiscovery,
            RecommendedExecutionMode: RealWorldExecutionMode.FocusedPlaceSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: true,
            Exploratory: false,
            ClarificationNeeded: false,
            HasNearMeLanguage: true,
            HasExplicitLocality: false,
            Confidence: 0.8,
            CandidateDomains: [RealWorldDiscoveryDomain.Cafe],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: []);

        var plan = planner.Plan(
            "coffee near me",
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: null,
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: null,
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.8,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: []));

        Assert.Equal(RealWorldExecutionMode.MissingLocationGuard, plan.Mode);
        Assert.True(plan.RequiresLocationGrounding);
    }

    [Fact]
    public void Plan_LocalityExplicitWithoutGps_AllowsSearch()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.PlaceDiscovery,
            RecommendedExecutionMode: RealWorldExecutionMode.FocusedPlaceSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: true,
            Exploratory: false,
            ClarificationNeeded: false,
            HasNearMeLanguage: false,
            HasExplicitLocality: true,
            Confidence: 0.8,
            CandidateDomains: [RealWorldDiscoveryDomain.MovieTheater],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: []);

        var plan = planner.Plan(
            "museums around dublin",
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: "query_locality",
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: "Dublin",
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.8,
                HasNearMeLanguage: false,
                HasExplicitLocality: true,
                LocalityHint: "dublin",
                PlaceTypeHints: ["movie_theater"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: []));

        Assert.Equal(RealWorldExecutionMode.FocusedPlaceSearch, plan.Mode);
        Assert.True(plan.ShouldUsePlaces);
        Assert.True(plan.UseDirectPlacesExecution);
    }

    [Fact]
    public void Plan_ExploratoryMode_SelectsBoundedDiversifiedDomains()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.ExploratoryAssistance,
            RecommendedExecutionMode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: true,
            Exploratory: true,
            ClarificationNeeded: false,
            HasNearMeLanguage: true,
            HasExplicitLocality: false,
            Confidence: 0.84,
            CandidateDomains:
            [
                RealWorldDiscoveryDomain.PubBar,
                RealWorldDiscoveryDomain.MovieTheater,
                RealWorldDiscoveryDomain.Restaurant,
                RealWorldDiscoveryDomain.ParkWalk,
                RealWorldDiscoveryDomain.NightlifeGeneral
            ],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: []);

        var plan = planner.Plan(
            "what can i do later tonight",
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: "typed_area",
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: "Dublin",
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.85,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: [],
                AudienceHints: [],
                TimeHints: ["tonight"],
                PreferenceHints: [],
                ReasonCodes: []));

        Assert.Equal(RealWorldExecutionMode.ExploratoryMultiDomainSearch, plan.Mode);
        Assert.True(plan.UseDirectPlacesExecution);
        Assert.InRange(plan.SelectedDomains.Count, 1, 4);
        Assert.Contains("real_world_planner_query_signal_used", plan.ReasonCodes);
        Assert.Contains("real_world_planner_ai_domains_preserved", plan.ReasonCodes);
        Assert.Contains("real_world_catalog_selection_started", plan.ReasonCodes);
        Assert.Contains(plan.ReasonCodes, code => code.StartsWith("real_world_catalog_domain_selected:", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_AiPrimaryLowConfidence_UsesClarifyLightTrustGuard()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.PlaceDiscovery,
            RecommendedExecutionMode: RealWorldExecutionMode.FocusedPlaceSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: true,
            Exploratory: false,
            ClarificationNeeded: false,
            HasNearMeLanguage: true,
            HasExplicitLocality: false,
            Confidence: 0.30d,
            CandidateDomains: [RealWorldDiscoveryDomain.Cafe],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: [])
        {
            InterpretationSource = RealWorldInterpretationSource.AiPrimary
        };

        var plan = planner.Plan(
            "help me find somewhere",
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: null,
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: null,
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.60d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: []));

        Assert.Equal(RealWorldExecutionMode.ClarifyLight, plan.Mode);
        Assert.False(plan.ShouldUsePlaces);
        Assert.Contains("execution_mode:low_confidence_clarify", plan.ReasonCodes);
        Assert.Contains(RealWorldInterpreterFallbackReasonCodes.PlannerDowngrade, plan.ReasonCodes);
    }

    [Fact]
    public void Plan_ExploratoryWithKidsSignal_PrioritizesFamilyFriendlyDomains()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.ExploratoryAssistance,
            RecommendedExecutionMode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: true,
            Exploratory: true,
            ClarificationNeeded: false,
            HasNearMeLanguage: true,
            HasExplicitLocality: false,
            Confidence: 0.84d,
            CandidateDomains:
            [
                RealWorldDiscoveryDomain.PubBar,
                RealWorldDiscoveryDomain.MovieTheater,
                RealWorldDiscoveryDomain.Restaurant,
                RealWorldDiscoveryDomain.ParkWalk,
                RealWorldDiscoveryDomain.Playground
            ],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: [])
        {
            InterpretationSource = RealWorldInterpretationSource.AiPrimary
        };

        var plan = planner.Plan(
            "what can i do later tonight with kids",
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: "typed_area",
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: "Dublin",
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.90d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: [],
                AudienceHints: ["kids", "family"],
                TimeHints: ["tonight"],
                PreferenceHints: [],
                ReasonCodes: []));

        Assert.Contains(RealWorldDiscoveryDomain.Playground, plan.SelectedDomains);
        Assert.DoesNotContain(RealWorldDiscoveryDomain.NightlifeGeneral, plan.SelectedDomains);
        Assert.Contains("real_world_catalog_family_signal_priority_applied", plan.ReasonCodes);
    }

    [Fact]
    public void Plan_EmptyQuerySignal_RecordsMissingSignalMarker()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.PlaceDiscovery,
            RecommendedExecutionMode: RealWorldExecutionMode.FocusedPlaceSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: false,
            Exploratory: false,
            ClarificationNeeded: false,
            HasNearMeLanguage: false,
            HasExplicitLocality: true,
            Confidence: 0.77d,
            CandidateDomains: [RealWorldDiscoveryDomain.Cafe],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: [])
        {
            InterpretationSource = RealWorldInterpretationSource.AiPrimary
        };

        var plan = planner.Plan(
            string.Empty,
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: "query_locality",
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: "Dublin",
                LocalityLabel: null,
                AccuracyBucket: null,
                CapturedAtUtc: null),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.72d,
                HasNearMeLanguage: false,
                HasExplicitLocality: true,
                LocalityHint: "dublin",
                PlaceTypeHints: ["cafe"],
                AudienceHints: [],
                TimeHints: [],
                PreferenceHints: [],
                ReasonCodes: []));

        Assert.Contains("real_world_planner_query_signal_missing", plan.ReasonCodes);
    }

    [Fact]
    public void Plan_ExploratorySelection_DiversifiesAcrossCatalogFamilies()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.ExploratoryAssistance,
            RecommendedExecutionMode: RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: true,
            Exploratory: true,
            ClarificationNeeded: false,
            HasNearMeLanguage: true,
            HasExplicitLocality: false,
            Confidence: 0.90d,
            CandidateDomains:
            [
                RealWorldDiscoveryDomain.Cafe,
                RealWorldDiscoveryDomain.Restaurant,
                RealWorldDiscoveryDomain.Takeaway,
                RealWorldDiscoveryDomain.PubBar,
                RealWorldDiscoveryDomain.MovieTheater,
                RealWorldDiscoveryDomain.ParkWalk
            ],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: []);

        var plan = planner.Plan(
            "something fun near me tonight",
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: "gps",
                Latitude: 53.35,
                Longitude: -6.26,
                RadiusMeters: 1500,
                TypedArea: null,
                LocalityLabel: "Dublin",
                AccuracyBucket: "high",
                CapturedAtUtc: DateTimeOffset.UtcNow),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.9d,
                HasNearMeLanguage: true,
                HasExplicitLocality: false,
                LocalityHint: null,
                PlaceTypeHints: [],
                AudienceHints: [],
                TimeHints: ["tonight"],
                PreferenceHints: [],
                ReasonCodes: []));

        var families = plan.SelectedDomains
            .Select(domain => domainCatalog.TryGetDomain(domain, out var capability) ? capability.Family : RealWorldDomainFamily.Meta)
            .Distinct()
            .ToArray();
        Assert.True(families.Length >= 2);
    }

    [Fact]
    public void Plan_ThemedFoodRequest_PrefersFoodDrinkDomainsFromCatalog()
    {
        var interpretation = new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.PlaceDiscovery,
            RecommendedExecutionMode: RealWorldExecutionMode.FocusedThemeSearch,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: false,
            Exploratory: false,
            ClarificationNeeded: false,
            HasNearMeLanguage: false,
            HasExplicitLocality: true,
            Confidence: 0.83d,
            CandidateDomains:
            [
                RealWorldDiscoveryDomain.FoodDrinkGeneral,
                RealWorldDiscoveryDomain.EntertainmentGeneral
            ],
            ClarificationPrompt: null,
            ReasonCodes: ["test"],
            Warnings: []);

        var plan = planner.Plan(
            "what should i eat tonight in dublin",
            interpretation,
            grounding: new CompanionLocationGrounding(
                Source: "typed_area",
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: "Dublin",
                LocalityLabel: "Dublin",
                AccuracyBucket: null,
                CapturedAtUtc: null),
            localDiscovery: new LocalDiscoveryConstraintExtractionResult(
                IsLocalDiscoveryCandidate: true,
                Confidence: 0.86d,
                HasNearMeLanguage: false,
                HasExplicitLocality: true,
                LocalityHint: "dublin",
                PlaceTypeHints: ["restaurant"],
                AudienceHints: [],
                TimeHints: ["tonight"],
                PreferenceHints: [],
                ReasonCodes: []));

        Assert.NotEmpty(plan.SelectedDomains);
        Assert.Contains(
            plan.SelectedDomains,
            domain => domainCatalog.TryGetDomain(domain, out var capability)
                      && capability.Family == RealWorldDomainFamily.FoodDrink);
    }
}
