using System.Text.Json;
using System.Text.Json.Serialization;
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
        var payload = scenario switch
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
}
