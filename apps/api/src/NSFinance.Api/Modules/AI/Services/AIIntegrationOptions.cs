namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIIntegrationOptions
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; } = true;
    public bool UseMockProvider { get; set; } = true;
    public string? Provider { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public AIProviderKind ProviderKind { get; set; } = AIProviderKind.Mock;
    public AIModelRoutingOptions Routing { get; set; } = new();
    public AIModelNameOptions Models { get; set; } = new();
    public AIExecutionOptions Execution { get; set; } = new();
    public ConversationMemoryOptions Memory { get; set; } = new();
    public ChatTurnOptions ChatTurns { get; set; } = new();
    public ConversationArchitectureOptions Architecture { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
    public MockAIProviderOptions Mock { get; set; } = new();

    // Runtime-only diagnostic marker set during normalization.
    public bool AliasNormalizationApplied { get; set; }
}

public sealed class AIModelRoutingOptions
{
    public string FastModelName { get; set; } = string.Empty;
    public string FastDeploymentName { get; set; } = string.Empty;
    public string HeavyModelName { get; set; } = string.Empty;
    public string HeavyDeploymentName { get; set; } = string.Empty;
    // Nullable on purpose so "not configured" can be distinguished from an explicit false.
    public bool? HeavyModelEnabled { get; set; }
    public HeavyModelFallbackPolicy HeavyModelFallbackPolicy { get; set; } = HeavyModelFallbackPolicy.UseFastModel;
}

public sealed class AIModelNameOptions
{
    // Legacy alias input only. Runtime routing must use AI:Routing:* values.
    public string? Fast { get; set; }
    // Legacy alias input only. Runtime routing must use AI:Routing:* values.
    public string? Heavy { get; set; }
}

public sealed class AIExecutionOptions
{
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 2;
    public int RetryBaseDelayMs { get; set; } = 200;
    public bool CircuitBreakerEnabled { get; set; } = true;
    public int CircuitBreakerFailureThreshold { get; set; } = 4;
    public int CircuitBreakerOpenSeconds { get; set; } = 60;
    public int CircuitBreakerRateLimitOpenSeconds { get; set; } = 180;
    public int CircuitBreakerAuthOpenSeconds { get; set; } = 300;
    public int MerchantInvestigationResultCacheSeconds { get; set; } = 300;
    public int MerchantInvestigationFailureCacheSeconds { get; set; } = 120;
    public int MaxContextTurns { get; set; } = 12;
    public int MaxSummaryEntries { get; set; } = 2;
}

public sealed class AzureOpenAIOptions
{
    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public bool UseManagedIdentity { get; set; }
    public string ApiVersion { get; set; } = "2024-10-21";
}

public sealed class MockAIProviderOptions
{
    public bool Enabled { get; set; } = true;
    public MockAIScenario DefaultMerchantScenario { get; set; } = MockAIScenario.MerchantInsufficientEvidence;
    public MockAIScenario DefaultSimpleChatScenario { get; set; } = MockAIScenario.UserChatSimple;
    public MockAIScenario DefaultComplexChatScenario { get; set; } = MockAIScenario.UserChatComplex;
}

public sealed class ConversationMemoryOptions
{
    public bool Enabled { get; set; } = true;
    public bool BuildContextLogsEnabled { get; set; } = true;
    public int RecentMessageFetchMultiplier { get; set; } = 3;
    public int SummaryRefreshMessageThreshold { get; set; } = 18;
    public int SummaryRefreshMessageDeltaThreshold { get; set; } = 10;
    public int SummaryRefreshTokenEstimateThreshold { get; set; } = 1800;
    public int MaxSummaryLengthChars { get; set; } = 1400;
    public int MaxStateEntries { get; set; } = 16;
    public int MaxStateValueLength { get; set; } = 240;
    public TaskContextBudgetOptions SimpleChat { get; set; } = new()
    {
        MaxRecentMessages = 10,
        MaxPromptTokens = 1200,
        MaxSummaryChars = 450,
        MaxStateEntries = 8
    };
    public TaskContextBudgetOptions ComplexChat { get; set; } = new()
    {
        MaxRecentMessages = 16,
        MaxPromptTokens = 2500,
        MaxSummaryChars = 800,
        MaxStateEntries = 12
    };
    public TaskContextBudgetOptions MerchantInvestigation { get; set; } = new()
    {
        MaxRecentMessages = 8,
        MaxPromptTokens = 1200,
        MaxSummaryChars = 360,
        MaxStateEntries = 6
    };
    public TaskContextBudgetOptions FinancialReasoning { get; set; } = new()
    {
        MaxRecentMessages = 18,
        MaxPromptTokens = 3000,
        MaxSummaryChars = 950,
        MaxStateEntries = 14
    };
    public TaskContextBudgetOptions Default { get; set; } = new();
}

public sealed class TaskContextBudgetOptions
{
    public int MaxRecentMessages { get; set; } = 12;
    public int MaxPromptTokens { get; set; } = 1800;
    public int MaxSummaryChars { get; set; } = 600;
    public int MaxStateEntries { get; set; } = 10;
}

public sealed class ChatTurnOptions
{
    public int MaxUserMessageChars { get; set; } = 4000;
    public int MaxClientRequestIdLength { get; set; } = 128;
    public bool AllowImplicitTransientFallback { get; set; }
    public bool AllowExplicitTransientFallbackInProduction { get; set; }
    public bool AllowImplicitTransientFallbackInProduction { get; set; }
    public bool RequirePersistentMemoryWhenRequested { get; set; }
}

public sealed class ConversationArchitectureOptions
{
    public bool EmitTelemetryEvents { get; set; } = true;
    public bool InterpretationEnabled { get; set; } = true;
    public bool ConversationIntelligenceEnabled { get; set; } = true;
    public bool CompanionActionResolverEnabled { get; set; } = true;
    public bool PlacesFollowUpExecutionEnabled { get; set; } = true;
    public bool PlacesBrandFirstEnabled { get; set; } = true;
    public bool PlacesOpenWorldConceptRankingEnabled { get; set; } = true;
    public bool ResponseCompositionAIScriptlessEnabled { get; set; } = true;
    public int ResultContextActiveMinutes { get; set; } = 30;
    public int ResultContextPersistedHours { get; set; } = 24;
    public int ExplorationConstraintTtlMinutes { get; set; } = 30;
    public ConversationModelTierOptions Tiers { get; set; } = new();
}

public sealed class ConversationModelTierOptions
{
    public string L1DecisionModelName { get; set; } = string.Empty;
    public string L1DecisionDeploymentName { get; set; } = string.Empty;
    public string L2CompositionModelName { get; set; } = string.Empty;
    public string L2CompositionDeploymentName { get; set; } = string.Empty;
}
