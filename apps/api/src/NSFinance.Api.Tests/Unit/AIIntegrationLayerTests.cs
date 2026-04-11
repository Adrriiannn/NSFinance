using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Tests.Unit;

public sealed class AIIntegrationLayerTests
{
    [Fact]
    public void ModelRouter_RoutesMerchantInvestigationToHeavy_WhenHeavyEnabled()
    {
        var router = CreateRouter(options => options.Routing.HeavyModelEnabled = true);

        var route = router.Resolve(AITaskType.MerchantInvestigation, AIModelClass.HeavyReasoning, null);

        Assert.Equal(AIModelClass.HeavyReasoning, route.ModelClass);
        Assert.Equal("gpt-5-chat", route.Model);
        Assert.False(route.IsFallback);
    }

    [Fact]
    public void ModelRouter_FallsBackToFast_WhenHeavyDisabledAndFallbackEnabled()
    {
        var router = CreateRouter(options =>
        {
            options.UseMockProvider = false;
            options.ProviderKind = AIProviderKind.AzureOpenAI;
            options.Routing.HeavyModelEnabled = false;
            options.Routing.HeavyModelFallbackPolicy = HeavyModelFallbackPolicy.UseFastModel;
        });

        var route = router.Resolve(AITaskType.UserChatComplex, AIModelClass.HeavyReasoning, "complex");

        Assert.Equal(AIModelClass.Fast, route.ModelClass);
        Assert.True(route.IsFallback);
        Assert.Equal("heavy_model_disabled_fallback_to_fast", route.Reason);
    }

    [Fact]
    public void ModelRouter_FailFast_WhenHeavyDisabledAndPolicyIsFailFast()
    {
        var router = CreateRouter(options =>
        {
            options.UseMockProvider = false;
            options.ProviderKind = AIProviderKind.AzureOpenAI;
            options.Routing.HeavyModelEnabled = false;
            options.Routing.HeavyModelFallbackPolicy = HeavyModelFallbackPolicy.FailFast;
        });

        var route = router.Resolve(AITaskType.MerchantInvestigation, AIModelClass.HeavyReasoning, null);

        Assert.Equal("heavy_model_disabled_fail_fast", route.Reason);
        Assert.Equal(AIModelClass.HeavyReasoning, route.ModelClass);
    }

    [Fact]
    public void UserChatComplexityClassifier_DetectsComplexFinancialPrompt()
    {
        var classifier = new UserChatComplexityClassifier();
        var request = new UserChatRequest(
            "Please compare three debt payoff scenarios, rank them by risk-adjusted cashflow impact, and give me a step by step tradeoff analysis.",
            [],
            null,
            "corr-1");

        var result = classifier.Evaluate(request);

        Assert.Equal(UserChatComplexity.Complex, result.Complexity);
        Assert.Contains("financial_reasoning_intent", result.ReasonCodes);
        Assert.Contains("ranking_intent", result.ReasonCodes);
    }

    [Fact]
    public void ConversationContextService_TrimsGreetingsResolvedAndDuplicates()
    {
        var service = new ConversationContextService(
            Options.Create(new AIIntegrationOptions
            {
                Execution = new AIExecutionOptions
                {
                    MaxContextTurns = 3,
                    MaxSummaryEntries = 2
                }
            }),
            NullLogger<ConversationContextService>.Instance);

        var turns = new List<UserChatTurn>
        {
            new(AIMessageRole.User, "hello", DateTime.UtcNow.AddMinutes(-10)),
            new(AIMessageRole.Assistant, "Hi there", DateTime.UtcNow.AddMinutes(-9), IsResolved: true),
            new(AIMessageRole.User, "Need help with budget", DateTime.UtcNow.AddMinutes(-8)),
            new(AIMessageRole.User, "Need help with budget", DateTime.UtcNow.AddMinutes(-7)),
            new(AIMessageRole.Assistant, "Sure", DateTime.UtcNow.AddMinutes(-6)),
            new(AIMessageRole.User, "Compare two plans", DateTime.UtcNow.AddMinutes(-5))
        };

        var result = service.BuildContext(
            new ConversationContextBuildRequest(
                AITaskType.UserChatComplex,
                turns,
                new ConversationStateSnapshot(
                    ActiveTopic: "budgeting",
                    UserIntent: "reduce monthly overspend",
                    Constraints: new Dictionary<string, string> { ["currency"] = "EUR" },
                    Summaries: ["User already provided fixed cost list."],
                    BudgetPreference: "conservative",
                    LocationPreference: "IE",
                    MerchantInvestigationSubject: null,
                    RecentConclusions: ["Needs two scenario comparison"]),
                "Give me the best option",
                "corr-2"));

        Assert.NotEmpty(result.ContextMessages);
        Assert.True(result.IncludedTurns.Count <= 3);
        Assert.Contains("structured_state_injected", result.ReasonCodes);
        Assert.Contains("excluded_irrelevant_turns", result.ReasonCodes);
    }

