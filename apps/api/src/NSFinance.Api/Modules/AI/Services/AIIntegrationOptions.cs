namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIIntegrationOptions
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; } = true;
    public bool UseMockProvider { get; set; } = true;
    public AIProviderKind ProviderKind { get; set; } = AIProviderKind.Mock;
    public AIModelRoutingOptions Routing { get; set; } = new();
    public AIExecutionOptions Execution { get; set; } = new();
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
