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
            .Validate(options => options.MaxTokensPerResponse > 0, "CompanionAI max tokens must be > 0")
            .Validate(options => options.MaxTurnsPerSession > 0, "CompanionAI max turns must be > 0")
            .Validate(options => options.DailySoftCapPerUser > 0, "CompanionAI daily soft cap must be > 0")
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
        services.AddScoped<IUserFinancialContextProfileService, UserFinancialContextProfileService>();
        services.AddScoped<IPlacesSearchService, NullPlacesSearchService>();
        services.AddScoped<IPlaceDetailsService, NullPlaceDetailsService>();
        services.AddScoped<IReviewInsightsService, NullReviewInsightsService>();
        services.AddScoped<ICompanionIntentNormalizer, CompanionIntentNormalizer>();
        services.AddScoped<ICompanionIntentSignalExtractor, CompanionIntentSignalExtractor>();
        services.AddScoped<ICompanionIntentScorer, CompanionIntentScorer>();
        services.AddScoped<ICompanionIntentResolutionPolicy, CompanionIntentResolutionPolicy>();
        services.AddScoped<ICompanionIntentRouter, CompanionIntentRouter>();
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
