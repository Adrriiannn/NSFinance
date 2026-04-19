using System.Text.Json;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record RealWorldInterpreterPromptInput(
    string UserMessage,
    CompanionLocationGrounding Grounding,
    LocalDiscoveryConstraintExtractionResult LocalDiscovery,
    RealWorldIntentInterpretation DeterministicSeed);

public interface IRealWorldIntentInterpreterPromptBuilder
{
    string BuildSystemInstructions();

    string BuildUserPrompt(RealWorldInterpreterPromptInput input);
}

public sealed class RealWorldIntentInterpreterPromptBuilder : IRealWorldIntentInterpreterPromptBuilder
{
    public string BuildSystemInstructions()
    {
        return
            """
            You are the primary semantic intent interpreter for a bounded real-world assistant.
            Your task is interpretation only, not tool execution and not provider query generation.

            Rules:
            1) Return STRICT JSON only. No prose outside JSON.
            2) Infer user goal from full sentence meaning, not isolated keywords.
            3) Distinguish:
               - financial planning/guidance
               - place/service discovery
               - commerce/vendor discovery
               - exploratory "what can I do" assistance
            4) Product mentions alone do NOT imply vendor lookup.
               Example: "How can I save for an Xbox?" is financial guidance, not places.
            5) For exploratory prompts, recommend diversified domains.
            6) Use only allowed enums and controlled canonical concept vocabulary.
            7) Do not invent facts.
            """;
    }

    public string BuildUserPrompt(RealWorldInterpreterPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var payload = new
        {
            userMessage = Sanitize(input.UserMessage, 420),
            grounding = new
            {
                source = input.Grounding.Source,
                hasCoordinates = input.Grounding.HasCoordinates,
                hasTypedArea = input.Grounding.HasTypedArea,
                typedArea = Sanitize(input.Grounding.TypedArea, 80),
                localityLabel = Sanitize(input.Grounding.LocalityLabel, 80)
            },
            localDiscovery = new
            {
                input.LocalDiscovery.IsLocalDiscoveryCandidate,
                input.LocalDiscovery.Confidence,
                input.LocalDiscovery.HasNearMeLanguage,
                input.LocalDiscovery.HasExplicitLocality,
                localityHint = Sanitize(input.LocalDiscovery.LocalityHint, 80),
                placeTypeHints = input.LocalDiscovery.PlaceTypeHints,
                audienceHints = input.LocalDiscovery.AudienceHints,
                timeHints = input.LocalDiscovery.TimeHints,
                preferenceHints = input.LocalDiscovery.PreferenceHints,
                reasonCodes = input.LocalDiscovery.ReasonCodes
            },
            deterministicSeed = new
            {
                intentFamily = input.DeterministicSeed.IntentFamily.ToString(),
                executionMode = input.DeterministicSeed.RecommendedExecutionMode.ToString(),
                input.DeterministicSeed.PlacesApplicable,
                input.DeterministicSeed.FinancialRelated,
                input.DeterministicSeed.RequiresLocation,
                input.DeterministicSeed.Exploratory,
                input.DeterministicSeed.ClarificationNeeded,
                input.DeterministicSeed.Confidence,
                candidateDomains = input.DeterministicSeed.CandidateDomains
                    .Select(static x => x.ToString())
                    .ToArray(),
                candidateConcepts = input.DeterministicSeed.CandidateConcepts,
                reasonCodes = input.DeterministicSeed.ReasonCodes
            }
        };

        return
            $$"""
              Interpret this request for execution planning.

              Input JSON:
              {{JsonSerializer.Serialize(payload)}}

              Allowed intentFamily values:
              - FinancialGuidance
              - PlaceDiscovery
              - CommerceDiscovery
              - ServiceDiscovery
              - ExploratoryAssistance
              - MixedAssistance
              - Ambiguous

              Allowed executionMode values:
              - FocusedPlaceSearch
              - FocusedThemeSearch
              - ExploratoryMultiDomainSearch
              - FinancialGuidanceOnly
              - ClarifyLight
              - MissingLocationGuard
              - ProviderFailureFallback

              Allowed candidateDomains values:
              - Cafe
              - Restaurant
              - Takeaway
              - PubBar
              - MovieTheater
              - ParkWalk
              - Playground
              - Pharmacy
              - PetrolStation
              - Gym
              - ElectronicsRetail
              - ConvenienceStore
              - Grocery
              - ShoppingGeneral
              - OutdoorActivity
              - EntertainmentGeneral
              - NightlifeGeneral
              - FoodDrinkGeneral
              - ServiceGeneral
              - CommerceGeneral
              - ExploratoryEveningActivity
              - ExploratoryFamilyActivity

              Allowed candidateConcepts values (canonical snake_case):
              - cafe
              - restaurant
              - takeaway
              - pub_bar
              - movie_theater
              - park_walk
              - playground
              - pharmacy
              - petrol_station
              - gym
              - electronics_retail
              - convenience_store
              - grocery
              - shopping_general
              - outdoor_activity
              - entertainment_general
              - nightlife_general
              - food_drink_general
              - service_general
              - commerce_general
              - exploratory_evening_activity
              - exploratory_family_activity

              Return strict JSON with exactly:
              {
                "intentFamily": "...",
                "executionMode": "...",
                "placesApplicable": true,
                "financialRelated": false,
                "requiresLocation": true,
                "exploratory": false,
                "clarificationNeeded": false,
                "confidence": 0.0,
                "candidateDomains": ["..."],
                "candidateConcepts": ["..."],
                "clarificationPrompt": "string|null",
                "reasonCodes": ["..."]
              }
              """;
    }

