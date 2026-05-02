using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public static class AIServiceCollectionExtensions
{
    private const string AzureAIClientUserAgent = "NSFinance.Api.AIIntegration/1.0";

    public static IServiceCollection AddAIIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AIIntegrationOptions>()
            .Bind(configuration.GetSection(AIIntegrationOptions.SectionName))
            .PostConfigure(AIIntegrationOptionsNormalizer.Normalize)
            .Services
            .AddSingleton<IValidateOptions<AIIntegrationOptions>, AIIntegrationOptionsValidator>();

        services.AddOptions<AIIntegrationOptions>()
            .ValidateOnStart();
        services.AddOptions<CompanionProfileLifecycleOptions>()
            .Bind(configuration.GetSection(CompanionProfileLifecycleOptions.SectionName))
            .Validate(
                options => options.StaleAfterHours > 0,
                "Companion profile stale threshold must be > 0")
            .Validate(
                options => options.RefreshNeededAfterHours > options.StaleAfterHours,
                "Companion profile refresh-needed threshold must be > stale threshold")
            .Validate(
                options => options.MaxActivePlans > 0,
                "Companion profile max active plans must be > 0")
            .Validate(
                options => options.MaxRecurringObligations > 0,
                "Companion profile max recurring obligations must be > 0")
            .Validate(
                options => options.SpendingAnalysisLookbackDays >= 14,
                "Companion profile spending lookback days must be >= 14")
            .Validate(
                options => options.ProfileSchemaVersion > 0,
                "Companion profile schema version must be > 0")
            .ValidateOnStart();
        services.AddOptions<CompanionAdviceOptions>()
            .Bind(configuration.GetSection(CompanionAdviceOptions.SectionName))
            .Validate(
                options => options.MaxAdjudicatedFindings > 0,
                "Companion advice max adjudicated findings must be > 0")
            .Validate(
                options => options.MaxAdjudicationInputChars >= 1_000,
                "Companion advice adjudication input chars must be >= 1000")
            .Validate(
                options => options.MaxAdjudicationOutputTokens >= 120,
                "Companion advice adjudication output tokens must be >= 120")
            .Validate(
                options => options.BorderlineConfidenceThreshold >= 0.4d
                           && options.BorderlineConfidenceThreshold <= 0.9d,
                "Companion advice borderline confidence threshold must be in [0.4,0.9]")
            .Validate(
                options => options.HighConfidenceSkipThreshold >= options.BorderlineConfidenceThreshold,
                "Companion advice high confidence threshold must be >= borderline threshold")
            .Validate(
                options => options.CategoryPressureIncreaseRatioThreshold >= 1.05m,
                "Companion advice category pressure ratio threshold must be >= 1.05")
            .Validate(
                options => options.RecurringPressureIncreaseRatioThreshold >= 1.01m,
                "Companion advice recurring pressure ratio threshold must be >= 1.01")
            .Validate(
                options => options.RecurringToIncomePressureRatioThreshold > 0m,
                "Companion advice recurring-to-income ratio threshold must be > 0")
            .Validate(
                options => options.BudgetLowRemainingRatioThreshold > 0m,
                "Companion advice budget low remaining ratio threshold must be > 0")
            .Validate(
                options => options.AffordabilityBufferRatioThreshold > 0m,
                "Companion advice affordability buffer ratio threshold must be > 0")
            .Validate(
                options => options.BaseFreshnessHoursHighSeverity > 0,
                "Companion advice freshness window for high severity must be > 0")
            .Validate(
                options => options.BaseFreshnessHoursModerateSeverity > 0,
                "Companion advice freshness window for moderate severity must be > 0")
            .Validate(
                options => options.BaseFreshnessHoursLowSeverity > 0,
                "Companion advice freshness window for low severity must be > 0")
            .Validate(
                options => options.BaseFreshnessHoursInfoSeverity > 0,
                "Companion advice freshness window for info severity must be > 0")
            .ValidateOnStart();
        services.AddOptions<GooglePlacesOptions>()
            .Bind(configuration.GetSection(GooglePlacesOptions.SectionName))
            .Services
            .AddSingleton<IValidateOptions<GooglePlacesOptions>, GooglePlacesOptionsValidator>();
        services.AddOptions<GooglePlacesOptions>()
            .ValidateOnStart();

        services.AddHttpClient("AI.AzureOpenAI", (sp, client) =>
        {
            var aiOptions = sp.GetRequiredService<IOptions<AIIntegrationOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.Execution.TimeoutSeconds, 5, 120));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AzureAIClientUserAgent);
        });
        services.AddHttpClient<IGooglePlacesClient, GooglePlacesClient>((sp, client) =>
        {
            var placesOptions = sp.GetRequiredService<IOptions<GooglePlacesOptions>>().Value;
            if (Uri.TryCreate(placesOptions.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, placesOptions.TimeoutSeconds));
        });
        services.AddHttpClient<IGooglePlacesPhotoService, GooglePlacesPhotoService>((sp, client) =>
        {
            var placesOptions = sp.GetRequiredService<IOptions<GooglePlacesOptions>>().Value;
            if (Uri.TryCreate(placesOptions.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, placesOptions.TimeoutSeconds));
        });

        services.AddSingleton<AzureOpenAIApiKeyAuthStrategy>();
        services.AddSingleton<AzureOpenAIManagedIdentityAuthStrategy>();
        services.AddSingleton<IAIProviderCircuitBreaker, AIProviderCircuitBreaker>();
        services.AddSingleton<IMerchantInvestigationResultCache, InMemoryMerchantInvestigationResultCache>();
        services.AddSingleton<IGooglePlacesCache, InMemoryGooglePlacesCache>();
        services.AddSingleton<IGooglePlacesCacheKeyBuilder, GooglePlacesCacheKeyBuilder>();
        services.AddSingleton<IGooglePlacesFieldMaskProvider, GooglePlacesFieldMaskProvider>();
        services.AddSingleton<IChatTelemetry, ChatTelemetry>();

        services.AddScoped<IAIProviderTransport, MockAIProviderTransport>();
        services.AddScoped<IAIProviderTransport, AzureOpenAIProviderTransport>();
        services.AddScoped<IOperationalFailureRecorder, OperationalFailureRecorder>();

        services.AddScoped<IAIClient, AIClient>();
        services.AddScoped<IAIModelRouter, AIModelRouter>();
        services.AddScoped<IConversationContextService, ConversationContextService>();
        services.AddScoped<IConversationBehaviorEngine, ConversationBehaviorEngine>();
        services.AddScoped<IConversationDecisionEngine, ConversationDecisionEngine>();
        services.AddScoped<IDeterministicConversationDecisionBuilder, DeterministicConversationDecisionBuilder>();
        services.AddScoped<IConversationModelRoutingPolicy, ConversationModelRoutingPolicy>();
        services.AddScoped<IModeRouter, ModeRouter>();
        services.AddScoped<IReadinessTransitionPolicy, ReadinessTransitionPolicy>();
        services.AddScoped<IFollowUpBindingPolicy, FollowUpBindingPolicy>();
        services.AddScoped<IContradictionResolutionPolicy, ContradictionResolutionPolicy>();
        services.AddScoped<IFinancialActivationPolicy, FinancialActivationPolicy>();
        services.AddScoped<IExplorationSubtypeDecisionPolicy, ExplorationSubtypeDecisionPolicy>();
        services.AddScoped<IToolGuardWarningPolicy, ToolGuardWarningPolicy>();
        services.AddScoped<IResultContextService, ResultContextService>();
        services.AddScoped<IResponseComposer, ResponseComposer>();
        services.AddScoped<IConversationIntelligenceService, ConversationIntelligenceService>();
        services.AddScoped<ICompanionActionResolver, CompanionActionResolver>();
        services.AddScoped<IPlaceResultFollowUpService, PlaceResultFollowUpService>();
        services.AddScoped<ICompanionSemanticIntentService, CompanionSemanticIntentService>();
        services.AddScoped<ICompanionPlaceCandidatePoolService, CompanionPlaceCandidatePoolService>();
        services.AddScoped<ICompanionPlaceConstraintEngine, CompanionPlaceConstraintEngine>();
        services.AddScoped<ICompanionPlaceIntelligenceRankingService, CompanionPlaceIntelligenceRankingService>();
        services.AddScoped<ICompanionPlaceFinalistEnrichmentService, CompanionPlaceFinalistEnrichmentService>();
        services.AddScoped<ICompanionPlaceSessionMemoryService, CompanionPlaceSessionMemoryService>();
        services.AddScoped<ICompanionPlaceResultContextBinder, CompanionPlaceResultContextBinder>();
        services.AddScoped<ICompanionPlaceParkingEvidenceService, CompanionPlaceParkingEvidenceService>();
        services.AddScoped<ICompanionPlaceDuplicateClusterService, CompanionPlaceDuplicateClusterService>();
        services.AddScoped<ICompanionPlaceCategoryCompatibilityService, CompanionPlaceCategoryCompatibilityService>();
        services.AddScoped<ICompanionPlaceBrandIdentityService, CompanionPlaceBrandIdentityService>();
        services.AddScoped<ICompanionGenericPlaceCategoryFallbackClassifier, CompanionGenericPlaceCategoryFallbackClassifier>();
        services.AddScoped<IDeterministicCompanionPlaceSearchStrategyFallback, DeterministicCompanionPlaceSearchStrategyFallback>();
        services.AddScoped<ICompanionPlaceSearchStrategyPlanner, AICompanionPlaceSearchStrategyPlanner>();
        services.AddScoped<ICompanionPlaceSearchStrategyPromptBuilder, CompanionPlaceSearchStrategyPromptBuilder>();
        services.AddScoped<ICompanionPlaceSearchStrategyJsonParser, CompanionPlaceSearchStrategyJsonParser>();
        services.AddScoped<ICompanionPlaceSearchStrategySanitizer, CompanionPlaceSearchStrategySanitizer>();
        services.AddScoped<ICompanionPlaceEntityVerificationService, CompanionPlaceEntityVerificationService>();
        services.AddScoped<ICompanionPlaceSearchVariantValidator, CompanionPlaceSearchVariantValidator>();
        services.AddScoped<ICompanionPlaceTypeFamilyClassifier, CompanionPlaceTypeFamilyClassifier>();
        services.AddScoped<IPlacesShortLivedCache, PlacesShortLivedCache>();
        services.AddScoped<IPlaceRegistryService, PlaceRegistryService>();
        services.AddScoped<ITurnInterpretationPromptBuilder, TurnInterpretationPromptBuilder>();
        services.AddScoped<ITurnInterpretationParser, TurnInterpretationParser>();
        services.AddScoped<ITurnInterpretationEngine, TurnInterpretationEngine>();
        services.AddScoped<IPlaceRetrievalPlanner, PlaceRetrievalPlanner>();
        services.AddScoped<IConversationIntelligencePromptBuilder, ConversationIntelligencePromptBuilder>();
        services.AddScoped<IConversationDecisionPromptBuilder, ConversationDecisionPromptBuilder>();
        services.AddScoped<IExplorationSubtypePromptBuilder, ExplorationSubtypePromptBuilder>();
        services.AddScoped<IResponseCompositionPromptBuilder, ResponseCompositionPromptBuilder>();
        services.AddScoped<IMerchantInvestigationPromptBuilder, MerchantInvestigationPromptBuilder>();
        services.AddScoped<IConversationDecisionParser, ConversationDecisionParser>();
        services.AddScoped<IConversationIntelligenceParser, ConversationIntelligenceParser>();
        services.AddScoped<IExplorationSubtypeDecisionParser, ExplorationSubtypeDecisionParser>();
        services.AddScoped<IConversationModeHandler, StructuredExplorationHandler>();
        services.AddScoped<IConversationModeHandler, OpenExplorationHandler>();
        services.AddScoped<IConversationModeHandler, FinancialModeHandler>();
        services.AddScoped<IConversationModeHandler, GeneralKnowledgeModeHandler>();
        services.AddScoped<IConversationThreadService, ConversationThreadService>();
        services.AddScoped<IConversationTurnService, ConversationTurnService>();
        services.AddScoped<IConversationMessageService, ConversationMessageService>();
        services.AddScoped<IConversationStateService, ConversationStateService>();
        services.AddScoped<IConversationSummaryGenerator, DeterministicConversationSummaryGenerator>();
        services.AddScoped<IConversationSummaryService, ConversationSummaryService>();
        services.AddScoped<IPersistentConversationContextService, PersistentConversationContextService>();
        services.AddScoped<IUserFinancialSummaryService, UserFinancialSummaryService>();
        services.AddScoped<ISpendingAnalysisService, SpendingAnalysisService>();
        services.AddScoped<IRecurringObligationsService, RecurringObligationsService>();
        services.AddScoped<IBudgetStatusService, BudgetStatusService>();
        services.AddScoped<ITransactionQueryService, TransactionQueryService>();
        services.AddScoped<IUserFinancialProfileSerializationMapper, UserFinancialProfileSerializationMapper>();
        services.AddScoped<IUserFinancialProfileMergePolicy, UserFinancialProfileMergePolicy>();
        services.AddScoped<IUserFinancialProfileInferenceBuilder, UserFinancialProfileInferenceBuilder>();
        services.AddScoped<IUserFinancialProfileInferencePersistencePolicy, UserFinancialProfileInferencePersistencePolicy>();
        services.AddScoped<IUserFinancialProfileSignalMetadataPolicy, UserFinancialProfileSignalMetadataPolicy>();
        services.AddScoped<IUserFinancialProfileLifecycleInvariantValidator, UserFinancialProfileLifecycleInvariantValidator>();
        services.AddScoped<IUserFinancialProfileFreshnessEvaluator, UserFinancialProfileFreshnessEvaluator>();
        services.AddScoped<IUserFinancialContextProfileService, UserFinancialContextProfileService>();
        services.AddScoped<ICompanionPlaceDiscoveryService, CompanionPlaceDiscoveryService>();
        services.AddScoped<IMerchantPlaceLookupService, MerchantPlaceLookupService>();
        services.AddScoped<ILocalDiscoveryConstraintExtractor, LocalDiscoveryConstraintExtractor>();
        services.AddScoped<ILocalDiscoveryQueryShaper, LocalDiscoveryQueryShaper>();
        services.AddScoped<IRealWorldDomainCapabilityCatalog, RealWorldDomainCapabilityCatalog>();
        services.AddScoped<IRealWorldProductDomainEligibilityPolicy, RealWorldProductDomainEligibilityPolicy>();
        services.AddScoped<ICompanionLocalityResolutionService, CompanionLocalityResolutionService>();
        services.AddScoped<ICompanionPlacesVocabularyNormalizer, CompanionPlacesVocabularyNormalizer>();
        services.AddScoped<ICompanionPlacesTextQueryBuilder, CompanionPlacesTextQueryBuilder>();
        services.AddScoped<ICompanionPlacesNearbyRequestBuilder, CompanionPlacesNearbyRequestBuilder>();
        services.AddScoped<ICompanionNearbyTypeMapper, CompanionNearbyTypeMapper>();
        services.AddScoped<ICompanionNearbyHybridRetrievalPolicy, CompanionNearbyHybridRetrievalPolicy>();
        services.AddScoped<ICompanionPlaceRankingPolicy, CompanionPlaceRankingPolicy>();
        services.AddScoped<IPlacesSearchService, GooglePlacesCompanionSearchService>();
        services.AddScoped<IPlaceDetailsService, GooglePlacesPlaceDetailsService>();
        services.AddScoped<IReviewInsightsService, NullReviewInsightsService>();
        services.AddScoped<ICompanionProfileBaselineBuilder, CompanionProfileBaselineBuilder>();
        services.AddScoped<IInsightInvalidationHintBuilder, InsightInvalidationHintBuilder>();
        services.AddScoped<IInsightFreshnessEvaluator, InsightFreshnessEvaluator>();
        services.AddScoped<IFinancialAdviceFindingFactory, FinancialAdviceFindingFactory>();
        services.AddScoped<IFinancialAdviceCategoryClassifier, FinancialAdviceCategoryClassifier>();
        services.AddScoped<CategoryPressureEvaluator>();
        services.AddScoped<RecurringSpendEvaluator>();
        services.AddScoped<BudgetHealthEvaluator>();
        services.AddScoped<AffordabilityEvaluator>();
        services.AddScoped<PlanDriftEvaluator>();
        services.AddScoped<PositiveSignalEvaluator>();
        services.AddScoped<IFinancialAdviceEngine, FinancialAdviceEngine>();
        services.AddScoped<IProtectedPreferenceHintParser, ProtectedPreferenceHintParser>();
        services.AddScoped<IProtectedCategoryPolicy, ProtectedCategoryPolicy>();
        services.AddScoped<IReductionSafetyPolicy, ReductionSafetyPolicy>();
        services.AddScoped<IConfidenceAdjustmentPolicy, ConfidenceAdjustmentPolicy>();
        services.AddScoped<IFindingRejectionPolicy, FindingRejectionPolicy>();
        services.AddScoped<IFinancialAdvicePolicyService, FinancialAdvicePolicyService>();
        services.AddScoped<IAdjudicationPromptBuilder, AdjudicationPromptBuilder>();
        services.AddScoped<IAdjudicationInputSanitizer, AdjudicationInputSanitizer>();
        services.AddScoped<IAdjudicationResultParser, AdjudicationResultParser>();
        services.AddScoped<IAdjudicationResultValidator, AdjudicationResultValidator>();
        services.AddScoped<IFinancialAdviceAdjudicationService, FinancialAdviceAdjudicationService>();
        services.AddScoped<IFinancialAdviceAdjudicationPlanSelector, FinancialAdviceAdjudicationPlanSelector>();
        services.AddScoped<IAdviceEvidenceSummaryBuilder, AdviceEvidenceSummaryBuilder>();
        services.AddScoped<IAdviceLifecycleMetadataBuilder, AdviceLifecycleMetadataBuilder>();
        services.AddScoped<IAdviceSummaryBuilder, AdviceSummaryBuilder>();
        services.AddScoped<IAdvicePacketBuilder, AdvicePacketBuilder>();
        services.AddScoped<IFinancialAdviceDecisionService, FinancialAdviceDecisionService>();
        services.AddScoped<IMerchantInvestigationResponseParser, MerchantInvestigationResponseParser>();
        services.AddScoped<IUserChatResponseParser, UserChatResponseParser>();
        services.AddScoped<IMerchantInvestigationOrchestrator, MerchantInvestigationOrchestrator>();
        services.AddScoped<IUserChatOrchestrator, ConversationLayerOrchestrator>();

        // Keep existing merchant resolution seam intact while allowing provider swap via AI options.
        services.AddScoped<IMerchantInvestigationService, AIBackedMerchantInvestigationService>();

        services.AddHostedService<AIConfigurationStartupLogger>();

        return services;
    }
}
