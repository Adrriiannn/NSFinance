using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class UserChatCompanionHandoffConversationContextTests
{
    private static readonly AIModelRoute Route = new(
        TaskType: AITaskType.UserChatSimple,
        ModelClass: AIModelClass.Fast,
        Model: "fast-model",
        Deployment: "fast-deployment",
        IsFallback: false,
        Reason: "test",
        Notes: []);

    [Fact]
    public async Task TryExecuteAsync_ReusesConversationSearchContext_ForFollowUpRefinements()
    {
        var placesSearch = new TrackingPlacesSearchService();
        var handoff = CreateHandoff(placesSearch);
        var userId = Guid.NewGuid();

        var first = await handoff.TryExecuteAsync(
            new UserChatRequest(
                UserMessage: "what can i do later tonight?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ctx-1",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "1800"
                },
                ClientRequestId: "req-ctx-1",
                UserId: userId,
                UsePersistentMemory: false),
            Route,
            sessionId: "session-ctx-1",
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.True(first!.Succeeded);
        Assert.Contains("real_world_search_scope:device_location", first.Warnings);

        var second = await handoff.TryExecuteAsync(
            new UserChatRequest(
                UserMessage: "with kids",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ctx-2",
                Metadata: null,
                ClientRequestId: "req-ctx-2",
                UserId: userId,
                UsePersistentMemory: false),
            Route,
            sessionId: "session-ctx-1",
            CancellationToken.None);

        Assert.NotNull(second);
        Assert.True(second!.Succeeded);
        Assert.Contains("real_world_search_context_reused", second.Warnings);
        Assert.Contains("real_world_search_scope:device_location", second.Warnings);
        Assert.DoesNotContain("nearby_location_missing", second.Warnings);
        Assert.DoesNotContain("fallback_clarify_light", second.Warnings);
        Assert.True(placesSearch.CallCount >= 2);
    }

    private static UserChatCompanionHandoffService CreateHandoff(IPlacesSearchService placesSearchService)
    {
        var interpreter = new RealWorldIntentInterpreter(
            new FixedModelRouter(),
            new NoopInterpreterAIClient(),
            new RealWorldIntentInterpreterPromptBuilder(),
            NullLogger<RealWorldIntentInterpreter>.Instance);

        return new UserChatCompanionHandoffService(
            new LocalDiscoveryConstraintExtractor(),
            new RealWorldConversationSearchContextService(),
            new RealWorldSearchScopeResolver(),
            interpreter,
            new RealWorldExecutionModePlanner(new ExploratoryDomainSelectionPolicy()),
            new RealWorldPlacesExecutionService(
                placesSearchService,
                NullLogger<RealWorldPlacesExecutionService>.Instance),
            new RealWorldFailureMessageBuilder(),
            new NoopFinancialCompanionService(),
            NullLogger<UserChatCompanionHandoffService>.Instance);
    }

    private sealed class FixedModelRouter : IAIModelRouter
    {
        public AIModelRoute Resolve(
            AITaskType taskType,
            AIModelClass preferredModelClass,
            string? complexityHint = null)
        {
            return Route;
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

    private sealed class NoopFinancialCompanionService : IFinancialCompanionService
    {
        public Task<FinancialCompanionResponse> ExecuteAsync(
            FinancialCompanionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new FinancialCompanionResponse(
                    ReplyText: "fallback companion response",
                    Intent: FinancialCompanionIntent.LocalPlacesOutings,
                    ToolsUsed: [],
                    Warnings: [],
                    Succeeded: true,
                    FailureReason: null,
                    ModelUsed: "financial_companion",
                    InputTokens: 0,
                    OutputTokens: 0));
        }
    }

    private sealed class TrackingPlacesSearchService : IPlacesSearchService
    {
        public int CallCount { get; private set; }

        public Task<PlaceSearchResult> SearchAsync(
            string query,
            string country,
            PlaceSearchLocationContext? locationContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount += 1;
            return Task.FromResult(
                new PlaceSearchResult(
                [
                    new PlaceSearchItem(
                        PlaceId: $"place-{CallCount}",
                        Name: $"Place {CallCount}",
                        Category: "Entertainment",
                        PriceLevel: "PRICE_LEVEL_MODERATE",
                        PrimaryType: "movie_theater",
                        Types: ["movie_theater"],
                        ShortFormattedAddress: "Dublin")
                ],
                    Metadata: new PlaceSearchMetadata(
                        UseCase: "companion_discovery",
                        FromCache: false,
                        RequestedCandidateCount: 8,
                        ReturnedCandidateCount: 1,
                        FieldMaskVariant: "companion_discovery_v1",
                        Elapsed: TimeSpan.FromMilliseconds(10),
                        TimedOut: false,
                        ProviderErrorCode: null),
                    Warnings: []));
        }
    }
}
