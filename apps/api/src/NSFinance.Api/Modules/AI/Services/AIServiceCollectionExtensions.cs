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
        services.AddOptions<CompanionAISettingsOptions>()
            .Bind(configuration.GetSection(CompanionAISettingsOptions.SectionName))
            .Validate(
                options => options.MaxTokensPerResponse > 0,
                "CompanionAI max tokens must be > 0")
            .Validate(
                options => options.MaxTurnsPerSession > 0,
                "CompanionAI max turns must be > 0")
            .Validate(
                options => options.DailySoftCapPerUser > 0,
                "CompanionAI daily soft cap must be > 0")
            .ValidateOnStart();
        services.AddOptions<CompanionOrchestrationOptions>()
            .Bind(configuration.GetSection(CompanionOrchestrationOptions.SectionName))
            .Validate(
                options => options.MaxToolCallsPerRequest > 0,
                "Companion orchestration max tool calls must be > 0")
            .Validate(
                options => options.MaxContextKeys > 0,
                "Companion orchestration max context keys must be > 0")
            .Validate(
                options => options.MaxSerializedContextChars > 1_000,
                "Companion orchestration max serialized context chars must be > 1000")
            .Validate(
                options => options.MaxSpendDomains > 0,
                "Companion orchestration max spend domains must be > 0")
            .Validate(
                options => options.MaxRecurringItems > 0,
                "Companion orchestration max recurring items must be > 0")
            .Validate(
                options => options.MaxTransactionRows > 0,
                "Companion orchestration max transaction rows must be > 0")
            .Validate(
                options => options.MaxPlaceItems > 0,
                "Companion orchestration max place items must be > 0")
            .Validate(
                options => options.MaxSummaryTextLength >= 60,
                "Companion orchestration max summary text length must be >= 60")
            .Validate(
                options => options.MaxSecondaryOptionalTools >= 0,
                "Companion orchestration max secondary optional tools must be >= 0")
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

        services.AddHttpClient("AI.AzureOpenAI", (sp, client) =>
        {
            var aiOptions = sp.GetRequiredService<IOptions<AIIntegrationOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.Execution.TimeoutSeconds, 5, 120));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AzureAIClientUserAgent);
        });

        services.AddSingleton<AzureOpenAIApiKeyAuthStrategy>();
        services.AddSingleton<AzureOpenAIManagedIdentityAuthStrategy>();
        services.AddSingleton<IAIProviderCircuitBreaker, AIProviderCircuitBreaker>();
        services.AddSingleton<IMerchantInvestigationResultCache, InMemoryMerchantInvestigationResultCache>();

        services.AddScoped<IAIProviderTransport, MockAIProviderTransport>();
        services.AddScoped<IAIProviderTransport, AzureOpenAIProviderTransport>();
        services.AddScoped<IOperationalFailureRecorder, OperationalFailureRecorder>();

        services.AddScoped<IAIClient, AIClient>();
        services.AddScoped<IAIModelRouter, AIModelRouter>();
        services.AddScoped<IUserChatComplexityClassifier, UserChatComplexityClassifier>();
        services.AddScoped<IConversationContextService, ConversationContextService>();
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
        services.AddScoped<IPlacesSearchService, NullPlacesSearchService>();
        services.AddScoped<IPlaceDetailsService, NullPlaceDetailsService>();
        services.AddScoped<IReviewInsightsService, NullReviewInsightsService>();
        services.AddScoped<ICompanionIntentNormalizer, CompanionIntentNormalizer>();
        services.AddScoped<ICompanionIntentSignalExtractor, CompanionIntentSignalExtractor>();
        services.AddScoped<ICompanionIntentScorer, CompanionIntentScorer>();
        services.AddScoped<ICompanionIntentResolutionPolicy, CompanionIntentResolutionPolicy>();
        services.AddScoped<ICompanionIntentRouter, CompanionIntentRouter>();
        services.AddScoped<ICompanionIntentToolPolicyProvider, CompanionIntentToolPolicyProvider>();
        services.AddScoped<ICompanionMixedIntentMergePolicy, CompanionMixedIntentMergePolicy>();
        services.AddScoped<ICompanionExecutionPlanBuilder, CompanionExecutionPlanBuilder>();
        services.AddScoped<ICompanionContextShaper, CompanionContextShaper>();
        services.AddScoped<ICompanionToolExecutor, CompanionToolExecutor>();
        services.AddScoped<ICompanionInsufficiencyEvaluator, CompanionInsufficiencyEvaluator>();
        services.AddScoped<ICompanionEvidenceBuilder, CompanionEvidenceBuilder>();
        services.AddScoped<ICompanionAssemblyResultBuilder, CompanionAssemblyResultBuilder>();
        services.AddScoped<IFinancialCompanionContextAssembler, FinancialCompanionContextAssembler>();
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
        services.AddScoped<IFinancialCompanionService, FinancialCompanionService>();
        services.AddScoped<IPromptBuilder, AIPromptBuilder>();
        services.AddScoped<IMerchantInvestigationResponseParser, MerchantInvestigationResponseParser>();
        services.AddScoped<IUserChatResponseParser, UserChatResponseParser>();
        services.AddScoped<IMerchantInvestigationOrchestrator, MerchantInvestigationOrchestrator>();
        services.AddScoped<IUserChatOrchestrator, UserChatOrchestrator>();

        // Keep existing merchant resolution seam intact while allowing provider swap via AI options.
        services.AddScoped<IMerchantInvestigationService, AIBackedMerchantInvestigationService>();

        services.AddHostedService<AIConfigurationStartupLogger>();

        return services;
    }
}
