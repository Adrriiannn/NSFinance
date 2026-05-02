using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class CompanionPlacesIntelligenceV2Tests
{
    [Fact]
    public void SemanticIntent_PreservesBrandSearchAndGpsGrounding()
    {
        var service = new CompanionSemanticIntentService();
        var action = new CompanionResolvedAction(
            CompanionActionKind.NewPlaceSearch,
            Reason: "brand search",
            RequiresToolExecution: true,
            RequiresClarification: false,
            ClarificationNeed: null,
            PlaceQuery: "Starbucks",
            LocationQuery: "near me",
            Requirement: null,
            SortGoal: null,
            TargetResultSetId: null,
            IncludeConcepts: ["coffee shop"],
            ExcludeConcepts: [],
            Preferences: [],
            TimeFilters: [],
            Warnings: []);

        var intent = service.Build(
            BuildRequest("Starbucks near me"),
            BuildState(),
            resultContext: null,
            interpretation: null,
            retrievalPlan: null,
            intelligence: null,
            resolvedAction: action);

        Assert.Equal("new_place_search", intent.ActionKind);
        Assert.Equal("Starbucks", intent.PlaceQuery);
        Assert.Equal("Starbucks", intent.BrandOrEntity);
        Assert.Equal("near_me", intent.Location.Mode);
        Assert.False(intent.Location.RequiresLocation);
        Assert.Equal("brand_match_then_distance", intent.RankingGoal);
    }

    [Fact]
    public void SemanticIntent_FallbackFiltersDoNotOverrideStructuredFilters()
    {
        var service = new CompanionSemanticIntentService();
        var action = new CompanionResolvedAction(
            CompanionActionKind.FilterPreviousResults,
            Reason: "structured correction",
            RequiresToolExecution: true,
            RequiresClarification: false,
            ClarificationNeed: null,
            PlaceQuery: null,
            LocationQuery: null,
            Requirement: null,
            SortGoal: null,
            TargetResultSetId: "active_result_set",
            IncludeConcepts: [],
            ExcludeConcepts: ["takeaway"],
            Preferences: [],
            TimeFilters: [],
            Warnings: []);

        var intent = service.Build(
            BuildRequest("not fast food"),
            BuildState(),
            resultContext: null,
            interpretation: null,
            retrievalPlan: null,
            intelligence: null,
            resolvedAction: action);

        Assert.Equal(["takeaway"], intent.NegativeFilters);
    }

    [Fact]
    public void SemanticIntent_ExtractsRatingFilter_WhenOtherHardFilterExists()
    {
        var service = new CompanionSemanticIntentService();
        var action = new CompanionResolvedAction(
            CompanionActionKind.FilterPreviousResults,
            Reason: "parking and rating",
            RequiresToolExecution: true,
            RequiresClarification: false,
            ClarificationNeed: null,
            PlaceQuery: null,
            LocationQuery: null,
            Requirement: "parking",
            SortGoal: null,
            TargetResultSetId: "active_result_set",
            IncludeConcepts: [],
            ExcludeConcepts: [],
            Preferences: [],
            TimeFilters: [],
            Warnings: []);

        var intent = service.Build(
            BuildRequest("only 4.7 rating and up please"),
            BuildState(),
            resultContext: null,
            interpretation: null,
            retrievalPlan: null,
            intelligence: null,
            resolvedAction: action);

        Assert.Contains("parking", intent.HardFilters);
        Assert.Contains("rating>=4.7", intent.HardFilters);
    }

    [Fact]
    public async Task CandidatePool_UsesMultipleIntentionalPassesToApproachFifty()
    {
        var discovery = new MultipassDiscoveryService();
        var pool = new CompanionPlaceCandidatePoolService(
            discovery,
            new NoOpPlaceRegistryService(),
            Options.Create(new GooglePlacesOptions()),
            new NoOpChatTelemetry());

        var result = await pool.BuildPoolAsync(
            BuildIntent(placeQuery: "Starbucks", rankingGoal: "brand_match_then_distance") with
            {
                BrandOrEntity = "Starbucks"
            },
            BuildRequest("Starbucks near me"),
            CancellationToken.None);

        Assert.Equal(50, result.Candidates.Count);
        Assert.Contains("Starbucks coffee", result.QueryPasses);
        Assert.Contains("Starbucks cafe", result.QueryPasses);
        Assert.True(discovery.TextQueries.Count >= 3);
    }

    [Fact]
    public async Task CandidatePool_KeepsFirstStageCandidatesLightweight()
    {
        var discovery = new MultipassDiscoveryService(includeRichFields: true);
        var pool = new CompanionPlaceCandidatePoolService(
            discovery,
            new NoOpPlaceRegistryService(),
            Options.Create(new GooglePlacesOptions()),
            new NoOpChatTelemetry());

        var result = await pool.BuildPoolAsync(
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance"),
            BuildRequest("coffee shops near me"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates, item => item.PlaceId == "coffee shops-1");
        Assert.DoesNotContain("website_url", candidate.LightweightAttributes.Keys);
        Assert.DoesNotContain("phone_number", candidate.LightweightAttributes.Keys);
        Assert.DoesNotContain("photos_json", candidate.LightweightAttributes.Keys);
        Assert.DoesNotContain("editorial_summary", candidate.LightweightAttributes.Keys);
    }

    [Fact]
    public void ConstraintEngine_HardFiltersFastFoodAndTakeawayForFineDining()
    {
        var engine = new CompanionPlaceConstraintEngine(new NoOpChatTelemetry());
        var intent = BuildIntent(
            placeQuery: "fine dining restaurants",
            rankingGoal: "concept_fit_then_distance",
            negativeFilters: ["fast_food", "takeaway"]);

        var result = engine.Apply(
            intent,
            [
                Candidate("mcd", "McDonald's", "fast_food_restaurant", ["fast_food_restaurant"], 300),
                Candidate("takeaway", "Local Takeaway", "meal_takeaway", ["meal_takeaway"], 250),
                Candidate("chapter", "Chapter One Fine Dining", "restaurant", ["restaurant"], 5_000, "Fine dining restaurant")
            ]);

        Assert.Single(result.Candidates);
        Assert.Equal("chapter", result.Candidates[0].PlaceId);
        Assert.Contains(result.Rejected, item => item.PlaceId == "mcd" && item.Reason.Contains("fast_food", StringComparison.Ordinal));
        Assert.Contains(result.Rejected, item => item.PlaceId == "takeaway" && item.Reason.Contains("takeaway", StringComparison.Ordinal));
    }

    [Fact]
    public void ConstraintEngine_DoesNotReAddRejectedCandidatesWhenAllFail()
    {
        var engine = new CompanionPlaceConstraintEngine(new NoOpChatTelemetry());
        var intent = BuildIntent(
            placeQuery: "restaurants",
            rankingGoal: "intent_fit_then_distance",
            hardFilters: ["rating>=4.7"]);

        var result = engine.Apply(
            intent,
            [
                Candidate("one", "One", "restaurant", ["restaurant"], 100, rating: 4.2),
                Candidate("two", "Two", "restaurant", ["restaurant"], 200, rating: 4.4)
            ]);

        Assert.Empty(result.Candidates);
        Assert.Equal(2, result.Rejected.Count);
        Assert.Contains("no_hard_filter_matches", result.Diagnostics);
    }

    [Fact]
    public void ConstraintEngine_RatingThresholdRejectsMissingRating()
    {
        var engine = new CompanionPlaceConstraintEngine(new NoOpChatTelemetry());
        var result = engine.Apply(
            BuildIntent(placeQuery: "restaurants", rankingGoal: "intent_fit_then_distance", hardFilters: ["rating>=4.7"]),
            [Candidate("missing", "Missing Rating", "restaurant", ["restaurant"], 100, rating: null)]);

        Assert.Empty(result.Candidates);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void CategoryCompatibility_BanksRejectAtmsButAtmsAllowAtms()
    {
        var telemetry = new NoOpChatTelemetry();
        var service = new CompanionPlaceCategoryCompatibilityService(new CompanionPlaceTypeFamilyClassifier(telemetry), telemetry);
        var bankIntent = BuildIntent(placeQuery: "AIB banks", rankingGoal: "brand_match_then_distance") with
        {
            Role = new CompanionPlaceRoleIntent("bank_branch", ["bank", "financial_institution"], ["bank"], ["atm"], [], "strict")
        };
        var atmIntent = BuildIntent(placeQuery: "AIB ATMs", rankingGoal: "brand_match_then_distance") with
        {
            Role = new CompanionPlaceRoleIntent("atm", ["atm"], ["atm"], [], [], "strict")
        };
        var candidates = new[]
        {
            Candidate("atm", "AIB ATM", "atm", ["atm"], 100, "ATM"),
            Candidate("bank", "AIB Bank", "bank", ["bank"], 200, "Bank")
        };

        var banks = service.Apply(bankIntent, candidates);
        var atms = service.Apply(atmIntent, candidates);

        Assert.Equal("bank", Assert.Single(banks.Candidates).PlaceId);
        Assert.Equal("atm", Assert.Single(atms.Candidates).PlaceId);
    }

    [Fact]
    public void BrandIdentity_RejectsCompetitorBrandsAndSupportsAibAlias()
    {
        var service = new CompanionPlaceBrandIdentityService(new NoOpChatTelemetry());
        var starbucks = service.Apply(
            BuildIntent(placeQuery: "Starbucks", rankingGoal: "brand_match_then_distance") with { BrandOrEntity = "Starbucks" },
            [
                Candidate("starbucks", "Starbucks Coffee", "cafe", ["cafe"], 100),
                Candidate("costa", "Costa Coffee", "cafe", ["cafe"], 200)
            ]);
        var aib = service.Apply(
            BuildIntent(placeQuery: "AIB banks", rankingGoal: "brand_match_then_distance") with { BrandOrEntity = "AIB" },
            [
                Candidate("aib", "Allied Irish Bank", "bank", ["bank"], 100),
                Candidate("boi", "Bank of Ireland", "bank", ["bank"], 200)
            ]);

        Assert.Equal("starbucks", Assert.Single(starbucks.Candidates).PlaceId);
        Assert.Equal("aib", Assert.Single(aib.Candidates).PlaceId);
    }

    [Fact]
    public void SearchStrategy_AibBanks_SplitsEntityAndRole()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("AIB banks near me"),
            BuildIntent(placeQuery: "AIB banks", rankingGoal: "brand_match_then_distance") with { BrandOrEntity = "AIB banks" },
            "test");

        Assert.Equal("AIB", strategy.Entity?.CanonicalName);
        Assert.Equal("bank_branch", strategy.Role.RequestedRole);
        Assert.DoesNotContain(strategy.SearchVariants, item => item.Query.Contains("coffee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AIPlanner_ParsesValidJson()
    {
        var parser = new CompanionPlaceSearchStrategyJsonParser(new CompanionPlaceSearchStrategySanitizer());
        var response = SuccessfulAiResponse(AibBankStrategyJson());

        var parsed = parser.TryParse(
            response,
            BuildRequest("AIB banks near me"),
            BuildIntent(placeQuery: "AIB banks", rankingGoal: "brand_match_then_distance"),
            out var strategy,
            out _,
            out _);

        Assert.True(parsed);
        Assert.Equal("AIB bank", strategy?.CanonicalQuery);
        Assert.Equal("AIB", strategy?.Entity?.CanonicalName);
        Assert.Equal("bank_branch", strategy?.Role.RequestedRole);
    }

    [Fact]
    public async Task AIPlanner_RejectsInvalidJsonAndFallsBack()
    {
        var telemetry = new RecordingTelemetry();
        var planner = BuildAiPlanner("not json", telemetry);

        var strategy = await planner.PlanAsync(
            BuildRequest("AIB banks near me"),
            BuildIntent(placeQuery: "AIB banks", rankingGoal: "brand_match_then_distance") with { BrandOrEntity = "AIB banks" },
            CancellationToken.None);

        Assert.Equal("AIB", strategy.Entity?.CanonicalName);
        Assert.Contains(strategy.Warnings, item => item.Contains("invalid_json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(telemetry.Events, item => item.Name == "places.search_strategy.ai_parse_failed");
        Assert.Contains(telemetry.Events, item => item.Name == "places.search_strategy.finalized"
                                                  && item.Properties.TryGetValue("source", out var source)
                                                  && string.Equals(source?.ToString(), "fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AIPlanner_RejectsOverBroadVariants()
    {
        var validator = new CompanionPlaceSearchVariantValidator(new NoOpChatTelemetry());
        var strategy = new CompanionPlaceSearchStrategy(
            "bike shops near me",
            "bike shops",
            null,
            new CompanionPlaceRoleIntent("bicycle_store", ["bicycle_store"], ["bicycle_store"], [], [], "compatible"),
            [
                new CompanionPlaceSearchVariant("bike shops", "primary", false, true, 0.9),
                new CompanionPlaceSearchVariant("local places", "provider_probe", false, false, 0.2)
            ],
            [], [], [], [], new CompanionLocationIntent("near_me", null, 53.3, -6.2, false), "intent_fit_then_distance", 50, 10, 0.9, []);

        var variants = validator.Validate(strategy);

        Assert.Single(variants);
        Assert.Equal("bike shops", variants[0].Query);
    }

    [Fact]
    public void SearchStrategy_AibAtms_SplitsEntityAndAtmRole()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("AIB ATMs near me"),
            BuildIntent(placeQuery: "AIB ATMs", rankingGoal: "brand_match_then_distance") with { BrandOrEntity = "AIB ATMs" },
            "test");

        Assert.Equal("AIB", strategy.Entity?.CanonicalName);
        Assert.Equal("atm", strategy.Role.RequestedRole);
        Assert.All(strategy.SearchVariants, item => Assert.Contains("ATM", item.Query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchStrategy_FineDining_HasNoEntityAndRestaurantModifier()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("fine dining restaurants near me"),
            BuildIntent(placeQuery: "fine dining restaurants", rankingGoal: "concept_fit_then_distance", softPreferences: ["upscale"]) with { BrandOrEntity = "fine dining" },
            "test");

        Assert.Null(strategy.Entity);
        Assert.Equal("restaurant", strategy.Role.RequestedRole);
        Assert.Contains("fine_dining", strategy.Role.Modifiers);
        Assert.DoesNotContain(strategy.SearchVariants, item => item.Query.Contains("coffee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchStrategy_CarParks_HasNoEntityAndParkingRole()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("car parks near me"),
            BuildIntent(placeQuery: "car parks", rankingGoal: "parking_match_then_distance") with { BrandOrEntity = "car parks" },
            "test");

        Assert.Null(strategy.Entity);
        Assert.Equal("parking", strategy.Role.RequestedRole);
        Assert.Contains(strategy.SearchVariants, item => item.Query.Contains("parking", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchStrategy_CoffeeShops_AllowsCafeVariants()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("coffee shops near me"),
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "intent_fit_then_distance"),
            "test");

        Assert.Null(strategy.Entity);
        Assert.Equal("coffee_shop", strategy.Role.RequestedRole);
        Assert.Contains(strategy.SearchVariants, item => string.Equals(item.Query, "cafe", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("bike shops near me")]
    [InlineData("bicycle shops near me")]
    [InlineData("cycle shops near me")]
    public void Fallback_BikeShops_NoEntity_UsesBicycleStoreRole(string message)
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest(message),
            BuildIntent(placeQuery: message, rankingGoal: "intent_fit_then_distance") with { BrandOrEntity = "bike" },
            "places_search_strategy_ai_timeout");

        Assert.Null(strategy.Entity);
        Assert.Equal("bicycle_store", strategy.Role.RequestedRole);
        Assert.Contains(strategy.SearchVariants, item => item.Query.Contains("bike shop", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(strategy.SearchVariants, item => item.Query.Contains("bicycle store", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(strategy.SearchVariants, item => item.Query.Contains("cycle shop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fallback_BikeRepair_UsesBicycleRepairVariants()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("bike repair near me"),
            BuildIntent(placeQuery: "bike repair", rankingGoal: "intent_fit_then_distance") with { BrandOrEntity = "bike" },
            "places_search_strategy_ai_timeout");

        Assert.Null(strategy.Entity);
        Assert.Equal("bicycle_store", strategy.Role.RequestedRole);
        Assert.Contains(strategy.SearchVariants, item => item.Query.Contains("bicycle repair shop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fallback_DistinctiveBrand_StillCanBeEntity()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("IKEA near me"),
            BuildIntent(placeQuery: "IKEA", rankingGoal: "brand_match_then_distance"),
            "places_search_strategy_ai_timeout");

        Assert.Equal("IKEA", strategy.Entity?.CanonicalName);
    }

    [Fact]
    public void SearchStrategy_Ikea_AllowsSingleVariant()
    {
        var strategy = new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), new NoOpChatTelemetry()).Plan(
            BuildRequest("IKEA near me"),
            BuildIntent(placeQuery: "IKEA", rankingGoal: "brand_match_then_distance"),
            "test");

        Assert.Equal("IKEA", strategy.Entity?.CanonicalName);
        Assert.Single(strategy.SearchVariants);
    }

    [Fact]
    public void ApplegreenPetrol_AIParser_BrandAndGasStationRole()
    {
        var strategy = ParseStrategy("""
{"canonicalQuery":"Applegreen petrol station","entity":{"rawEntityText":"Applegreen","canonicalName":"Applegreen","aliases":["Applegreen"],"isBrandOrNamedEntity":true,"requiresEntityLock":true,"verificationRequired":true,"confidence":0.91},"role":{"requestedRole":"gas_station","requiredCoreRoles":["gas_station"],"acceptableSubRoles":["gas_station"],"excludedSiblingRoles":["car_wash"],"modifiers":[],"categoryStrictness":"strict"},"searchVariants":[{"query":"Applegreen petrol station","purpose":"primary","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.92},{"query":"Applegreen fuel station","purpose":"role_disambiguation","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.8}],"hardRequirements":[],"negativeRequirements":["car_wash"],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"brand_match_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.91,"warnings":[]}
""", "Applegreen petrol stations near me");

        Assert.Equal("Applegreen", strategy.Entity?.CanonicalName);
        Assert.Equal("gas_station", strategy.Role.RequestedRole);
    }

    [Fact]
    public void FacebookOffice_AIParser_BrandEntityOfficeRole()
    {
        var strategy = ParseStrategy("""
{"canonicalQuery":"Facebook office Dublin","entity":{"rawEntityText":"Facebook","canonicalName":"Facebook","aliases":["Facebook"],"relationshipAliases":[{"name":"Meta","relationshipType":"parent_company"}],"isBrandOrNamedEntity":true,"requiresEntityLock":true,"verificationRequired":true,"confidence":0.82},"role":{"requestedRole":"office","requiredCoreRoles":[],"acceptableSubRoles":[],"excludedSiblingRoles":[],"modifiers":[],"categoryStrictness":"loose"},"searchVariants":[{"query":"Facebook office Dublin","purpose":"primary","requiresEntityMatch":true,"requiresRoleMatch":false,"confidence":0.84},{"query":"Meta office Dublin","purpose":"alias","requiresEntityMatch":true,"requiresRoleMatch":false,"confidence":0.75}],"hardRequirements":[],"negativeRequirements":[],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"brand_match_then_relevance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.82,"warnings":[]}
""", "Facebook office Dublin");

        Assert.Equal("Facebook", strategy.Entity?.CanonicalName);
        Assert.Equal("office", strategy.Role.RequestedRole);
        var alias = Assert.Single(strategy.Entity!.RelationshipAliases);
        Assert.Equal("Meta", alias.Name);
        Assert.Equal("parent_company", alias.RelationshipType);
    }

    [Fact]
    public void BikeShops_AIParser_GenericCategoryNoBrand()
    {
        var strategy = ParseStrategy("""
{"canonicalQuery":"bike shops","entity":null,"role":{"requestedRole":"bicycle_store","requiredCoreRoles":["bicycle_store"],"acceptableSubRoles":["bicycle_store","sporting_goods_store"],"excludedSiblingRoles":[],"modifiers":[],"categoryStrictness":"compatible"},"searchVariants":[{"query":"bike shops","purpose":"primary","requiresEntityMatch":false,"requiresRoleMatch":true,"confidence":0.9}],"hardRequirements":[],"negativeRequirements":[],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"intent_fit_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.9,"warnings":[]}
""", "bike shops near me");

        Assert.Null(strategy.Entity);
        Assert.Equal("bicycle_store", strategy.Role.RequestedRole);
    }

    [Fact]
    public void VariantValidator_RejectsCoffeeVariantsForBanksAndFineDining()
    {
        var validator = new CompanionPlaceSearchVariantValidator(new NoOpChatTelemetry());
        var bank = new CompanionPlaceSearchStrategy(
            "AIB banks near me",
            "AIB bank",
            new CompanionPlaceEntityIntent("AIB", "AIB", ["AIB"], [], true, true, true, "verified", 0.9),
            new CompanionPlaceRoleIntent("bank_branch", ["bank"], ["bank"], ["atm"], [], "strict"),
            [
                new CompanionPlaceSearchVariant("AIB bank", "primary", true, true, 0.9),
                new CompanionPlaceSearchVariant("AIB banks coffee", "role_disambiguation", true, true, 0.2)
            ],
            [], [], [], [], new CompanionLocationIntent("near_me", null, 53.3, -6.2, false), "brand_match_then_distance", 50, 10, 0.9, []);
        var fineDining = bank with
        {
            OriginalUserMessage = "fine dining restaurants near me",
            CanonicalQuery = "fine dining restaurants",
            Entity = null,
            Role = new CompanionPlaceRoleIntent("restaurant", ["restaurant"], ["restaurant"], ["fast_food_restaurant"], ["fine_dining"], "compatible"),
            SearchVariants =
            [
                new CompanionPlaceSearchVariant("fine dining restaurants", "primary", false, true, 0.9),
                new CompanionPlaceSearchVariant("fine dining cafe", "role_disambiguation", false, true, 0.2)
            ]
        };

        Assert.DoesNotContain(validator.Validate(bank), item => item.Query.Contains("coffee", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(validator.Validate(fineDining), item => item.Query.Contains("cafe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VariantValidator_AllowsCoffeeVariantsForCoffeeIntentAndDoesNotPad()
    {
        var validator = new CompanionPlaceSearchVariantValidator(new NoOpChatTelemetry());
        var strategy = new CompanionPlaceSearchStrategy(
            "coffee shops near me",
            "coffee shops",
            null,
            new CompanionPlaceRoleIntent("coffee_shop", ["coffee_shop", "cafe"], ["coffee_shop", "cafe"], [], [], "compatible"),
            [new CompanionPlaceSearchVariant("coffee", "primary", false, true, 0.9)],
            [], [], [], [], new CompanionLocationIntent("near_me", null, 53.3, -6.2, false), "intent_fit_then_distance", 50, 10, 0.9, []);

        var variants = validator.Validate(strategy);

        Assert.Single(variants);
        Assert.Equal("coffee", variants[0].Query);
    }

    [Fact]
    public async Task EntityVerification_VerifiesAnPostFromProviderEvidence()
    {
        var discovery = new LookupDiscoveryService(
            new Dictionary<string, IReadOnlyList<CompanionPlaceCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ANPOST post office"] = [ProviderCandidate("an-post", "An Post Office", "post_office", ["post_office"])]
            });
        var telemetry = new NoOpChatTelemetry();
        var verifier = new CompanionPlaceEntityVerificationService(discovery, new CompanionPlaceTypeFamilyClassifier(telemetry), telemetry);
        var strategy = new CompanionPlaceSearchStrategy(
            "ANPOST post offices near me",
            "ANPOST post office",
            new CompanionPlaceEntityIntent("ANPOST", "ANPOST", ["ANPOST"], [], true, true, true, "pending", 0.75),
            new CompanionPlaceRoleIntent("post_office", ["post_office"], ["post_office"], ["mailbox"], [], "strict"),
            [new CompanionPlaceSearchVariant("ANPOST post office", "primary", true, true, 0.9)],
            [], [], [], [], new CompanionLocationIntent("near_me", null, 53.3, -6.2, false), "brand_match_then_distance", 50, 10, 0.9, []);

        var result = await verifier.VerifyAsync(strategy, CancellationToken.None);

        Assert.Equal("verified", result.Status);
        Assert.Contains(result.Entity!.Aliases, item => item.Contains("An Post", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EntityVerification_RejectsUnknownEntityWhenNoEvidence()
    {
        var telemetry = new NoOpChatTelemetry();
        var verifier = new CompanionPlaceEntityVerificationService(new LookupDiscoveryService(new Dictionary<string, IReadOnlyList<CompanionPlaceCandidate>>()), new CompanionPlaceTypeFamilyClassifier(telemetry), telemetry);
        var strategy = new CompanionPlaceSearchStrategy(
            "unknown thing near me",
            "unknown thing",
            new CompanionPlaceEntityIntent("unknown thing", "unknown thing", ["unknown thing"], [], true, true, true, "pending", 0.45),
            new CompanionPlaceRoleIntent("store", [], [], [], [], "loose"),
            [new CompanionPlaceSearchVariant("unknown thing", "primary", true, false, 0.9)],
            [], [], [], [], new CompanionLocationIntent("near_me", null, 53.3, -6.2, false), "brand_match_then_distance", 50, 10, 0.9, []);

        var result = await verifier.VerifyAsync(strategy, CancellationToken.None);

        Assert.Equal("rejected", result.Status);
    }

    [Fact]
    public async Task EntityVerification_FacebookOffice_MetaAlias_VerifiesWithWeakRoleEvidence()
    {
        var discovery = new LookupDiscoveryService(
            new Dictionary<string, IReadOnlyList<CompanionPlaceCandidate>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Meta office Dublin"] = [ProviderCandidate("meta", "Meta Dublin", "establishment", ["establishment", "point_of_interest"])]
            });
        var telemetry = new RecordingTelemetry();
        var verifier = new CompanionPlaceEntityVerificationService(discovery, new CompanionPlaceTypeFamilyClassifier(telemetry), telemetry);
        var strategy = new CompanionPlaceSearchStrategy(
            "Facebook office Dublin",
            "Facebook office Dublin",
            new CompanionPlaceEntityIntent(
                "Facebook",
                "Facebook",
                ["Facebook"],
                [new CompanionEntityRelationshipAlias("Meta", "parent_company")],
                true,
                true,
                true,
                "pending",
                0.82),
            new CompanionPlaceRoleIntent("office", ["office"], ["corporate_office"], [], [], "compatible"),
            [new CompanionPlaceSearchVariant("Meta office Dublin", "alias", true, true, 0.8)],
            [], [], [], [], new CompanionLocationIntent("typed_area", "Dublin", null, null, false), "brand_match_then_relevance", 50, 10, 0.82, []);

        var result = await verifier.VerifyAsync(strategy, CancellationToken.None);

        Assert.Equal("verified", result.Status);
        Assert.Contains("role_evidence_weak_entity_match_strong", result.Warnings);
        Assert.Contains(result.Evidence, item => item.Contains("relationship_alias_match:Meta:parent_company", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(telemetry.Events, item => item.Name == "places.entity_alias.relationship_matched");
    }

    [Fact]
    public void CategoryCompatibility_OfficeRole_StrongEntityAllowsGenericTypeButRejectsWrongEntity()
    {
        var telemetry = new NoOpChatTelemetry();
        var service = new CompanionPlaceCategoryCompatibilityService(new CompanionPlaceTypeFamilyClassifier(telemetry), telemetry);
        var intent = BuildIntent(placeQuery: "Facebook office Dublin", rankingGoal: "brand_match_then_relevance") with
        {
            BrandOrEntity = "Facebook",
            Role = new CompanionPlaceRoleIntent("office", ["office"], ["corporate_office"], [], [], "compatible")
        };
        var strategy = new CompanionPlaceSearchStrategy(
            "Facebook office Dublin",
            "Facebook office Dublin",
            new CompanionPlaceEntityIntent(
                "Facebook",
                "Facebook",
                ["Facebook"],
                [new CompanionEntityRelationshipAlias("Meta", "parent_company")],
                true,
                true,
                true,
                "verified",
                0.9),
            intent.Role,
            [new CompanionPlaceSearchVariant("Meta office Dublin", "alias", true, true, 0.8)],
            [], [], [], [], intent.Location, "brand_match_then_relevance", 50, 10, 0.9, []);

        var result = service.Apply(
            intent,
            [
                Candidate("meta", "Meta Dublin", "establishment", ["establishment", "point_of_interest"], 1_000, "Establishment"),
                Candidate("google", "Google Dublin", "establishment", ["establishment", "point_of_interest"], 900, "Establishment")
            ],
            strategy);

        var card = Assert.Single(result.Candidates);
        Assert.Equal("meta", card.PlaceId);
        Assert.Contains(result.Rejected, item => item.PlaceId == "google" && item.Reason == "office_entity_mismatch");
    }

    [Fact]
    public async Task CandidatePool_WithValidatedStrategy_DoesNotInventCoffeeVariantsForBanks()
    {
        var discovery = new MultipassDiscoveryService();
        var pool = new CompanionPlaceCandidatePoolService(
            discovery,
            new NoOpPlaceRegistryService(),
            Options.Create(new GooglePlacesOptions()),
            new NoOpChatTelemetry());
        var intent = BuildIntent(placeQuery: "AIB bank", rankingGoal: "brand_match_then_distance") with { BrandOrEntity = "AIB" };
        var strategy = new CompanionPlaceSearchStrategy(
            "AIB banks near me",
            "AIB bank",
            new CompanionPlaceEntityIntent("AIB", "AIB", ["AIB", "Allied Irish Bank"], [], true, true, true, "verified", 0.9),
            new CompanionPlaceRoleIntent("bank_branch", ["bank"], ["bank"], ["atm"], [], "strict"),
            [
                new CompanionPlaceSearchVariant("AIB bank", "primary", true, true, 0.9),
                new CompanionPlaceSearchVariant("Allied Irish Bank", "alias", true, true, 0.85)
            ],
            [], [], [], [], new CompanionLocationIntent("near_me", null, 53.3, -6.2, false), "brand_match_then_distance", 50, 10, 0.9, []);

        await pool.BuildPoolAsync(intent, BuildRequest("AIB banks near me"), strategy, CancellationToken.None);

        Assert.DoesNotContain(discovery.TextQueries, query => query.Contains("coffee", StringComparison.OrdinalIgnoreCase) || query.Contains("cafe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("AIB bank", discovery.TextQueries);
    }

    [Fact]
    public void DuplicateCluster_CollapsesNearbyCarParkDuplicates()
    {
        var service = new CompanionPlaceDuplicateClusterService(new NoOpChatTelemetry());
        var intent = BuildIntent(placeQuery: "car parks", rankingGoal: "parking_match_then_distance") with
        {
            Role = new CompanionPlaceRoleIntent("parking", ["parking"], ["parking"], [], [], "strict")
        };
        var result = service.Cluster(
            intent,
            [
                Candidate("one", "Omni Park Car Park", "parking", ["parking"], 100, latitude: 53.400000, longitude: -6.250000, shortAddress: "Omni Park"),
                Candidate("two", "Omni Park Parking", "parking", ["parking"], 110, latitude: 53.400030, longitude: -6.250030, shortAddress: "Omni Park")
            ]);

        Assert.Single(result);
    }

    [Fact]
    public async Task ParkingEvidence_ShoppingCentreAddressCountsAsLikelyParking()
    {
        var service = new CompanionPlaceParkingEvidenceService(new MultipassDiscoveryService(), new NoOpChatTelemetry());
        var result = await service.EvaluateAsync(
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance", hardFilters: ["parking"]),
            [Candidate("coffee", "Coffee Shop", "cafe", ["cafe"], 100, shortAddress: "Omni Shopping Centre")],
            CancellationToken.None);

        var evidence = Assert.Single(result.EvidenceByPlaceId.Values);
        Assert.Equal("likely_on_site", evidence.EvidenceLevel);
    }

    [Fact]
    public void ResultContextBinder_PrefersLatestPlacesV2ForFollowUps()
    {
        var binder = new CompanionPlaceResultContextBinder(new NoOpChatTelemetry());
        var active = Snapshot(Guid.NewGuid(), "Starbucks");
        var latest = Snapshot(Guid.NewGuid(), "fine dining restaurants");
        var binding = binder.Bind(
            BuildRequest("only 4.7 rating and up please"),
            new ResultContextReadResult(active, ResultContextBindingClassification.Refine, UsedClientResultSetId: true, ExpiredBindingCleared: false, ReasonCodes: []),
            latest,
            BuildIntent(placeQuery: "restaurants", rankingGoal: "intent_fit_then_distance") with { ActionKind = "filter_previous_results" });

        Assert.Equal(latest.ResultSetId, binding.Context?.ResultSetId);
        Assert.True(binding.ClientContextWasStale);
        Assert.Equal("latest_v2", binding.Source);
    }

    [Fact]
    public void Ranking_FineDiningConceptOutranksNearbyFastFood()
    {
        var ranking = new CompanionPlaceIntelligenceRankingService();
        var intent = BuildIntent(
            placeQuery: "fine dining restaurants",
            rankingGoal: "concept_fit_then_distance",
            softPreferences: ["upscale"],
            negativeFilters: ["fast_food", "takeaway"]);

        var result = ranking.Rank(
            intent,
            [
                Candidate("mcd", "McDonald's", "fast_food_restaurant", ["fast_food_restaurant"], 300, "Fast food restaurant", rating: 4.4),
                Candidate("chapter", "Chapter One Fine Dining", "restaurant", ["restaurant"], 5_000, "Fine dining restaurant", rating: 4.7, priceLevel: "PRICE_LEVEL_EXPENSIVE"),
                Candidate("takeaway", "City Takeaway", "meal_takeaway", ["meal_takeaway"], 250, "Meal takeaway", rating: 4.8)
            ]);

        Assert.Equal("chapter", result.RankedCandidates[0].PlaceId);
    }

    [Fact]
    public async Task FinalistEnrichment_EnrichesOnlyTopTenAndUsesCache()
    {
        var details = new CountingPlaceDetailsService();
        var cache = new InMemoryPlacesShortLivedCache();
        var service = new CompanionPlaceFinalistEnrichmentService(
            details,
            photoService: null,
            cache,
            Options.Create(new GooglePlacesOptions { PlaceDetailsCacheTtlSeconds = 900 }),
            new NoOpChatTelemetry());
        var candidates = Enumerable.Range(1, 12)
            .Select(index => Candidate($"place-{index}", $"Place {index}", "cafe", ["cafe"], index * 100))
            .ToArray();

        var first = await service.EnrichAsync(
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance"),
            candidates,
            maxCards: 10,
            CancellationToken.None);
        var second = await service.EnrichAsync(
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance"),
            candidates,
            maxCards: 10,
            CancellationToken.None);

        Assert.Equal(10, first.StructuredResults?.Items.Count);
        Assert.Equal(10, first.EnrichedCount);
        Assert.Equal(10, details.CallCount);
        Assert.Equal(10, second.StructuredResults?.Items.Count);
        Assert.Equal(10, second.EnrichedCount);
        Assert.Equal(10, details.CallCount);
    }

    [Fact]
    public async Task SessionMemory_SavesHiddenPoolAndVisibleCardCounts()
    {
        var contextService = new CapturingResultContextService();
        var service = new CompanionPlaceSessionMemoryService(contextService, new NoOpChatTelemetry());
        var pool = Enumerable.Range(1, 12)
            .Select(index => Candidate($"place-{index}", $"Place {index}", "cafe", ["cafe"], index * 100))
            .ToArray();
        var cards = new CompanionStructuredResults(
            "places",
            [
                new CompanionPlaceCardResult(
                    Id: "place-1",
                    Name: "Place 1",
                    DistanceMeters: 100,
                    PhotoUrl: null,
                    PhotoUrls: [],
                    FormattedAddress: "1 Test Street",
                    ShortFormattedAddress: "1 Test Street",
                    Rating: 4.5,
                    OpenNow: true,
                    PriceLevel: null,
                    WebsiteUrl: null,
                    Category: "Cafe",
                    PrimaryTypeDisplayName: "Cafe",
                    ClosesInMinutes: null,
                    OpensInMinutes: null,
                    PhoneNumber: null,
                    MenuUrl: null,
                    GoogleMapsUri: null,
                    Latitude: 53.3,
                    Longitude: -6.2)
            ]);

        await service.SaveSearchContextAsync(
            BuildRequest("coffee shops near me"),
            BuildState(),
            BuildIntent(placeQuery: "coffee shops", rankingGoal: "distance"),
            pool,
            cards,
            CancellationToken.None);

        Assert.NotNull(contextService.LastWrite);
        Assert.Equal(12, contextService.LastWrite!.SuggestedEntities.Count);
        Assert.Equal("12", contextService.LastWrite.NormalizedConstraints["candidate_pool_count"]);
        Assert.Equal("1", contextService.LastWrite.NormalizedConstraints["visible_card_count"]);
        Assert.Equal("coffee shops", contextService.LastWrite.NormalizedConstraints["semantic_place_query"]);
    }

    [Fact]
    public async Task PlaceRegistry_StoresProviderIdsWithoutDuplicatingPlaceDetails()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"places-registry-{Guid.NewGuid():N}")
                .Options);
        var registry = new PlaceRegistryService(db, new NoOpChatTelemetry());

        await registry.RegisterSeenAsync("google_places", "place-1", ["coffee"], CancellationToken.None);
        await registry.RegisterSeenAsync("google_places", "place-1", ["coffee", "nearby"], CancellationToken.None);

        var row = Assert.Single(await db.PlaceRegistry.ToListAsync());
        Assert.Equal("google_places", row.Provider);
        Assert.Equal("place-1", row.ProviderPlaceId);
        Assert.Contains("nearby", row.InternalTagsJson);
        Assert.Null(row.LastRefreshedAtUtc);
    }

    [Fact]
    public async Task ConversationStateService_WhenSnapshotVersionConflict_Retries()
    {
        var userId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        await using var db = new ConflictOnceAppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"state-conflict-{Guid.NewGuid():N}")
                .Options);
        db.ConversationThreads.Add(new ConversationThread
        {
            Id = threadId,
            UserId = userId,
            StartedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ThrowNextStateSnapshotSave = true;
        var telemetry = new RecordingTelemetry();
        var service = new ConversationStateService(db, Options.Create(new AIIntegrationOptions()), telemetry);

        var saved = await service.SaveSnapshotAsync(
            userId,
            threadId,
            BuildState(),
            ConversationStateSnapshotReason.AssistantTurn,
            CancellationToken.None);

        Assert.Equal(1, saved.StateVersion);
        Assert.Equal(3, db.SaveChangesCallCount);
        Assert.Contains(telemetry.Events, item => item.Name == "conversation_state.snapshot_version_conflict_retry");
    }

    private static UserChatRequest BuildRequest(string message)
    {
        return new UserChatRequest(
            UserMessage: message,
            RecentTurns: [],
            State: BuildState(),
            CorrelationId: "test-correlation",
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CompanionLocationMetadataKeys.Latitude] = "53.3498",
                [CompanionLocationMetadataKeys.Longitude] = "-6.2603",
                [CompanionLocationMetadataKeys.RadiusMeters] = "1200"
            },
            UserId: Guid.NewGuid(),
            ConversationThreadId: Guid.NewGuid());
    }

    private static NSFinance.Api.Modules.AI.Services.ConversationStateSnapshot BuildState()
    {
        return new NSFinance.Api.Modules.AI.Services.ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: null,
            MerchantInvestigationSubject: null,
            RecentConclusions: []);
    }

    private static CompanionSemanticIntent BuildIntent(
        string placeQuery,
        string rankingGoal,
        IReadOnlyList<string>? hardFilters = null,
        IReadOnlyList<string>? negativeFilters = null,
        IReadOnlyList<string>? softPreferences = null)
    {
        return new CompanionSemanticIntent(
            IntentFamily: "places",
            ActionKind: "new_place_search",
            PlaceQuery: placeQuery,
            BrandOrEntity: null,
            Location: new CompanionLocationIntent("near_me", null, 53.3498, -6.2603, RequiresLocation: false),
            Role: new CompanionPlaceRoleIntent(null, [], [], [], [], "loose"),
            HardFilters: hardFilters ?? [],
            NegativeFilters: negativeFilters ?? [],
            SoftPreferences: softPreferences ?? [],
            NonSearchablePreferences: [],
            RequestedDetailFields: [],
            RankingGoal: rankingGoal,
            RequestedMaxResults: null,
            Confidence: 0.9,
            Ambiguities: []);
    }

    private static CompanionPlacePoolCandidate Candidate(
        string id,
        string name,
        string? primaryType,
        IReadOnlyList<string> types,
        double? distanceMeters,
        string? primaryTypeDisplayName = null,
        double? rating = 4.5,
        string? priceLevel = null,
        double? latitude = 53.3,
        double? longitude = -6.2,
        string? shortAddress = "Test Street")
    {
        return new CompanionPlacePoolCandidate(
            PlaceId: id,
            DisplayName: name,
            PrimaryType: primaryType,
            PrimaryTypeDisplayName: primaryTypeDisplayName ?? primaryType,
            Types: types,
            Latitude: latitude,
            Longitude: longitude,
            DistanceMeters: distanceMeters,
            ShortFormattedAddress: shortAddress,
            Rating: rating,
            UserRatingCount: 100,
            PriceLevel: priceLevel,
            OpenNow: true,
            LightweightAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static CompanionPlaceCandidate ProviderCandidate(
        string id,
        string name,
        string? primaryType,
        IReadOnlyList<string> types)
    {
        return new CompanionPlaceCandidate(
            PlaceId: id,
            ResourceName: $"places/{id}",
            DisplayName: name,
            PrimaryType: primaryType,
            PrimaryTypeDisplayName: primaryType,
            Types: types,
            NationalPhoneNumber: null,
            FormattedAddress: null,
            ShortFormattedAddress: "Test Street",
            Rating: 4.5,
            UserRatingCount: 100,
            GoogleMapsUri: null,
            WebsiteUri: null,
            OpeningHours: new PlaceOpeningHoursSummary(true, [], null),
            BusinessStatus: "OPERATIONAL",
            PriceLevel: null,
            IconMaskBaseUri: null,
            IconBackgroundColor: null,
            Takeout: null,
            Delivery: null,
            DineIn: null,
            Reservable: null,
            ServesBreakfast: null,
            ServesLunch: null,
            ServesDinner: null,
            ServesBeer: null,
            ServesWine: null,
            ServesBrunch: null,
            ServesVegetarianFood: null,
            OutdoorSeating: null,
            LiveMusic: null,
            MenuForChildren: null,
            ServesCocktails: null,
            ServesDessert: null,
            ServesCoffee: null,
            AllowsDogs: null,
            Restroom: null,
            GoodForGroups: null,
            GoodForWatchingSports: null,
            PaymentOptions: new PlacePaymentOptionsSummary(null, null, null, null),
            AccessibilityOptions: new PlaceAccessibilitySummary(null, null, null, null),
            EditorialSummary: new PlaceEditorialSummary(null, null),
            Location: new PlaceLocationSummary(53.3, -6.2),
            Photos: null);
    }

    private static CompanionPlaceSearchStrategy ParseStrategy(string json, string message)
    {
        var parser = new CompanionPlaceSearchStrategyJsonParser(new CompanionPlaceSearchStrategySanitizer());
        var success = parser.TryParse(
            SuccessfulAiResponse(json),
            BuildRequest(message),
            BuildIntent(placeQuery: message, rankingGoal: "intent_fit_then_distance"),
            out var strategy,
            out _,
            out var failure);
        Assert.True(success, failure);
        return strategy!;
    }

    private static AICompanionPlaceSearchStrategyPlanner BuildAiPlanner(string payload, RecordingTelemetry telemetry)
    {
        return new AICompanionPlaceSearchStrategyPlanner(
            new CompanionPlaceSearchStrategyPromptBuilder(),
            new CompanionPlaceSearchStrategyJsonParser(new CompanionPlaceSearchStrategySanitizer()),
            new FixedModelRouter(),
            new FixedAIClient(payload),
            new DeterministicCompanionPlaceSearchStrategyFallback(new CompanionGenericPlaceCategoryFallbackClassifier(), telemetry),
            Options.Create(new AIIntegrationOptions
            {
                Architecture = new ConversationArchitectureOptions
                {
                    PlacesStrategyPlannerV2Enabled = true,
                    PlacesStrategyPlannerModelBacked = true,
                    PlacesStrategyPlannerFallbackEnabled = true,
                    PlacesStrategyPlannerTimeoutMs = 4500
                }
            }),
            telemetry,
            NullLogger<AICompanionPlaceSearchStrategyPlanner>.Instance);
    }

    private static AIResponse SuccessfulAiResponse(string payload)
    {
        return new AIResponse(
            Content: payload,
            StructuredPayloadJson: payload,
            FinishReason: "stop",
            Provider: "test",
            Model: "test-model",
            Deployment: "test-deployment",
            InputTokenEstimate: null,
            OutputTokenEstimate: null,
            LatencyMs: 1,
            WasMocked: true,
            RawDiagnostics: null,
            Succeeded: true,
            FailureReason: null);
    }

    private static string AibBankStrategyJson()
    {
        return """
{"canonicalQuery":"AIB bank","entity":{"rawEntityText":"AIB","canonicalName":"AIB","aliases":["AIB","Allied Irish Bank"],"isBrandOrNamedEntity":true,"requiresEntityLock":true,"verificationRequired":true,"confidence":0.92},"role":{"requestedRole":"bank_branch","requiredCoreRoles":["bank","financial_institution"],"acceptableSubRoles":["bank"],"excludedSiblingRoles":["atm"],"modifiers":[],"categoryStrictness":"strict"},"searchVariants":[{"query":"AIB bank","purpose":"primary","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.93},{"query":"Allied Irish Bank","purpose":"alias","requiresEntityMatch":true,"requiresRoleMatch":true,"confidence":0.88}],"hardRequirements":[],"negativeRequirements":["atm"],"softPreferences":[],"nonSearchablePreferences":[],"rankingGoal":"brand_match_then_distance","maxCandidatePoolSize":50,"maxVisibleCards":10,"confidence":0.91,"warnings":[]}
""";
    }

    private static ResultContextSnapshot Snapshot(Guid id, string placeQuery)
    {
        return new ResultContextSnapshot(
            ResultSetId: id,
            ParentResultSetId: null,
            BranchRootResultSetId: id,
            SourceMode: ConversationMode.Exploration,
            SourceSubtype: ExplorationSubtype.Structured,
            QueryFingerprint: placeQuery,
            NormalizedConstraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pipeline"] = "places_intelligence_v2",
                ["semantic_place_query"] = placeQuery
            },
            SuggestedEntities: [],
            SelectedEntityId: null,
            ActiveUntilUtc: DateTime.UtcNow.AddMinutes(10),
            ExpiresUtc: DateTime.UtcNow.AddHours(1),
            IsExpired: false,
            IsActiveWindowExpired: false);
    }

    private sealed class NoOpChatTelemetry : IChatTelemetry
    {
        public Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTelemetry : IChatTelemetry
    {
        public List<(string Name, IReadOnlyDictionary<string, object?> Properties)> Events { get; } = [];

        public Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken)
        {
            Events.Add((eventName, new Dictionary<string, object?>(properties)));
            return Task.CompletedTask;
        }
    }

    private sealed class ConflictOnceAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public bool ThrowNextStateSnapshotSave { get; set; }
        public int SaveChangesCallCount { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowNextStateSnapshotSave
                && ChangeTracker.Entries<NSFinance.Api.Persistence.Entities.ConversationStateSnapshot>().Any(entry => entry.State == EntityState.Added))
            {
                ThrowNextStateSnapshotSave = false;
                SaveChangesCallCount++;
                throw new DbUpdateException(
                    "IX_ConversationStateSnapshots_ConversationThreadId_StateVersion duplicate key",
                    new InvalidOperationException("IX_ConversationStateSnapshots_ConversationThreadId_StateVersion duplicate key"));
            }

            SaveChangesCallCount++;
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CountingPlaceDetailsService : IPlaceDetailsService
    {
        public int CallCount { get; private set; }

        public Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                new PlaceDetailsResult(
                    PlaceId: placeId,
                    Name: placeId,
                    Address: "1 Test Street",
                    Website: $"https://{placeId}.example",
                    PriceLevel: null,
                    NationalPhoneNumber: "01 234 5678",
                    GoogleMapsUri: $"https://maps.example/{placeId}",
                    BusinessStatus: "OPERATIONAL",
                    Rating: 4.6,
                    UserRatingCount: 120,
                    PrimaryType: "cafe",
                    PrimaryTypeDisplayName: "Cafe",
                    Types: ["cafe"],
                    OpeningHours: new PlaceOpeningHoursSummary(true, [], null),
                    PaymentOptions: null,
                    AccessibilityOptions: null,
                    EditorialSummary: null,
                    Location: new PlaceLocationSummary(53.3, -6.2)));
        }
    }

    private sealed class InMemoryPlacesShortLivedCache : IPlacesShortLivedCache
    {
        private readonly Dictionary<string, object> values = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string provider, string placeId, string fieldMaskHash, CancellationToken ct)
        {
            return Task.FromResult(values.TryGetValue(Key(provider, placeId, fieldMaskHash), out var value) ? (T)value : default);
        }

        public Task SetAsync<T>(string provider, string placeId, string fieldMaskHash, T payload, TimeSpan ttl, CancellationToken ct)
        {
            if (payload is not null)
            {
                values[Key(provider, placeId, fieldMaskHash)] = payload;
            }

            return Task.CompletedTask;
        }

        private static string Key(string provider, string placeId, string fieldMaskHash)
        {
            return $"{provider}:{placeId}:{fieldMaskHash}";
        }
    }

    private sealed class NoOpPlaceRegistryService : IPlaceRegistryService
    {
        public Task RegisterSeenAsync(
            string provider,
            string providerPlaceId,
            IReadOnlyList<string> internalTags,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(AITaskType taskType, AIModelClass preferredModelClass, string? complexityHint = null)
        {
            return new AIModelRoute(
                taskType,
                preferredModelClass,
                "test-model",
                "test-deployment",
                IsFallback: false,
                Reason: complexityHint ?? "test",
                Notes: []);
        }
    }

    private sealed class FixedAIClient(string payload) : IAIClient
    {
        public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
        {
            return Task.FromResult(SuccessfulAiResponse(payload));
        }
    }

    private sealed class MultipassDiscoveryService(bool includeRichFields = false) : ICompanionPlaceDiscoveryService
    {
        public List<string> TextQueries { get; } = [];

        public Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
            CompanionPlaceDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            TextQueries.Add(request.Query);
            return Task.FromResult(Result(request.Query, request.MaxCandidates ?? 20));
        }

        public Task<CompanionPlaceDiscoveryResult> DiscoverNearbyAsync(
            CompanionNearbyDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result("nearby", request.MaxCandidates ?? 20));
        }

        private CompanionPlaceDiscoveryResult Result(string prefix, int count)
        {
            var candidates = Enumerable.Range(1, count)
                .Select(index => new CompanionPlaceCandidate(
                    PlaceId: $"{prefix}-{index}",
                    ResourceName: $"places/{prefix}-{index}",
                    DisplayName: $"{prefix} {index}",
                    PrimaryType: "cafe",
                    PrimaryTypeDisplayName: "Cafe",
                    Types: ["cafe"],
                    NationalPhoneNumber: includeRichFields ? "01 234 5678" : null,
                    FormattedAddress: includeRichFields ? "1 Rich Street" : null,
                    ShortFormattedAddress: "Short Street",
                    Rating: 4.5,
                    UserRatingCount: 100,
                    GoogleMapsUri: includeRichFields ? "https://maps.example" : null,
                    WebsiteUri: includeRichFields ? "https://example.com" : null,
                    OpeningHours: new PlaceOpeningHoursSummary(true, [], null),
                    BusinessStatus: "OPERATIONAL",
                    PriceLevel: null,
                    IconMaskBaseUri: null,
                    IconBackgroundColor: null,
                    Takeout: includeRichFields,
                    Delivery: includeRichFields,
                    DineIn: includeRichFields,
                    Reservable: includeRichFields,
                    ServesBreakfast: null,
                    ServesLunch: null,
                    ServesDinner: null,
                    ServesBeer: null,
                    ServesWine: includeRichFields,
                    ServesBrunch: null,
                    ServesVegetarianFood: null,
                    OutdoorSeating: null,
                    LiveMusic: null,
                    MenuForChildren: null,
                    ServesCocktails: null,
                    ServesDessert: null,
                    ServesCoffee: null,
                    AllowsDogs: null,
                    Restroom: null,
                    GoodForGroups: null,
                    GoodForWatchingSports: null,
                    PaymentOptions: new PlacePaymentOptionsSummary(null, null, null, null),
                    AccessibilityOptions: new PlaceAccessibilitySummary(null, null, null, null),
                    EditorialSummary: new PlaceEditorialSummary(includeRichFields ? "Rich details" : null, null),
                    Location: new PlaceLocationSummary(53.3, -6.2),
                    Photos: includeRichFields ? [new PlacePhotoSummary("places/photo", 100, 100)] : null))
                .ToArray();

            return new CompanionPlaceDiscoveryResult(
                Succeeded: true,
                Candidates: candidates,
                Metadata: new PlaceSearchMetadata("test", false, count, candidates.Length, "test", TimeSpan.Zero, false),
                Warnings: []);
        }
    }

    private sealed class LookupDiscoveryService(IReadOnlyDictionary<string, IReadOnlyList<CompanionPlaceCandidate>> results) : ICompanionPlaceDiscoveryService
    {
        public int CallCount { get; private set; }

        public Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
            CompanionPlaceDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var candidates = results.TryGetValue(request.Query, out var value) ? value : [];
            return Task.FromResult(new CompanionPlaceDiscoveryResult(
                Succeeded: true,
                Candidates: candidates,
                Metadata: new PlaceSearchMetadata("test", false, request.MaxCandidates ?? 5, candidates.Count, "test", TimeSpan.Zero, false),
                Warnings: []));
        }

        public Task<CompanionPlaceDiscoveryResult> DiscoverNearbyAsync(
            CompanionNearbyDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CompanionPlaceDiscoveryResult(
                Succeeded: true,
                Candidates: [],
                Metadata: new PlaceSearchMetadata("test", false, request.MaxCandidates ?? 5, 0, "test", TimeSpan.Zero, false),
                Warnings: []));
        }
    }

    private sealed class CapturingResultContextService : IResultContextService
    {
        public ResultContextWriteRequest? LastWrite { get; private set; }

        public Task<ResultContextReadResult> ReadAsync(
            ResultContextReadRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new ResultContextReadResult(
                    ActiveResultContext: null,
                    BindingClassification: ResultContextBindingClassification.None,
                    UsedClientResultSetId: false,
                    ExpiredBindingCleared: false,
                    ReasonCodes: []));
        }

        public Task<ResultContextWriteResult> WriteAsync(
            ResultContextWriteRequest request,
            CancellationToken cancellationToken)
        {
            LastWrite = request;
            var resultSetId = Guid.NewGuid();
            var snapshot = new ResultContextSnapshot(
                ResultSetId: resultSetId,
                ParentResultSetId: request.ParentResultSetId,
                BranchRootResultSetId: request.BranchRootResultSetId ?? resultSetId,
                SourceMode: request.SourceMode,
                SourceSubtype: request.SourceSubtype,
                QueryFingerprint: request.QueryFingerprint,
                NormalizedConstraints: request.NormalizedConstraints,
                SuggestedEntities: request.SuggestedEntities,
                SelectedEntityId: request.SelectedEntityId,
                ActiveUntilUtc: request.CreatedUtc.AddMinutes(30),
                ExpiresUtc: request.CreatedUtc.AddHours(2),
                IsExpired: false,
                IsActiveWindowExpired: false);
            return Task.FromResult(
                new ResultContextWriteResult(
                    snapshot,
                    new ConversationResultContextReference(
                        resultSetId,
                        snapshot.BranchRootResultSetId,
                        snapshot.ActiveUntilUtc,
                        snapshot.ExpiresUtc),
                    ReasonCodes: []));
        }

        public Task<ResultContextWriteResult?> TrySelectEntityAsync(
            Guid userId,
            Guid conversationThreadId,
            Guid resultSetId,
            string selectedEntityId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ResultContextWriteResult?>(null);
        }

        public Task<ResultContextSnapshot?> GetLatestPlacesV2ContextAsync(
            Guid userId,
            Guid conversationThreadId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ResultContextSnapshot?>(null);
        }

        public Task ClearExpiredBindingsAsync(Guid conversationThreadId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
