using System.Text.Json;
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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
            MockAIScenario.UserChatComplex => BuildComplexChatResponse(request),
            _ => BuildSimpleChatResponse(request)
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
        var descriptor = request.Metadata.TryGetValue("normalizedDescriptor", out var normalized)
            ? normalized
            : "unknown descriptor";

        var response = new MerchantInvestigationStructuredResponse(
            Summary: new MerchantInvestigationSummary(0.93d, 0.08d, MerchantInvestigationRecommendation.AcceptCandidate, "Strong descriptor-to-entity alignment."),
            Candidates:
            [
                new MerchantInvestigationStructuredCandidate(
                    CanonicalName: "Acme Streaming",
                    DisplayName: "Acme Streaming",
                    LikelyOfficialWebsite: "https://acme-streaming.test",
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.NarrowUse,
                    BusinessSummary: "Digital streaming subscription service.",
                    SupportsSubscriptions: true,
                    SupportsRecurringPayments: true,
                    SupportsOneTimePurchases: false,
                    SupportsMarketplacePayments: false,
                    SupportsInAppPurchases: true,
                    LikelyCategoryFamilies: ["Subscriptions", "Entertainment"],
                    DescriptorMatchStrength: 0.95d,
                    EntityMatchStrength: 0.91d,
                    MixedUseRisk: false,
                    Confidence: 0.94d,
                    WhyItMayMatch: $"Descriptor {descriptor} aligns with known billing format.",
                    WhyItMayBeWrong: "Could overlap with similarly named intermediary merchants.",
                    PrimaryCountryCode: "IE",
                    HasContradictions: false,
                    AliasCandidates: [descriptor])
            ],
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestion(descriptor.ToUpperInvariant(), "BillingDescriptor", 0.94d, true),
                new MerchantInvestigationAliasSuggestion("ACME STREAMING", "MerchantName", 0.88d, false)
            ],
            Evidence:
            [
                new MerchantInvestigationStructuredEvidence(
                    MerchantEvidenceType.TransactionObservation,
                    "Descriptor token sequence aligns with prior observations.",
                    0.89d,
                    null,
                    0.90d)
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantAmbiguousCandidates(AIRequest request)
    {
        var descriptor = request.Metadata.TryGetValue("normalizedDescriptor", out var normalized)
            ? normalized
            : "unknown descriptor";

        var response = new MerchantInvestigationStructuredResponse(
            Summary: new MerchantInvestigationSummary(0.62d, 0.42d, MerchantInvestigationRecommendation.ConflictingCandidates, "Several plausible entities found."),
            Candidates:
            [
                new MerchantInvestigationStructuredCandidate(
                    CanonicalName: "Acme Group",
                    DisplayName: "Acme Group",
                    LikelyOfficialWebsite: null,
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.MixedUse,
                    BusinessSummary: "Mixed-use merchant group.",
                    SupportsSubscriptions: true,
                    SupportsRecurringPayments: true,
                    SupportsOneTimePurchases: true,
                    SupportsMarketplacePayments: true,
                    SupportsInAppPurchases: true,
                    LikelyCategoryFamilies: ["Subscriptions", "Shopping"],
                    DescriptorMatchStrength: 0.70d,
                    EntityMatchStrength: 0.66d,
                    MixedUseRisk: true,
                    Confidence: 0.67d,
                    WhyItMayMatch: descriptor,
                    WhyItMayBeWrong: "Descriptor is too broad.",
                    PrimaryCountryCode: "US",
                    HasContradictions: false,
                    AliasCandidates: [descriptor]),
                new MerchantInvestigationStructuredCandidate(
                    CanonicalName: "Acme Services",
                    DisplayName: "Acme Services",
                    LikelyOfficialWebsite: null,
                    MerchantType: MerchantType.Intermediary,
                    MerchantUsageType: MerchantUsageType.Intermediary,
                    BusinessSummary: "Payment intermediary for multiple merchants.",
                    SupportsSubscriptions: true,
                    SupportsRecurringPayments: true,
                    SupportsOneTimePurchases: true,
                    SupportsMarketplacePayments: true,
                    SupportsInAppPurchases: false,
                    LikelyCategoryFamilies: ["Intermediary"],
                    DescriptorMatchStrength: 0.63d,
                    EntityMatchStrength: 0.61d,
                    MixedUseRisk: true,
                    Confidence: 0.64d,
                    WhyItMayMatch: "Known intermediary descriptor overlap.",
                    WhyItMayBeWrong: "No decisive unique signal.",
                    PrimaryCountryCode: "US",
                    HasContradictions: false,
                    AliasCandidates: [descriptor])
            ],
            AliasSuggestions: [],
            Evidence:
            [
                new MerchantInvestigationStructuredEvidence(
                    MerchantEvidenceType.Deterministic,
                    "Ambiguous entity overlap detected.",
                    0.55d,
                    null,
                    0.68d)
            ]);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildMerchantInsufficientEvidence(AIRequest request)
    {
        var descriptor = request.Metadata.TryGetValue("normalizedDescriptor", out var normalized)
            ? normalized
            : "unknown descriptor";

        var response = new MerchantInvestigationStructuredResponse(
            Summary: new MerchantInvestigationSummary(0.21d, 0.12d, MerchantInvestigationRecommendation.InsufficientEvidence, "Insufficient signal to identify a trusted merchant."),
            Candidates: [],
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestion(descriptor.ToUpperInvariant(), "BillingDescriptor", 0.35d, false)
            ],
            Evidence: []);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static string BuildSimpleChatResponse(AIRequest request)
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

    private static string BuildComplexChatResponse(AIRequest request)
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
