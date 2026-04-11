namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIIntegrationOptions
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; } = true;
    public bool UseMockProvider { get; set; } = true;
    public AIProviderKind ProviderKind { get; set; } = AIProviderKind.Mock;
    public AIModelRoutingOptions Routing { get; set; } = new();
    public AIExecutionOptions Execution { get; set; } = new();
    public ConversationMemoryOptions Memory { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
    public MockAIProviderOptions Mock { get; set; } = new();
}

public sealed class AIModelRoutingOptions
{
    public string FastModelName { get; set; } = "gpt-4.1";
    public string FastDeploymentName { get; set; } = "gpt-4-1-chat";
    public string HeavyModelName { get; set; } = "gpt-5-chat";
    public string HeavyDeploymentName { get; set; } = "merchant-investigation";
    public bool HeavyModelEnabled { get; set; }
    public HeavyModelFallbackPolicy HeavyModelFallbackPolicy { get; set; } = HeavyModelFallbackPolicy.UseFastModel;
}

public sealed class AIExecutionOptions
{
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 2;
    public int RetryBaseDelayMs { get; set; } = 200;
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
