using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldIntentInterpreterTests
{
    private readonly LocalDiscoveryConstraintExtractor localDiscoveryExtractor = new();

    [Fact]
    public async Task InterpretAsync_FinancialPlanningWithProductMention_StaysFinancial()
    {
        var interpreter = CreateInterpreter();
        var message = "How can I save for an Xbox in 2 months?";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-fin-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldIntentFamily.FinancialGuidance, result.IntentFamily);
        Assert.Equal(RealWorldExecutionMode.FinancialGuidanceOnly, result.RecommendedExecutionMode);
        Assert.False(result.PlacesApplicable);
    }

    [Fact]
    public async Task InterpretAsync_CommerceLookup_DetectsCommerceAndPlacesApplicability()
    {
        var interpreter = CreateInterpreter();
        var message = "Where can I buy an Xbox near me?";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-com-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldIntentFamily.CommerceDiscovery, result.IntentFamily);
        Assert.True(result.PlacesApplicable);
        Assert.Contains(RealWorldDiscoveryDomain.ElectronicsRetail, result.CandidateDomains);
    }

    [Fact]
    public async Task InterpretAsync_ExploratoryPrompt_ChoosesExploratoryMode()
    {
        var interpreter = CreateInterpreter();
        var message = "What can I do later tonight?";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-exp-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldIntentFamily.ExploratoryAssistance, result.IntentFamily);
        Assert.Equal(RealWorldExecutionMode.ExploratoryMultiDomainSearch, result.RecommendedExecutionMode);
        Assert.True(result.Exploratory);
    }

    [Fact]
    public async Task InterpretAsync_ThemedFoodPrompt_ChoosesFocusedThemeMode()
    {
        var interpreter = CreateInterpreter();
        var message = "What should I eat tonight?";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-theme-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldExecutionMode.FocusedThemeSearch, result.RecommendedExecutionMode);
        Assert.Contains(RealWorldDiscoveryDomain.Restaurant, result.CandidateDomains);
    }

    private static RealWorldIntentInterpreter CreateInterpreter()
    {
        return new RealWorldIntentInterpreter(
            modelRouter: new FixedModelRouter(),
            aiClient: new NoopInterpreterAIClient(),
            promptBuilder: new RealWorldIntentInterpreterPromptBuilder(),
            logger: NullLogger<RealWorldIntentInterpreter>.Instance);
    }

    private sealed class FixedModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(
            AITaskType taskType,
            AIModelClass preferredModelClass,
            string? complexityHint = null)
        {
            return new AIModelRoute(
                taskType,
                preferredModelClass,
                "fast-model",
                "fast-deployment",
                false,
                "test",
                []);
        }
    }

    private sealed class NoopInterpreterAIClient : IAIClient
    {
        public Task<AIResponse> SendAsync(
            AIRequest request,
            AIModelRoute route,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                AIResponse.Failed(
                    provider: "mock",
                    model: route.Model,
                    deployment: route.Deployment,
                    failureReason: "disabled",
                    wasMocked: true));
        }
    }
}
