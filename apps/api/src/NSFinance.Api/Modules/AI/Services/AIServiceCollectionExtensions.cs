using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public static class AIServiceCollectionExtensions
{
    public static IServiceCollection AddAIIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AIIntegrationOptions>()
            .Bind(configuration.GetSection(AIIntegrationOptions.SectionName))
            .PostConfigure(AIIntegrationOptionsNormalizer.Normalize)
            .Services
            .AddSingleton<IValidateOptions<AIIntegrationOptions>, AIIntegrationOptionsValidator>();

        services.AddOptions<AIIntegrationOptions>()
            .ValidateOnStart();

        services.AddHttpClient("AI.AzureOpenAI", (sp, client) =>
        {
            var aiOptions = sp.GetRequiredService<IOptions<AIIntegrationOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.Execution.TimeoutSeconds, 5, 120));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NSFinance.Api/AIIntegration/1.0");
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