    private static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value.Trim(), "\\s+", " ");
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd();
    }
}

public sealed class RealWorldIntentInterpreter : IRealWorldIntentInterpreter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAIModelRouter modelRouter;
    private readonly IAIClient aiClient;
    private readonly IRealWorldIntentInterpreterPromptBuilder promptBuilder;
    private readonly IRealWorldDeterministicFallbackBuilder deterministicFallbackBuilder;
    private readonly IRealWorldInterpretationValidationPolicy validationPolicy;
    private readonly ILogger<RealWorldIntentInterpreter> logger;

    public RealWorldIntentInterpreter(
        IAIModelRouter modelRouter,
        IAIClient aiClient,
        IRealWorldIntentInterpreterPromptBuilder promptBuilder,
        ILogger<RealWorldIntentInterpreter> logger)
        : this(
            modelRouter,
            aiClient,
            promptBuilder,
            new RealWorldDeterministicFallbackBuilder(),
            new RealWorldInterpretationValidationPolicy(new RealWorldFinancialGuardrailPolicy()),
            logger)
    {
    }

    public RealWorldIntentInterpreter(
        IAIModelRouter modelRouter,
        IAIClient aiClient,
        IRealWorldIntentInterpreterPromptBuilder promptBuilder,
        IRealWorldDeterministicFallbackBuilder deterministicFallbackBuilder,
        IRealWorldInterpretationValidationPolicy validationPolicy,
        ILogger<RealWorldIntentInterpreter> logger)
    {
        this.modelRouter = modelRouter ?? throw new ArgumentNullException(nameof(modelRouter));
        this.aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        this.promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        this.deterministicFallbackBuilder = deterministicFallbackBuilder
                                            ?? throw new ArgumentNullException(nameof(deterministicFallbackBuilder));
        this.validationPolicy = validationPolicy ?? throw new ArgumentNullException(nameof(validationPolicy));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RealWorldIntentInterpretation> InterpretAsync(
        UserChatRequest request,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var deterministicSeed = deterministicFallbackBuilder.BuildSeed(request.UserMessage, localDiscovery);
        var deterministicFallback = deterministicFallbackBuilder.BuildFallback(request.UserMessage, localDiscovery);
        var (aiInterpretation, aiFailureReasonCode) = await TryInterpretWithAiAsync(
            request,
            grounding,
            localDiscovery,
            deterministicSeed,
            cancellationToken);
        if (aiInterpretation is null)
        {
            var warnings = new HashSet<string>(deterministicFallback.Warnings, StringComparer.Ordinal)
            {
                "real_world_interpreter_deterministic_fallback_used"
            };
            var reasonCodes = new HashSet<string>(deterministicFallback.ReasonCodes, StringComparer.Ordinal)
            {
                "real_world_interpreter_deterministic_fallback_used"
            };
            if (!string.IsNullOrWhiteSpace(aiFailureReasonCode))
            {
                warnings.Add(aiFailureReasonCode!);
                reasonCodes.Add(aiFailureReasonCode!);
            }

            return deterministicFallback with
            {
                ReasonCodes = reasonCodes.ToArray(),
                Warnings = warnings.ToArray(),
                InterpretationSource = RealWorldInterpretationSource.DeterministicFallback
            };
        }

        var normalized = validationPolicy.ValidateAndNormalize(
            request.UserMessage,
            grounding,
            localDiscovery,
            aiInterpretation,
            deterministicFallback);
        var normalizedWarnings = new HashSet<string>(normalized.Warnings, StringComparer.Ordinal);
        var normalizedReasonCodes = new HashSet<string>(normalized.ReasonCodes, StringComparer.Ordinal)
        {
            "real_world_interpreter_ai_primary_used"
        };
        if (!string.IsNullOrWhiteSpace(aiFailureReasonCode))
        {
            normalizedWarnings.Add(aiFailureReasonCode!);
            normalizedReasonCodes.Add(aiFailureReasonCode!);
        }

        return normalized with
        {
            Warnings = normalizedWarnings.ToArray(),
            ReasonCodes = normalizedReasonCodes.ToArray(),
            InterpretationSource = RealWorldInterpretationSource.AiPrimary
        };
    }

    private async Task<(RealWorldIntentInterpretation? Interpretation, string? FailureReasonCode)>
        TryInterpretWithAiAsync(
            UserChatRequest request,
            CompanionLocationGrounding grounding,
            LocalDiscoveryConstraintExtractionResult localDiscovery,
            RealWorldIntentInterpretation deterministicSeed,
            CancellationToken cancellationToken)
    {
        try
        {
            var route = modelRouter.Resolve(
                AITaskType.UserChatSimple,
                AIModelClass.Fast,
                complexityHint: "real_world_intent_interpretation");
            var promptInput = new RealWorldInterpreterPromptInput(
                request.UserMessage,
                grounding,
                localDiscovery,
                deterministicSeed);
            var aiRequest = AIRequest.Create(
                taskType: AITaskType.UserChatSimple,
                preferredModelClass: route.ModelClass,
                messages:
                [
                    AIMessage.User(promptBuilder.BuildUserPrompt(promptInput))
                ],
                correlationId: request.CorrelationId,
                systemInstructions: promptBuilder.BuildSystemInstructions(),
                structuredOutputSchemaName: "real_world_intent_interpretation_v1",
                temperature: 0.1d,
                maxOutputTokens: 320,
                metadata: request.Metadata);

            var response = await aiClient.SendAsync(aiRequest, route, cancellationToken);
            if (!response.Succeeded)
            {
                logger.LogInformation(
                    "Real-world interpreter AI call failed correlationId={CorrelationId} reason={Reason}",
                    request.CorrelationId,
                    response.FailureReason ?? "unknown");
                return (null, "real_world_interpreter_ai_call_failed");
            }

            var payload = response.StructuredPayloadJson ?? response.Content;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return (null, "real_world_interpreter_invalid_ai_payload");
            }

            var parsed = JsonSerializer.Deserialize<RealWorldInterpreterAiResponse>(payload, JsonOptions);
            var normalized = NormalizeAiResponse(parsed);
            if (normalized is null)
            {
                return (null, "real_world_interpreter_invalid_ai_payload");
            }

            return (normalized, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "real_world_interpreter_ai_transient_cancellation");
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Real-world interpreter AI parsing failed correlationId={CorrelationId}",
                request.CorrelationId);
            return (null, "real_world_interpreter_invalid_ai_payload");
        }
    }

    private static RealWorldIntentInterpretation? NormalizeAiResponse(RealWorldInterpreterAiResponse? ai)
    {
        if (ai is null)
        {
            return null;
        }

        if (!TryParseIntentFamily(ai.IntentFamily, out var family)
            || !TryParseExecutionMode(ai.ExecutionMode, out var mode))
        {
            return null;
        }

        var domains = (ai.CandidateDomains ?? [])
            .Select(TryParseDomain)
            .Where(static domain => domain.HasValue)
            .Select(static domain => domain!.Value)
            .Distinct()
            .Take(8)
            .ToArray();
        var concepts = (ai.CandidateConcepts ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(RealWorldDeterministicFallbackBuilder.NormalizeConceptToken)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        var confidence = Math.Round(Math.Clamp(ai.Confidence ?? 0.5d, 0d, 0.98d), 4);

        return new RealWorldIntentInterpretation(
            IntentFamily: family,
            RecommendedExecutionMode: mode,
            PlacesApplicable: ai.PlacesApplicable ?? family is not RealWorldIntentFamily.FinancialGuidance,
            FinancialRelated: ai.FinancialRelated ?? family == RealWorldIntentFamily.FinancialGuidance,
            RequiresLocation: ai.RequiresLocation ?? false,
            Exploratory: ai.Exploratory ?? mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            ClarificationNeeded: ai.ClarificationNeeded ?? mode == RealWorldExecutionMode.ClarifyLight,
            HasNearMeLanguage: false,
            HasExplicitLocality: false,
            Confidence: confidence,
            CandidateDomains: domains,
            ClarificationPrompt: ai.ClarificationPrompt,
            ReasonCodes: (ai.ReasonCodes ?? [])
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Warnings: [])
        {
            CandidateConcepts = concepts,
            InterpretationSource = RealWorldInterpretationSource.AiPrimary
        };
    }

    private static bool TryParseIntentFamily(string? value, out RealWorldIntentFamily family)
    {
        return Enum.TryParse(value, true, out family);
    }

    private static bool TryParseExecutionMode(string? value, out RealWorldExecutionMode mode)
    {
        return Enum.TryParse(value, true, out mode);
    }

    private static RealWorldDiscoveryDomain? TryParseDomain(string? value)
    {
        if (Enum.TryParse<RealWorldDiscoveryDomain>(value, true, out var parsed))
        {
            return parsed;
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "movie_theater" => RealWorldDiscoveryDomain.MovieTheater,
            "pub_bar" => RealWorldDiscoveryDomain.PubBar,
            "petrol_station" => RealWorldDiscoveryDomain.PetrolStation,
            "food_drink_general" => RealWorldDiscoveryDomain.FoodDrinkGeneral,
            "service_general" => RealWorldDiscoveryDomain.ServiceGeneral,
            "commerce_general" => RealWorldDiscoveryDomain.CommerceGeneral,
            "park_walk" => RealWorldDiscoveryDomain.ParkWalk,
            _ => null
        };
    }

    private sealed record RealWorldInterpreterAiResponse(
        string? IntentFamily,
        string? ExecutionMode,
        bool? PlacesApplicable,
        bool? FinancialRelated,
        bool? RequiresLocation,
        bool? Exploratory,
        bool? ClarificationNeeded,
        double? Confidence,
        IReadOnlyList<string>? CandidateDomains,
        IReadOnlyList<string>? CandidateConcepts,
        string? ClarificationPrompt,
        IReadOnlyList<string>? ReasonCodes);
}
