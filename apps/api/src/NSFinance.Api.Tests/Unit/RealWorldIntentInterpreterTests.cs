using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class RealWorldIntentInterpreterTests
{
    private readonly LocalDiscoveryConstraintExtractor localDiscoveryExtractor = new();

    [Fact]
    public async Task InterpretAsync_AiPrimary_RecognizesMovieTheaterSemanticPhrasing()
    {
        var interpreter = CreateInterpreter(
            new ScriptedInterpreterAIClient(
                """
                {
                  "intentFamily":"PlaceDiscovery",
                  "executionMode":"FocusedPlaceSearch",
                  "placesApplicable":true,
                  "financialRelated":false,
                  "requiresLocation":true,
                  "exploratory":false,
                  "clarificationNeeded":false,
                  "confidence":0.84,
                  "candidateDomains":["EntertainmentGeneral"],
                  "candidateConcepts":["movie_theatre"],
                  "clarificationPrompt":null,
                  "reasonCodes":["ai_semantic_movie_theater"]
                }
                """));
        const string message = "somewhere to watch a film nearby";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-sem-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldInterpretationSource.AiPrimary, result.InterpretationSource);
        Assert.Equal(RealWorldIntentFamily.PlaceDiscovery, result.IntentFamily);
        Assert.Equal(RealWorldExecutionMode.FocusedPlaceSearch, result.RecommendedExecutionMode);
        Assert.Contains(RealWorldDiscoveryDomain.MovieTheater, result.CandidateDomains);
        Assert.Contains("movie_theater", result.CandidateConcepts);
        Assert.Contains("real_world_interpreter_ai_primary_used", result.ReasonCodes);
        Assert.Contains("real_world_interpreter_concept_normalized", result.ReasonCodes);
    }

    [Fact]
    public async Task InterpretAsync_AiPrimary_RecognizesCommerceDiscovery()
    {
        var interpreter = CreateInterpreter(
            new ScriptedInterpreterAIClient(
                """
                {
                  "intentFamily":"CommerceDiscovery",
                  "executionMode":"FocusedPlaceSearch",
                  "placesApplicable":true,
                  "financialRelated":false,
                  "requiresLocation":true,
                  "exploratory":false,
                  "clarificationNeeded":false,
                  "confidence":0.87,
                  "candidateDomains":["ElectronicsRetail","CommerceGeneral"],
                  "candidateConcepts":["electronics_retail"],
                  "clarificationPrompt":null,
                  "reasonCodes":["ai_commerce_discovery"]
                }
                """));
        const string message = "where can I buy an xbox near me";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-sem-2"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldInterpretationSource.AiPrimary, result.InterpretationSource);
        Assert.Equal(RealWorldIntentFamily.CommerceDiscovery, result.IntentFamily);
        Assert.True(result.PlacesApplicable);
        Assert.Contains(RealWorldDiscoveryDomain.ElectronicsRetail, result.CandidateDomains);
    }

    [Fact]
    public async Task InterpretAsync_AiPrimary_RecognizesExploratoryMultiDomainIntent()
    {
        var interpreter = CreateInterpreter(
            new ScriptedInterpreterAIClient(
                """
                {
                  "intentFamily":"ExploratoryAssistance",
                  "executionMode":"ExploratoryMultiDomainSearch",
                  "placesApplicable":true,
                  "financialRelated":false,
                  "requiresLocation":true,
                  "exploratory":true,
                  "clarificationNeeded":false,
                  "confidence":0.82,
                  "candidateDomains":["PubBar","MovieTheater","Restaurant","ParkWalk"],
                  "candidateConcepts":["exploratory_evening_activity"],
                  "clarificationPrompt":null,
                  "reasonCodes":["ai_exploratory_semantic"]
                }
                """));
        const string message = "what can I do later tonight";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-sem-3"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldInterpretationSource.AiPrimary, result.InterpretationSource);
        Assert.Equal(RealWorldIntentFamily.ExploratoryAssistance, result.IntentFamily);
        Assert.Equal(RealWorldExecutionMode.ExploratoryMultiDomainSearch, result.RecommendedExecutionMode);
        Assert.True(result.Exploratory);
        Assert.Contains("real_world_interpreter_ai_primary_used", result.ReasonCodes);
    }

    [Fact]
    public async Task InterpretAsync_FinancialGuardrail_OverridesAiMisrouteToPlaces()
    {
        var interpreter = CreateInterpreter(
            new ScriptedInterpreterAIClient(
                """
                {
                  "intentFamily":"CommerceDiscovery",
                  "executionMode":"FocusedPlaceSearch",
                  "placesApplicable":true,
                  "financialRelated":false,
                  "requiresLocation":true,
                  "exploratory":false,
                  "clarificationNeeded":false,
                  "confidence":0.79,
                  "candidateDomains":["ElectronicsRetail"],
                  "candidateConcepts":["electronics_retail"],
                  "clarificationPrompt":null,
                  "reasonCodes":["ai_wrongly_thinks_commerce"]
                }
                """));
        const string message = "How can I save for an Xbox in 2 months?";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-guardrail-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldIntentFamily.FinancialGuidance, result.IntentFamily);
        Assert.Equal(RealWorldExecutionMode.FinancialGuidanceOnly, result.RecommendedExecutionMode);
        Assert.False(result.PlacesApplicable);
        Assert.Contains("real_world_financial_guardrail_enforced", result.ReasonCodes);
    }

    [Fact]
    public async Task InterpretAsync_LowConfidenceAi_DegradesToClarifyLight()
    {
        var interpreter = CreateInterpreter(
            new ScriptedInterpreterAIClient(
                """
                {
                  "intentFamily":"PlaceDiscovery",
                  "executionMode":"FocusedPlaceSearch",
                  "placesApplicable":true,
                  "financialRelated":false,
                  "requiresLocation":true,
                  "exploratory":false,
                  "clarificationNeeded":false,
                  "confidence":0.22,
                  "candidateDomains":["Cafe"],
                  "candidateConcepts":["cafe"],
                  "clarificationPrompt":null,
                  "reasonCodes":["ai_low_confidence_guess"]
                }
                """));
        const string message = "help me find somewhere";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-lowconf-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldExecutionMode.ClarifyLight, result.RecommendedExecutionMode);
        Assert.True(result.ClarificationNeeded);
        Assert.False(result.PlacesApplicable);
        Assert.Contains("real_world_interpreter_low_confidence_clarify", result.ReasonCodes);
        Assert.Contains(RealWorldInterpreterFallbackReasonCodes.LowConfidence, result.ReasonCodes);
    }

    [Fact]
    public async Task InterpretAsync_InvalidAiPayload_UsesDeterministicFallback()
    {
        var interpreter = CreateInterpreter(
            new ScriptedInterpreterAIClient(
                """
                {
                  "intentFamily":"PlaceDiscovery"
                }
                """));
        const string message = "cinemas near me";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-invalid-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldInterpretationSource.DeterministicFallback, result.InterpretationSource);
        Assert.Contains("real_world_interpreter_deterministic_fallback_used", result.ReasonCodes);
        Assert.Contains(RealWorldInterpreterFallbackReasonCodes.InvalidPayload, result.ReasonCodes);
    }

    [Fact]
    public async Task InterpretAsync_AiUnavailable_UsesDeterministicFallback()
    {
        var interpreter = CreateInterpreter(new NoopInterpreterAIClient());
        const string message = "movie theatre near me";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-fallback-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldInterpretationSource.DeterministicFallback, result.InterpretationSource);
        Assert.Contains("real_world_interpreter_deterministic_fallback_used", result.ReasonCodes);
        Assert.Contains(RealWorldInterpreterFallbackReasonCodes.AiCallFailed, result.ReasonCodes);
    }

    [Fact]
    public async Task InterpretAsync_AiProviderUnavailable_UsesUnavailableFallbackReason()
    {
        var interpreter = CreateInterpreter(new UnavailableInterpreterAIClient());
        const string message = "cinemas near me";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-unavailable-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldInterpretationSource.DeterministicFallback, result.InterpretationSource);
        Assert.Contains(RealWorldInterpreterFallbackReasonCodes.AiUnavailable, result.ReasonCodes);
    }

    [Fact]
    public async Task InterpretAsync_UnknownConcepts_FlattensGracefullyWithoutDroppingPlacesIntent()
    {
        var interpreter = CreateInterpreter(
            new ScriptedInterpreterAIClient(
                """
                {
                  "intentFamily":"PlaceDiscovery",
                  "executionMode":"FocusedPlaceSearch",
                  "placesApplicable":true,
                  "financialRelated":false,
                  "requiresLocation":true,
                  "exploratory":false,
                  "clarificationNeeded":false,
                  "confidence":0.71,
                  "candidateDomains":["PlaceDiscovery"],
                  "candidateConcepts":["watch_movie_place","chill_evening_spot"],
                  "clarificationPrompt":null,
                  "reasonCodes":["ai_unknownish_concepts"]
                }
                """));
        const string message = "somewhere to watch a film nearby";

        var result = await interpreter.InterpretAsync(
            new UserChatRequest(
                UserMessage: message,
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-ai-unknown-concept-1"),
            CompanionLocationGroundingParser.Parse(null, null),
            localDiscoveryExtractor.Extract(message),
            CancellationToken.None);

        Assert.Equal(RealWorldInterpretationSource.AiPrimary, result.InterpretationSource);
        Assert.Contains("movie_theater", result.CandidateConcepts);
        Assert.Contains(RealWorldDiscoveryDomain.MovieTheater, result.CandidateDomains);
        Assert.Contains("real_world_interpreter_concept_normalized", result.ReasonCodes);
    }

    private static RealWorldIntentInterpreter CreateInterpreter(IAIClient aiClient)
    {
        return new RealWorldIntentInterpreter(
            modelRouter: new FixedModelRouter(),
            aiClient: aiClient,
            promptBuilder: new RealWorldIntentInterpreterPromptBuilder(),
            deterministicFallbackBuilder: new RealWorldDeterministicFallbackBuilder(),
            validationPolicy: new RealWorldInterpretationValidationPolicy(new RealWorldFinancialGuardrailPolicy()),
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

    private sealed class ScriptedInterpreterAIClient(string payload) : IAIClient
    {
        public Task<AIResponse> SendAsync(
            AIRequest request,
            AIModelRoute route,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new AIResponse(
                    Content: payload,
                    StructuredPayloadJson: payload,
                    FinishReason: "stop",
                    Provider: "mock",
                    Model: route.Model,
                    Deployment: route.Deployment,
                    InputTokenEstimate: 30,
                    OutputTokenEstimate: 50,
                    LatencyMs: 3,
                    WasMocked: true,
                    RawDiagnostics: null,
                    Succeeded: true,
                    FailureReason: null));
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

    private sealed class UnavailableInterpreterAIClient : IAIClient
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
                    failureReason: "provider_unavailable",
                    wasMocked: true));
        }
    }
}
