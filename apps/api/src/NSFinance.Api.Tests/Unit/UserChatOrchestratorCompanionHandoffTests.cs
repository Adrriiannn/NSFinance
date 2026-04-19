using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class UserChatOrchestratorCompanionHandoffTests
{
    [Fact]
    public async Task ExecuteAsync_LocalPlacesWithGpsMetadata_UsesCompanionHandoff_WithoutGenericAI()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "1. Cafe One - open now\n2. Bistro Two - moderate pricing",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: ["IPlacesSearchService"],
                Warnings: ["places_response_built_from_grounded_candidates"],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "places_grounded_response",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Find coffee near me",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-1",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [CompanionLocationMetadataKeys.Source] = "gps",
                    [CompanionLocationMetadataKeys.Latitude] = "53.357123",
                    [CompanionLocationMetadataKeys.Longitude] = "-6.44789",
                    [CompanionLocationMetadataKeys.RadiusMeters] = "3000"
                },
                ClientRequestId: "req-1",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("Cafe One", response.ReplyText, StringComparison.Ordinal);
        Assert.Contains("chat_path_companion_local_places", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(1, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_LocalPlacesWithoutGrounding_ReturnsSafePrompt_WithoutProviderInvocation()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Any brunch places near me?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-2",
                Metadata: null,
                ClientRequestId: "req-2",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("location permission", response.ReplyText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nearby_location_missing", response.Warnings);
        Assert.Equal(0, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_NonPlacesPrompt_UsesGenericAIPath()
    {
        var companion = new TrackingCompanionService(
            new FinancialCompanionResponse(
                ReplyText: "unused",
                Intent: FinancialCompanionIntent.LocalPlacesOutings,
                ToolsUsed: [],
                Warnings: [],
                Succeeded: true,
                FailureReason: null,
                ModelUsed: "unused",
                InputTokens: 0,
                OutputTokens: 0));
        var aiClient = new TrackingAIClient();
        var sut = CreateSut(companion, aiClient);

        var response = await sut.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "How is my monthly budget doing?",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-3",
                Metadata: null,
                ClientRequestId: "req-3",
                UserId: Guid.NewGuid(),
                UsePersistentMemory: false),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Equal("generic assistant reply", response.ReplyText);
        Assert.Equal(1, aiClient.CallCount);
        Assert.Equal(0, companion.CallCount);
    }

    private static UserChatOrchestrator CreateSut(
        TrackingCompanionService companion,
        TrackingAIClient aiClient)
    {
        var handoff = new UserChatCompanionHandoffService(
            new CompanionIntentRouter(NullLogger<CompanionIntentRouter>.Instance),
            companion,
            NullLogger<UserChatCompanionHandoffService>.Instance);
        return new UserChatOrchestrator(
            complexityClassifier: new FixedComplexityClassifier(),
            contextService: new PassThroughContextService(),
            modelRouter: new FixedModelRouter(),
            promptBuilder: new FixedPromptBuilder(),
            responseParser: new FixedResponseParser(),
            aiClient: aiClient,
            logger: NullLogger<UserChatOrchestrator>.Instance,
            options: Options.Create(CreateOptions()),
            companionHandoffService: handoff);
    }

    private static AIIntegrationOptions CreateOptions()
    {
        return new AIIntegrationOptions
        {
            Routing = new AIModelRoutingOptions
            {
                FastModelName = "fast-model",
                FastDeploymentName = "fast-deployment",
                HeavyModelName = "heavy-model",
                HeavyDeploymentName = "heavy-deployment",
                HeavyModelEnabled = true
            }
        };
    }

    private sealed class FixedComplexityClassifier : IUserChatComplexityClassifier
    {
        public UserChatComplexityEvaluation Evaluate(UserChatRequest request)
        {
            return new UserChatComplexityEvaluation(
                Complexity: UserChatComplexity.Simple,
                ReasonCodes: ["test_simple"],
                ConstraintCount: 0,
                FinancialReasoningIntent: false,
                RankingIntent: false,
                MultiStepLanguageDetected: false);
        }
    }

    private sealed class PassThroughContextService : IConversationContextService
    {
        public ConversationContextBuildResult BuildContext(ConversationContextBuildRequest request)
        {
            return new ConversationContextBuildResult(
                ContextMessages:
                [
                    AIMessage.User(request.CurrentUserMessage ?? string.Empty)
                ],
                IncludedTurns: [],
                ExcludedTurns: [],
                ContextSummary: "test_context",
                StructuredState: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReasonCodes: ["context_pass_through"]);
        }
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
                preferredModelClass == AIModelClass.Any ? AIModelClass.Fast : preferredModelClass,
                Model: "fast-model",
                Deployment: "fast-deployment",
                IsFallback: false,
                Reason: "test_route",
                Notes: []);
        }
    }

    private sealed class FixedPromptBuilder : IPromptBuilder
    {
        public PromptBuildResult BuildMerchantInvestigationPrompt(MerchantInvestigationPromptInput input)
        {
            throw new NotSupportedException();
        }

        public PromptBuildResult BuildUserChatPrompt(UserChatPromptInput input)
        {
            return new PromptBuildResult(
                SystemInstructions: "test",
                Messages: [AIMessage.User(input.ChatRequest.UserMessage)],
                StructuredSchemaName: "user_chat_response_v1",
                ReasonCodes: ["prompt_built"]);
        }
    }

    private sealed class FixedResponseParser : IUserChatResponseParser
    {
        public bool TryParse(
            AIResponse response,
            AIModelRoute route,
            out UserChatResponse parsedResponse,
            out IReadOnlyList<string> reasonCodes)
        {
            parsedResponse = new UserChatResponse(
                ReplyText: "generic assistant reply",
                ModelUsed: route.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: null,
                Warnings: [],
                FollowUpIntentHints: [],
                Succeeded: true,
                FailureReason: null);
            reasonCodes = ["structured_parse_success"];
            return true;
        }
    }

    private sealed class TrackingAIClient : IAIClient
    {
        public int CallCount { get; private set; }

        public Task<AIResponse> SendAsync(
            AIRequest request,
            AIModelRoute route,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount += 1;
            return Task.FromResult(
                new AIResponse(
                    Content: "{\"replyText\":\"generic assistant reply\"}",
                    StructuredPayloadJson: "{\"replyText\":\"generic assistant reply\"}",
                    FinishReason: "stop",
                    Provider: "mock",
                    Model: route.Model,
                    Deployment: route.Deployment,
                    InputTokenEstimate: 20,
                    OutputTokenEstimate: 20,
                    LatencyMs: 2,
                    WasMocked: true,
                    RawDiagnostics: null,
                    Succeeded: true,
                    FailureReason: null));
        }
    }

    private sealed class TrackingCompanionService(
        FinancialCompanionResponse response) : IFinancialCompanionService
    {
        public int CallCount { get; private set; }

        public Task<FinancialCompanionResponse> ExecuteAsync(
            FinancialCompanionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount += 1;
            return Task.FromResult(response);
        }
    }
}