    [Fact]
    public void MerchantInvestigationParser_RejectsMalformedJson()
    {
        var parser = new MerchantInvestigationResponseParser(NullLogger<MerchantInvestigationResponseParser>.Instance);
        var response = new AIResponse(
            Content: "{invalid-json}",
            StructuredPayloadJson: "{invalid-json}",
            FinishReason: "stop",
            Provider: "Mock",
            Model: "gpt-5-chat",
            Deployment: "merchant-investigation",
            InputTokenEstimate: 10,
            OutputTokenEstimate: 20,
            LatencyMs: 1,
            WasMocked: true,
            RawDiagnostics: null,
            Succeeded: true,
            FailureReason: null);

        var parsed = parser.TryParse(response, out var result, out var reasonCodes);

        Assert.False(parsed);
        Assert.False(result.Succeeded);
        Assert.Contains("invalid_json_payload", reasonCodes);
    }

    [Fact]
    public async Task MerchantInvestigationOrchestrator_WithMockStrongCandidate_ReturnsCandidate()
    {
        var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true",
            ["AI:UseMockProvider"] = "true",
            ["AI:ProviderKind"] = "Mock",
            ["AI:Routing:HeavyModelEnabled"] = "true",
            ["AI:Mock:DefaultMerchantScenario"] = "MerchantStrongCandidate"
        });

        var service = services.GetRequiredService<IMerchantInvestigationService>();
        var result = await service.InvestigateAsync(
            new MerchantInvestigationRequest("ACME STREAMING DUBLIN", "acme streaming dublin", "unit-test"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.InsufficientEvidence);
        Assert.NotEmpty(result.Candidates);
        Assert.Equal("Acme Streaming", result.Candidates[0].CanonicalName);
    }

    [Fact]
    public async Task UserChatOrchestrator_ReturnsStructuredResponse_FromMockProvider()
    {
        var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true",
            ["AI:UseMockProvider"] = "true",
            ["AI:ProviderKind"] = "Mock",
            ["AI:Routing:HeavyModelEnabled"] = "true",
            ["AI:Mock:DefaultSimpleChatScenario"] = "UserChatSimple",
            ["AI:Mock:DefaultComplexChatScenario"] = "UserChatComplex"
        });

        var orchestrator = services.GetRequiredService<IUserChatOrchestrator>();
        var response = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Compare debt payoff options and rank them.",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-chat-1"),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.NotEmpty(response.ReplyText);
        Assert.NotNull(response.ModelUsed);
    }

    [Fact]
    public async Task AIClient_UsesAzureProvider_WhenConfiguredAndMockDisabled()
    {
        var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true",
            ["AI:UseMockProvider"] = "false",
            ["AI:ProviderKind"] = "AzureOpenAI",
            ["AI:AzureOpenAI:Enabled"] = "false"
        });

        var client = services.GetRequiredService<IAIClient>();
        var response = await client.SendAsync(
            AIRequest.Create(
                AITaskType.UserChatSimple,
                AIModelClass.Fast,
                [AIMessage.User("hello")],
                "corr-azure-1"),
            new AIModelRoute(AITaskType.UserChatSimple, AIModelClass.Fast, "gpt-4.1", "gpt-4-1-chat", false, "fast_route", []),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("AzureOpenAI", response.Provider);
        Assert.False(response.WasMocked);
    }

    [Fact]
    public async Task AIClient_StaysMock_WhenMockEnabledEvenIfAzureConfigured()
    {
        var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true",
            ["AI:UseMockProvider"] = "true",
            ["AI:ProviderKind"] = "AzureOpenAI",
            ["AI:Mock:DefaultSimpleChatScenario"] = "UserChatSimple"
        });

        var client = services.GetRequiredService<IAIClient>();
        var response = await client.SendAsync(
            AIRequest.Create(
                AITaskType.UserChatSimple,
                AIModelClass.Fast,
                [AIMessage.User("hello")],
                "corr-mock-1"),
            new AIModelRoute(AITaskType.UserChatSimple, AIModelClass.Fast, "gpt-4.1", "gpt-4-1-chat", false, "fast_route", []),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.True(response.WasMocked);
        Assert.Equal("Mock", response.Provider);
    }

    private static AIModelRouter CreateRouter(Action<AIIntegrationOptions>? configure)
    {
        var options = new AIIntegrationOptions();
        configure?.Invoke(options);

        return new AIModelRouter(
            Options.Create(options),
            NullLogger<AIModelRouter>.Instance);
    }

    private static ServiceProvider BuildServiceProvider(IReadOnlyDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<MerchantDescriptorNormalizer>();
        services.AddAIIntegration(configuration);

        return services.BuildServiceProvider();
    }
}
