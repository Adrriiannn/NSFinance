using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

internal sealed class MockAIProviderTransport(
    IOptions<AIIntegrationOptions> options,
    ILogger<MockAIProviderTransport> logger) : IAIProviderTransport
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public AIProviderKind Kind => AIProviderKind.Mock;

    public Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scenario = ResolveScenario(request, options.Value.Mock);
        var payload = request.StructuredOutputSchemaName switch
        {
            "conversation_turn_strategy_decision_v1" => BuildConversationDecisionResponse(request),
            "exploration_subtype_decision_v1" => BuildExplorationSubtypeDecisionResponse(request),
            "conversation_intelligence_v1" => BuildConversationIntelligenceResponse(request),
            "turn_interpretation_v2" => BuildTurnInterpretationResponse(request),
            "response_composition_output_v1" => BuildResponseCompositionResponse(request),
            _ => scenario switch
            {
                MockAIScenario.MerchantStrongCandidate => BuildMerchantStrongCandidate(request),
                MockAIScenario.MerchantAmbiguousCandidates => BuildMerchantAmbiguousCandidates(request),
                MockAIScenario.MerchantInsufficientEvidence => BuildMerchantInsufficientEvidence(request),
                MockAIScenario.MerchantConflictingCandidates => BuildMerchantConflictingCandidates(request),
                MockAIScenario.MerchantMalformedOutput => "{\"summary\":{\"overallConfidence\":0.8,\"recommendation\":\"accept_candidate\"}",
                MockAIScenario.MerchantDangerousAliasProposal => BuildMerchantDangerousAliasProposal(request),
                MockAIScenario.MerchantNarrowUseWeakOfficial => BuildMerchantNarrowUseWeakOfficial(request),
                MockAIScenario.MerchantIntermediaryMarketplace => BuildMerchantIntermediaryMarketplace(request),
                MockAIScenario.UserChatComplex => BuildComplexChatResponse(),
                _ => BuildSimpleChatResponse()
            }
        };

        logger.LogDebug(
            "Mock AI provider executed scenario={Scenario} task={TaskType} correlationId={CorrelationId}",
            scenario,
            request.TaskType,
            request.CorrelationId);

        return Task.FromResult(new AIResponse(
            Content: payload,
            StructuredPayloadJson: payload,
            FinishReason: "stop",
            Provider: AIProviderKind.Mock.ToString(),
            Model: route.Model,
            Deployment: route.Deployment,
            InputTokenEstimate: Math.Max(1, payload.Length / 5),
            OutputTokenEstimate: Math.Max(1, payload.Length / 6),
            LatencyMs: 3,
            WasMocked: true,
            RawDiagnostics: $"scenario={scenario}",
            Succeeded: true,
            FailureReason: null));
    }

    private static MockAIScenario ResolveScenario(AIRequest request, MockAIProviderOptions options)
    {
        if (request.Metadata.TryGetValue("mockScenario", out var configured)
            && Enum.TryParse<MockAIScenario>(configured, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return request.TaskType switch
        {
            AITaskType.MerchantInvestigation => options.DefaultMerchantScenario,
            AITaskType.UserChatComplex => options.DefaultComplexChatScenario,
            _ => options.DefaultSimpleChatScenario
        };
    }

    private static string BuildMerchantStrongCandidate(AIRequest request)
    {
        var descriptor = ResolveDescriptor(request);

        var response = new MerchantInvestigationStructuredResponse(
            OverallConfidence: 0.94d,
            AmbiguityLevel: 0.08d,
            Recommendation: MerchantInvestigationContract.RecommendationAcceptCandidate,
            Summary: "Strong narrow-use merchant signature match with low ambiguity.",
            Candidates:
            [
                BuildCandidate(
                    canonicalName: "Netflix",
                    displayName: "Netflix",
                    merchantType: MerchantType.Merchant,
                    usageType: MerchantUsageType.NarrowUse,
                    confidence: 0.95d,
                    descriptorStrength: 0.96d,
                    entityStrength: 0.92d,
                    mixedUseRisk: false,
                    whyMatch: $"Descriptor {descriptor} aligns with known subscription signature.",
                    whyWrong: "Could be a reseller descriptor in rare cases.",
                    website: "https://www.netflix.com",
                    countryCode: "IE",
                    likelyFamilies: ["Subscriptions", "Streaming"])
            ],
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestionPayload("NETFLIX.COM", "BillingDescriptor", 0.95d, "Exact descriptor token pattern", true),
                new MerchantInvestigationAliasSuggestionPayload("NETFLIX", "MerchantName", 0.90d, "Canonical short name", false)
            ],
            Evidence:
            [
                BuildEvidence(
                    MerchantEvidenceType.OfficialSource,
                    sourceClass: "official_website",
                    summary: "Descriptor and official billing naming pattern align.",
                    confidence: 0.92d,
                    relevance: 0.92d,
                    sourceReference: "https://help.netflix.com/en/node/13444")
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantAmbiguousCandidates(AIRequest request)
    {
        var descriptor = ResolveDescriptor(request);

        var response = new MerchantInvestigationStructuredResponse(
            OverallConfidence: 0.63d,
            AmbiguityLevel: 0.47d,
            Recommendation: MerchantInvestigationContract.RecommendationUnresolved,
            Summary: "Descriptor likely belongs to a mixed-use merchant family with ambiguous entity mapping.",
            Candidates:
            [
                BuildCandidate(
                    canonicalName: "Google Services",
                    displayName: "Google Services",
                    merchantType: MerchantType.Merchant,
                    usageType: MerchantUsageType.MixedUse,
                    confidence: 0.68d,
                    descriptorStrength: 0.71d,
                    entityStrength: 0.63d,
                    mixedUseRisk: true,
                    whyMatch: descriptor,
                    whyWrong: "Could refer to multiple Google product lines.",
                    website: "https://payments.google.com",
                    countryCode: "US",
                    likelyFamilies: ["Subscriptions", "Apps", "Ads"]),
                BuildCandidate(
                    canonicalName: "YouTube",
                    displayName: "YouTube",
                    merchantType: MerchantType.Merchant,
                    usageType: MerchantUsageType.MixedUse,
                    confidence: 0.65d,
                    descriptorStrength: 0.66d,
                    entityStrength: 0.62d,
                    mixedUseRisk: true,
                    whyMatch: "YouTube billing can include Google descriptors.",
                    whyWrong: "Descriptor does not conclusively identify YouTube.",
                    website: "https://www.youtube.com",
                    countryCode: "US",
                    likelyFamilies: ["Streaming", "Apps"])
            ],
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestionPayload("GOOGLE", "MerchantName", 0.58d, "Too broad for direct linking", false)
            ],
            Evidence:
            [
                BuildEvidence(
                    MerchantEvidenceType.TransactionObservation,
                    sourceClass: "descriptor_observation",
                    summary: "Observed broad family descriptor without specific product token.",
                    confidence: 0.57d,
                    relevance: 0.75d,
                    sourceReference: null)
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantInsufficientEvidence(AIRequest request)
    {
        var descriptor = ResolveDescriptor(request);

        var response = new MerchantInvestigationStructuredResponse(
            OverallConfidence: 0.24d,
            AmbiguityLevel: 0.10d,
            Recommendation: MerchantInvestigationContract.RecommendationInsufficientEvidence,
            Summary: "Insufficient evidence to map descriptor to a trusted merchant.",
            Candidates: [],
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestionPayload(descriptor.ToUpperInvariant(), "BillingDescriptor", 0.30d, "Observed descriptor only", false)
            ],
            Evidence: []);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantConflictingCandidates(AIRequest request)
    {
        var descriptor = ResolveDescriptor(request);

        var response = new MerchantInvestigationStructuredResponse(
            OverallConfidence: 0.71d,
            AmbiguityLevel: 0.52d,
            Recommendation: MerchantInvestigationContract.RecommendationConflictingCandidates,
            Summary: "Two strong candidates conflict without sufficient dominance gap.",
            Candidates:
            [
                BuildCandidate(
                    canonicalName: "Acme Cloud",
                    displayName: "Acme Cloud",
                    merchantType: MerchantType.Merchant,
                    usageType: MerchantUsageType.MixedUse,
                    confidence: 0.74d,
                    descriptorStrength: 0.78d,
                    entityStrength: 0.72d,
                    mixedUseRisk: true,
                    whyMatch: descriptor,
                    whyWrong: "Could be intermediary processing string.",
                    website: null,
                    countryCode: "US",
                    likelyFamilies: ["SaaS"]),
                BuildCandidate(
                    canonicalName: "Acme Payments",
                    displayName: "Acme Payments",
                    merchantType: MerchantType.Intermediary,
                    usageType: MerchantUsageType.Intermediary,
                    confidence: 0.73d,
                    descriptorStrength: 0.76d,
                    entityStrength: 0.70d,
                    mixedUseRisk: true,
                    whyMatch: "Descriptor resembles known intermediary format.",
                    whyWrong: "No strong endpoint-specific marker.",
                    website: null,
                    countryCode: "US",
                    likelyFamilies: ["Intermediary"])
            ],
            AliasSuggestions: [],
            Evidence:
            [
                BuildEvidence(
                    MerchantEvidenceType.Deterministic,
                    sourceClass: "ambiguity_analysis",
                    summary: "Candidate confidence gap below dominance threshold.",
                    confidence: 0.70d,
                    relevance: 0.80d,
                    sourceReference: null)
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantDangerousAliasProposal(AIRequest request)
    {
        var descriptor = ResolveDescriptor(request);

        var response = new MerchantInvestigationStructuredResponse(
            OverallConfidence: 0.84d,
            AmbiguityLevel: 0.26d,
            Recommendation: MerchantInvestigationContract.RecommendationAcceptCautiously,
            Summary: "Likely merchant match exists but includes broad risky alias suggestions.",
            Candidates:
            [
                BuildCandidate(
                    canonicalName: "Amazon Prime",
                    displayName: "Amazon Prime",
                    merchantType: MerchantType.Merchant,
                    usageType: MerchantUsageType.MixedUse,
                    confidence: 0.84d,
                    descriptorStrength: 0.87d,
                    entityStrength: 0.81d,
                    mixedUseRisk: true,
                    whyMatch: descriptor,
                    whyWrong: "Amazon family descriptors are mixed-use and broad.",
                    website: "https://www.amazon.com/prime",
                    countryCode: "US",
                    likelyFamilies: ["Subscriptions", "Retail"],
                    aliasSuggestions:
                    [
                        new MerchantInvestigationAliasSuggestionPayload("AMAZON", "MerchantName", 0.91d, "Broad family alias", true),
                        new MerchantInvestigationAliasSuggestionPayload("AMZN", "Abbreviation", 0.76d, "Potentially broad", false)
                    ])
            ],
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestionPayload("AMAZON", "MerchantName", 0.91d, "Broad family alias", true)
            ],
            Evidence:
            [
                BuildEvidence(
                    MerchantEvidenceType.TransactionObservation,
                    sourceClass: "descriptor_observation",
                    summary: "Prime-specific token appears, but broad family alias remains risky.",
                    confidence: 0.81d,
                    relevance: 0.84d,
                    sourceReference: null)
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantNarrowUseWeakOfficial(AIRequest request)
    {
        var descriptor = ResolveDescriptor(request);

        var response = new MerchantInvestigationStructuredResponse(
            OverallConfidence: 0.79d,
            AmbiguityLevel: 0.18d,
            Recommendation: MerchantInvestigationContract.RecommendationAcceptCautiously,
            Summary: "Coherent narrow-use descriptor pattern with weaker official-source signals.",
            Candidates:
            [
                BuildCandidate(
                    canonicalName: "Acme Water Utility",
                    displayName: "Acme Water Utility",
                    merchantType: MerchantType.Utility,
                    usageType: MerchantUsageType.NarrowUse,
                    confidence: 0.80d,
                    descriptorStrength: 0.83d,
                    entityStrength: 0.75d,
                    mixedUseRisk: false,
                    whyMatch: descriptor,
                    whyWrong: "Official site confidence is moderate.",
                    website: null,
                    countryCode: "IE",
                    likelyFamilies: ["Utilities"])
            ],
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestionPayload("ACME WATER DD", "BillingDescriptor", 0.82d, "Observed recurring descriptor", true)
            ],
            Evidence:
            [
                BuildEvidence(
                    MerchantEvidenceType.TransactionObservation,
                    sourceClass: "historical_pattern",
                    summary: "Recurring direct debit pattern observed across multiple months.",
                    confidence: 0.78d,
                    relevance: 0.86d,
                    sourceReference: null)
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantIntermediaryMarketplace(AIRequest request)
    {
        var descriptor = ResolveDescriptor(request);

        var response = new MerchantInvestigationStructuredResponse(
            OverallConfidence: 0.72d,
            AmbiguityLevel: 0.33d,
            Recommendation: MerchantInvestigationContract.RecommendationUnresolved,
            Summary: "Likely intermediary or marketplace descriptor requiring conservative handling.",
            Candidates:
            [
                BuildCandidate(
                    canonicalName: "PayPal",
                    displayName: "PayPal",
                    merchantType: MerchantType.Intermediary,
                    usageType: MerchantUsageType.Intermediary,
                    confidence: 0.74d,
                    descriptorStrength: 0.76d,
                    entityStrength: 0.72d,
                    mixedUseRisk: true,
                    whyMatch: descriptor,
                    whyWrong: "Underlying merchant remains unknown from descriptor alone.",
                    website: "https://www.paypal.com",
                    countryCode: "US",
                    likelyFamilies: ["Intermediary", "Marketplace"])
            ],
            AliasSuggestions: [],
            Evidence:
            [
                BuildEvidence(
                    MerchantEvidenceType.TransactionObservation,
                    sourceClass: "intermediary_pattern",
                    summary: "Descriptor indicates payment intermediary usage.",
                    confidence: 0.74d,
                    relevance: 0.88d,
                    sourceReference: null)
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static MerchantInvestigationStructuredCandidate BuildCandidate(
        string canonicalName,
        string displayName,
        MerchantType merchantType,
        MerchantUsageType usageType,
        double confidence,
        double descriptorStrength,
        double entityStrength,
        bool mixedUseRisk,
        string whyMatch,
        string whyWrong,
        string? website,
        string countryCode,
        IReadOnlyList<string> likelyFamilies,
        IReadOnlyList<MerchantInvestigationAliasSuggestionPayload>? aliasSuggestions = null)
    {
        return new MerchantInvestigationStructuredCandidate(
            CanonicalName: canonicalName,
            DisplayName: displayName,
            LikelyOfficialWebsite: website,
            ParentBrand: null,
            MerchantType: merchantType,
            MerchantUsageType: usageType,
            BusinessSummary: string.Empty,
            SupportsSubscriptions: true,
            SupportsRecurringPayments: true,
            SupportsOneTimePurchases: usageType != MerchantUsageType.NarrowUse,
            SupportsMarketplacePayments: usageType is MerchantUsageType.MixedUse or MerchantUsageType.Intermediary,
            SupportsInAppPurchases: usageType != MerchantUsageType.Intermediary,
            LikelyCategoryFamilies: likelyFamilies,
            Confidence: confidence,
            DescriptorMatchStrength: descriptorStrength,
            EntityMatchStrength: entityStrength,
            MixedUseRisk: mixedUseRisk,
            HasContradictions: false,
            WhyItMayMatch: whyMatch,
            WhyItMayBeWrong: whyWrong,
            PrimaryCountryCode: countryCode,
            AliasCandidates: [canonicalName.ToUpperInvariant()],
            AliasSuggestions: aliasSuggestions,
            EvidenceItems: null);
    }

    private static MerchantInvestigationStructuredEvidence BuildEvidence(
        MerchantEvidenceType evidenceType,
        string sourceClass,
        string summary,
        double confidence,
        double relevance,
        string? sourceReference,
        MerchantSourceTrustLevel? sourceTrustLevel = null)
    {
        return new MerchantInvestigationStructuredEvidence(
            EvidenceType: evidenceType,
            SourceClass: sourceClass,
            Summary: summary,
            Confidence: confidence,
            Relevance: relevance,
            SourceReference: sourceReference,
            SourceTrustLevel: sourceTrustLevel);
    }

    private static string ResolveDescriptor(AIRequest request)
    {
        if (request.Metadata.TryGetValue("normalizedDescriptor", out var normalized)
            && !string.IsNullOrWhiteSpace(normalized))
        {
            return normalized.Trim();
        }

        return "unknown descriptor";
    }

    private static string BuildSimpleChatResponse()
    {
        var response = new UserChatStructuredResponse(
            ReplyText: "I can help with that. Tell me your goal and constraints, and I'll suggest a focused next step.",
            ReferencedContextSummary: "Simple guidance response from mock provider.",
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["last_intent"] = "simple_guidance"
            },
            Warnings: [],
            FollowUpIntentHints: ["clarify_goal", "set_constraints"]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildComplexChatResponse()
    {
        var response = new UserChatStructuredResponse(
            ReplyText: "Here is a structured plan: 1) define your monthly target, 2) map fixed vs variable spending, 3) test two reduction scenarios and compare tradeoffs.",
            ReferencedContextSummary: "Complex-response path with multi-step guidance.",
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["analysis_mode"] = "scenario_planning",
                ["next_action"] = "collect_monthly_spend_breakdown"
            },
            Warnings: ["Mock response: validate with live model before production decisions."],
            FollowUpIntentHints: ["scenario_compare", "budget_refinement"]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildConversationDecisionResponse(AIRequest request)
    {
        var userMessage = ExtractLatestUserMessage(request).ToLowerInvariant();

        if (ContainsAny(userMessage, "restaurant", "restaurants", "cafe", "cafes", "park", "parks", "museum", "museums")
            && ContainsAny(userMessage, "near me", "nearby", "open now", "in ", "around "))
        {
            return Serialize(new
            {
                strategy = "ToolReadyHandoff",
                modeCandidate = "Exploration",
                readiness = new { from = "R1_Vague", to = "R4_ToolReady" },
                confidence = 0.89,
                followUpBindingType = "None",
                clarificationQuestion = (string?)null,
                suggestedOptions = Array.Empty<string>(),
                toolExecutionPermission = "EligibleIfGuardPasses",
                reasonCodes = new[] { "mock_structured_exploration_ready" }
            });
        }

        if (ContainsAny(userMessage, "quiet", "safe", "view", "walk", "beach"))
        {
            return Serialize(new
            {
                strategy = "GeneralGuidance",
                modeCandidate = "Exploration",
                readiness = new { from = "R1_Vague", to = "R2_DirectionKnown" },
                confidence = 0.82,
                followUpBindingType = "None",
                clarificationQuestion = (string?)null,
                suggestedOptions = new[] { "Ask for a shortlist", "Add an area", "Keep it exploratory" },
                toolExecutionPermission = "Forbidden",
                reasonCodes = new[] { "mock_open_exploration" }
            });
        }

        if (ContainsAny(userMessage, "budget", "spending", "subscriptions", "afford", "expense", "save"))
        {
            return Serialize(new
            {
                strategy = "SuggestAndClarify",
                modeCandidate = "Conversation",
                readiness = new { from = "R0_Unknown", to = "R1_Vague" },
                confidence = 0.80,
                followUpBindingType = "None",
                clarificationQuestion = (string?)null,
                suggestedOptions = Array.Empty<string>(),
                toolExecutionPermission = "Forbidden",
                reasonCodes = new[] { "mock_financial_validation" }
            });
        }

        if (StartsWithAny(userMessage, "what ", "when ", "where ", "who ", "how "))
        {
            return Serialize(new
            {
                strategy = "DirectAnswer",
                modeCandidate = "GeneralKnowledge",
                readiness = new { from = "R0_Unknown", to = "R1_Vague" },
                confidence = 0.76,
                followUpBindingType = "None",
                clarificationQuestion = (string?)null,
                suggestedOptions = Array.Empty<string>(),
                toolExecutionPermission = "Forbidden",
                reasonCodes = new[] { "mock_general_knowledge" }
            });
        }

        return Serialize(new
        {
            strategy = "SuggestAndClarify",
            modeCandidate = "Conversation",
            readiness = new { from = "R0_Unknown", to = "R1_Vague" },
            confidence = 0.60,
            followUpBindingType = "None",
            clarificationQuestion = "What would be most useful to narrow down first?",
            suggestedOptions = new[] { "Clarify the goal", "Share a constraint", "Ask for general guidance" },
            toolExecutionPermission = "Forbidden",
            reasonCodes = new[] { "mock_default_conversation" }
        });
    }

    private static string BuildExplorationSubtypeDecisionResponse(AIRequest request)
    {
        var userMessage = ExtractLatestUserMessage(request).ToLowerInvariant();
        var isStructured = ContainsAny(userMessage, "restaurant", "restaurants", "cafe", "cafes", "park", "parks", "museum", "museums")
                           && ContainsAny(userMessage, "near me", "nearby", "open now", "in ", "around ");

        if (isStructured)
        {
            return Serialize(new
            {
                subtype = "Structured",
                confidence = 0.87,
                toolPathEligible = true,
                primaryWhy = "The request is a concrete place search with actionable filters.",
                missingConstraints = Array.Empty<string>(),
                reasonCodes = new[] { "mock_structured_subtype" }
            });
        }

        return Serialize(new
        {
            subtype = "Open",
            confidence = 0.83,
            toolPathEligible = false,
            primaryWhy = "The request is atmospheric or experiential rather than a concrete place search.",
            missingConstraints = new[] { "location_or_area" },
            reasonCodes = new[] { "mock_open_subtype" }
        });
    }

    private static string BuildConversationIntelligenceResponse(AIRequest request)
    {
        var userMessage = ExtractLatestUserMessage(request).ToLowerInvariant();
        var promptText = string.Join(
            "\n",
            request.Messages.Select(message => message.Content));
        var hasPriorResults = promptText.Contains("Active result context JSON", StringComparison.OrdinalIgnoreCase)
                              || promptText.Contains("Result context JSON", StringComparison.OrdinalIgnoreCase);
        var isClosing = userMessage is "thanks" or "thank you" or "that's all" or "thats all" or "never mind" or "stop";
        var isFrustrated = ContainsAny(userMessage, "useless", "why are you asking", "already said", "that's not what i asked", "thats not what i asked");
        var isCorrection = ContainsAny(userMessage, "i said", "i meant", "not ", "you're wrong", "you are wrong");
        var isClosest = ContainsAny(userMessage, "closest", "nearest");
        var isParking = ContainsAny(userMessage, "parking", "car park", "car parks");
        var isFollowUp = hasPriorResults && (isClosest || isParking || ContainsAny(userMessage, "open now", "which one", "just the"));
        var action = isClosing
            ? "answer_directly"
            : isClosest
                ? "sort_previous_results"
                : isParking && hasPriorResults
                    ? "filter_previous_results"
                    : isParking
                        ? "execute_search"
                        : isFollowUp
                            ? "filter_previous_results"
                            : ContainsAny(userMessage, "near me", "in ", "around ", "restaurants", "coffee", "starbucks", "museums", "parks", "beaches")
                                ? "execute_search"
                                : "answer_directly";

        return Serialize(new
        {
            conversation_phase = isClosing
                ? "closing"
                : isFrustrated
                    ? "frustration"
                    : isCorrection
                        ? "correction"
                        : isFollowUp
                            ? "refinement"
                            : "start",
            user_emotional_state = isFrustrated ? "frustrated" : isClosing ? "satisfied" : "neutral",
            user_intent_confidence = 0.88,
            should_continue_task = !isClosing && (isFollowUp || action != "answer_directly"),
            should_clarify = false,
            should_execute_tool = action is "execute_search" or "filter_previous_results" or "sort_previous_results" or "enrich_details",
            should_acknowledge_issue = isFrustrated || isCorrection,
            response_style = new
            {
                tone = isFrustrated || isCorrection ? "apologetic" : isClosing ? "warm" : "helpful",
                verbosity = isClosing ? "short" : "medium",
                avoid_repetition = true
            },
            task_state = new
            {
                is_new_task = !isFollowUp && !isClosing,
                is_follow_up = isFollowUp,
                is_refinement = isFollowUp,
                is_user_correction = isCorrection,
                target_previous_results = isFollowUp
            },
            next_action = new
            {
                type = action,
                reason = isParking
                    ? "The user is asking about parking in the current place task."
                    : isClosest
                        ? "The user wants the previous or current results ranked by distance."
                        : "The latest turn can be handled from the current conversation context.",
                target = isFollowUp ? "active_result_set" : null,
                requirement = isParking ? "parking" : isClosest ? "closest" : null
            },
            reason_codes = new[] { "mock_conversation_intelligence_v1" }
        });
    }

    private static string BuildResponseCompositionResponse(AIRequest request)
    {
        var compositionRequest = ExtractResponseCompositionRequest(request);
        var suggestedUpdates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (compositionRequest is null)
        {
            return JsonSerializer.Serialize(
                new UserChatStructuredResponse(
                    ReplyText: "I can help from here. Tell me which detail you want to refine next.",
                    ReferencedContextSummary: "Mock response composition fallback.",
                    SuggestedStructuredStateUpdates: suggestedUpdates,
                    Warnings: [],
                    FollowUpIntentHints: ["clarify_intent"]),
                SerializerOptions);
        }

        suggestedUpdates["mode"] = compositionRequest.Mode.ToString();
        suggestedUpdates["readiness"] = compositionRequest.ReadinessLevel.ToString();

        var replyText = compositionRequest.ResponseType switch
        {
            ResponseCompositionType.Clarify => BuildClarificationReply(compositionRequest),
            ResponseCompositionType.Placeholder => "I can help with that, and the next step is to choose which angle you want to focus on first.",
            ResponseCompositionType.ResultSummary => BuildResultSummaryReply(compositionRequest),
            ResponseCompositionType.Direct => "Here is the concise answer based on what you asked.",
            ResponseCompositionType.Suggest => BuildSuggestionReply(compositionRequest),
            _ => "I can still help from here without taking action yet."
        };

        var response = new UserChatStructuredResponse(
            ReplyText: replyText,
            ReferencedContextSummary: "Mock response composition output.",
            SuggestedStructuredStateUpdates: suggestedUpdates,
            Warnings: compositionRequest.GroundedData.Warnings,
            FollowUpIntentHints: compositionRequest.MissingConstraints.Count > 0
                ? ["clarify_intent"]
                : ["continue_conversation"]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildTurnInterpretationResponse(AIRequest request)
    {
        var userMessage = ExtractLatestUserMessage(request).ToLowerInvariant();
        var nearMe = ContainsAny(
            userMessage,
            "near me",
            "nearby",
            "around me",
            "close to me",
            "around here",
            "near us",
            "in my neighborhood",
            "in my neighbourhood");
        var explicitArea = ExtractAreaHint(userMessage);
        var hasLocation = nearMe || !string.IsNullOrWhiteSpace(explicitArea);
        var brand = ResolveBrand(userMessage);
        var includeTypes = ResolveIncludeTypes(userMessage, brand);
        var hasTarget = includeTypes.Length > 0 || !string.IsNullOrWhiteSpace(brand);
        var action = hasTarget && hasLocation
            ? "ReadyForSearch"
            : hasTarget
                ? "MissingLocation"
                : hasLocation
                    ? "MissingTarget"
                    : "Conversation";
        var scopeVerdict = StartsWithAny(userMessage, "who ", "what year", "when was")
            ? "OutOfScope"
            : "InScope";
        if (scopeVerdict == "OutOfScope")
        {
            action = "SoftRedirect";
        }

        return Serialize(new
        {
            intent_family = ContainsAny(userMessage, "budget", "spending", "subscription")
                ? "FinancialGuidance"
                : "PlaceDiscovery",
            in_scope_verdict = scopeVerdict,
            action_type = action,
            confidence = hasTarget || hasLocation ? 0.88 : 0.62,
            ambiguities = action == "MissingLocation"
                ? new[] { "missing_location" }
                : action == "MissingTarget"
                    ? new[] { "missing_target" }
                    : Array.Empty<string>(),
            recommended_next_step = action switch
            {
                "ReadyForSearch" => "ready_for_search",
                "MissingLocation" => "ask_for_location",
                "MissingTarget" => "ask_for_target",
                "SoftRedirect" => "soft_redirect",
                _ => "continue_conversation"
            },
            place_plan = new
            {
                brand_or_entity_terms = string.IsNullOrWhiteSpace(brand) ? Array.Empty<string>() : new[] { brand },
                canonical_concept = includeTypes.FirstOrDefault(),
                candidate_domains = Array.Empty<string>(),
                include_types = includeTypes,
                exclude_types = ContainsAny(userMessage, "car park", "car parks", "parking")
                    ? new[] { "park" }
                    : Array.Empty<string>(),
                preferences = Array.Empty<string>(),
                time_filters = ContainsAny(userMessage, "open now", "24/7") ? new[] { "open_now" } : Array.Empty<string>(),
                audience_filters = Array.Empty<string>()
            },
            location_plan = new
            {
                near_me_semantic = nearMe,
                explicit_area_text = explicitArea,
                resolved_area_hint = explicitArea,
                requires_location = hasTarget,
                can_use_recent_area = true,
                clarification_needed = action is "MissingLocation" or "MissingTarget"
            },
            reason_codes = new[] { "mock_turn_interpretation_v2" }
        });
    }

    private static string? ExtractAreaHint(string userMessage)
    {
        var match = Regex.Match(
            userMessage,
            @"\b(?:in|around|near)\s+([a-z0-9][a-z0-9\s'\-]{1,60})\b",
            RegexOptions.CultureInvariant);
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        var area = match.Groups[1].Value.Trim();
        return area.Length == 0 ? null : area;
    }

    private static string? ResolveBrand(string userMessage)
    {
        var brands = new[] { "starbucks", "tesco", "lidl", "aldi", "mcdonalds", "supervalu" };
        var knownBrand = brands.FirstOrDefault(brand => userMessage.Contains(brand, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(knownBrand))
        {
            return knownBrand;
        }

        var tokens = userMessage
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "find", "show", "me", "near", "nearby", "around", "close", "to", "where", "i", "am", "in", "my",
            "any", "open", "now", "restaurants", "restaurant", "parks", "park", "gyms", "gym", "post", "office",
            "offices", "car", "parking", "stores", "store", "shops", "shop"
        };
        var candidate = tokens
            .TakeWhile(token => !string.Equals(token, "near", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(token, "nearby", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(token, "around", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(token, "in", StringComparison.OrdinalIgnoreCase))
            .Where(token => token.Length >= 3 && !stopWords.Contains(token))
            .Take(3)
            .ToArray();
        return candidate.Length == 0 ? null : string.Join(' ', candidate);
    }

    private static string[] ResolveIncludeTypes(string userMessage, string? brand)
    {
        if (!string.IsNullOrWhiteSpace(brand)
            && ContainsAny(userMessage, "coffee", "cafe", "cafes"))
        {
            return ["cafe"];
        }

        if (ContainsAny(userMessage, "post office", "post offices"))
        {
            return ["post_office"];
        }

        if (ContainsAny(userMessage, "car park", "car parks", "parking"))
        {
            return ["parking"];
        }

        if (ContainsAny(userMessage, "coffee", "cafe", "cafes"))
        {
            return ["cafe"];
        }

        if (ContainsAny(userMessage, "restaurant", "restaurants"))
        {
            return ["restaurant"];
        }

        if (ContainsAny(userMessage, "gym", "gyms", "fitness"))
        {
            return ["gym"];
        }

        return [];
    }

    private static string BuildClarificationReply(ResponseCompositionRequest request)
    {
        var options = request.SuggestedOptions is { Count: > 0 }
            ? $" Options: {string.Join("; ", request.SuggestedOptions.Take(3))}."
            : string.Empty;
        return $"{request.ClarificationQuestion ?? "Could you clarify what you want to focus on?"}{options}";
    }

    private static string BuildResultSummaryReply(ResponseCompositionRequest request)
    {
        if (request.GroundedData.Entities.Count == 0)
        {
            return "I checked the grounded options, but I need one more detail to sharpen the shortlist.";
        }

        var shortlist = string.Join(
            "; ",
            request.GroundedData.Entities
                .Take(3)
                .Select(entity => entity.Label));
        return $"Here are a few grounded options to start with: {shortlist}.";
    }

    private static string BuildSuggestionReply(ResponseCompositionRequest request)
    {
        if (request.GroundedData.SummaryFacts.Count > 0)
        {
            var first = request.GroundedData.SummaryFacts[0];
            return $"{first.Label}: {first.Value}";
        }

        if (request.SuggestedOptions is { Count: > 0 })
        {
            return $"A good next step is to choose one of these directions: {string.Join("; ", request.SuggestedOptions.Take(3))}.";
        }

        return "A good next step is to make the request a little more concrete.";
    }

    private static ResponseCompositionRequest? ExtractResponseCompositionRequest(AIRequest request)
    {
        var prompt = request.Messages.LastOrDefault(message => message.Role == AIMessageRole.User)?.Content;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var match = Regex.Match(
            prompt,
            @"RequestJson:\s*(\{.*\})\s*Return strict JSON only",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ResponseCompositionRequest>(match.Groups[1].Value, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractLatestUserMessage(AIRequest request)
    {
        foreach (var message in request.Messages.Reverse())
        {
            var content = message.Content ?? string.Empty;
            var latestUserMessage = ExtractPromptLineValue(content, "LatestUserMessage:");
            if (!string.IsNullOrWhiteSpace(latestUserMessage))
            {
                return latestUserMessage;
            }

            var directUserMessage = ExtractPromptLineValue(content, "UserMessage:");
            if (!string.IsNullOrWhiteSpace(directUserMessage))
            {
                return directUserMessage;
            }

            if (message.Role == AIMessageRole.User && !string.IsNullOrWhiteSpace(content))
            {
                return content.Trim();
            }
        }

        return string.Empty;
    }

    private static string? ExtractPromptLineValue(string content, string prefix)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.Ordinal));
    }

    private static bool StartsWithAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.StartsWith(token, StringComparison.Ordinal));
    }

    private static string Serialize<T>(T payload)
    {
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }
}
