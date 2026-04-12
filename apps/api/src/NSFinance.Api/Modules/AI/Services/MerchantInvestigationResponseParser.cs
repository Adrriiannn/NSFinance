using System.Text.Json;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed partial class MerchantInvestigationResponseParser(
    ILogger<MerchantInvestigationResponseParser> logger) : IMerchantInvestigationResponseParser
{
    private const int MaxSummaryLength = 1200;
    private const int MaxReasonCodes = 24;
    private const double MinViableCandidateConfidence = 0.60d;
    private static readonly IReadOnlySet<string> AllowedTopLevelProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "overallConfidence",
        "ambiguityLevel",
        "recommendation",
        "summary",
        "candidates",
        "aliasSuggestions",
        "evidence"
    };

    private static readonly HashSet<string> DangerousBroadAliasTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "amazon",
        "google",
        "apple",
        "microsoft",
        "paypal"
    };

    public MerchantInvestigationParseResult Parse(AIResponse response)
    {
        var reasonCodes = new List<string>();

        if (!response.Succeeded)
        {
            reasonCodes.Add("ai_response_failed");
            return BuildFailure(reasonCodes, response.FailureReason ?? "AI provider failed.", "ai_response_failed");
        }

        var payload = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(payload))
        {
            reasonCodes.Add("empty_payload");
            return BuildFailure(reasonCodes, "AI response did not include structured payload.", "empty_payload");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Merchant investigation response parse failed due to malformed JSON");
            reasonCodes.Add("invalid_json_payload");
            return BuildFailure(reasonCodes, "AI response schema mismatch: invalid JSON.", "invalid_json_payload");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reasonCodes.Add("root_not_object");
                return BuildFailure(reasonCodes, "AI response schema mismatch: root must be an object.", "root_not_object");
            }

            if (!ValidateNoUnexpectedProperties(root, AllowedTopLevelProperties, out var unexpectedTopLevel))
            {
                reasonCodes.Add("unexpected_top_level_property");
                return BuildFailure(
                    reasonCodes,
                    $"AI response schema mismatch: unexpected top-level property '{unexpectedTopLevel}'.",
                    "unexpected_top_level_property");
            }

            if (!TryGetBoundedDouble(root, "overallConfidence", 0d, 1d, out var overallConfidence))
            {
                reasonCodes.Add("invalid_overall_confidence");
                return BuildFailure(reasonCodes, "AI response schema mismatch: invalid overallConfidence.", "invalid_overall_confidence");
            }

            if (!TryGetBoundedDouble(root, "ambiguityLevel", 0d, 1d, out var ambiguityLevel))
            {
                reasonCodes.Add("invalid_ambiguity_level");
                return BuildFailure(reasonCodes, "AI response schema mismatch: invalid ambiguityLevel.", "invalid_ambiguity_level");
            }

            if (!TryGetRequiredString(root, "recommendation", 64, out var recommendationToken)
                || !TryMapRecommendation(recommendationToken, out var recommendation))
            {
                reasonCodes.Add("invalid_recommendation");
                return BuildFailure(reasonCodes, "AI response schema mismatch: invalid recommendation.", "invalid_recommendation");
            }

            if (!TryGetRequiredString(root, "summary", MaxSummaryLength, out var summaryText))
            {
                reasonCodes.Add("invalid_summary_text");
                return BuildFailure(reasonCodes, "AI response schema mismatch: invalid summary text.", "invalid_summary_text");
            }

            var candidateParse = ParseCandidates(root, reasonCodes);
            if (!candidateParse.Succeeded)
            {
                return BuildFailure(
                    reasonCodes,
                    candidateParse.FailureReason ?? "AI response schema mismatch: invalid candidates.",
                    candidateParse.FailureCode ?? "invalid_candidates");
            }

            var evidenceParse = ParseEvidenceArray(root, "evidence", reasonCodes, optional: true);
            if (!evidenceParse.Succeeded)
            {
                return BuildFailure(
                    reasonCodes,
                    evidenceParse.FailureReason ?? "AI response schema mismatch: invalid evidence list.",
                    evidenceParse.FailureCode ?? "invalid_evidence");
            }

            var aliasParse = ParseAliasSuggestions(root, "aliasSuggestions", reasonCodes, optional: true);
            if (!aliasParse.Succeeded)
            {
                return BuildFailure(
                    reasonCodes,
                    aliasParse.FailureReason ?? "AI response schema mismatch: invalid alias suggestions.",
                    aliasParse.FailureCode ?? "invalid_alias_suggestions");
            }

            var semantic = ValidateSemanticConsistency(
                recommendation,
                overallConfidence,
                ambiguityLevel,
                candidateParse.Candidates,
                aliasParse.AliasSuggestions,
                reasonCodes);

            if (!semantic.IsValid)
            {
                return BuildFailure(
                    reasonCodes,
                    semantic.FailureReason ?? "AI response recommendation is semantically invalid.",
                    semantic.FailureCode ?? "semantic_validation_failed");
            }

            var insufficientEvidence = recommendation is MerchantInvestigationRecommendation.InsufficientEvidence
                                       or MerchantInvestigationRecommendation.Unresolved
                                       || semantic.ViableCandidateCount == 0;

            var lowTrust = insufficientEvidence
                           || recommendation == MerchantInvestigationRecommendation.ConflictingCandidates
                           || overallConfidence < 0.75d
                           || ambiguityLevel > 0.35d;
            if (lowTrust)
            {
                reasonCodes.Add("low_trust_valid_output");
            }

            reasonCodes.Add($"recommendation_{ToContractRecommendation(recommendation)}");

            var normalizedCandidates = candidateParse.Candidates
                .Select(candidate => candidate with { AmbiguityScore = ambiguityLevel })
                .ToArray();

            var structured = new MerchantInvestigationStructuredResponse(
                OverallConfidence: overallConfidence,
                AmbiguityLevel: ambiguityLevel,
                Recommendation: ToContractRecommendation(recommendation),
                Summary: summaryText,
                Candidates: normalizedCandidates.Select(MapCandidate).ToArray(),
                AliasSuggestions: aliasParse.AliasSuggestions.Select(MapAliasSuggestion).ToArray(),
                Evidence: evidenceParse.Evidence.Select(MapEvidence).ToArray());

            var result = new MerchantInvestigationResult(
                Succeeded: true,
                InsufficientEvidence: insufficientEvidence,
                Candidates: normalizedCandidates,
                Evidence: evidenceParse.Evidence,
                FailureReason: null,
                Recommendation: recommendation,
                OverallConfidence: overallConfidence,
                AmbiguityLevel: ambiguityLevel,
                AliasSuggestions: aliasParse.AliasSuggestions,
                InvestigationReasonCodes: reasonCodes.Take(MaxReasonCodes).ToArray(),
                ParserRejected: false);

            return new MerchantInvestigationParseResult(
                ParsedSuccessfully: true,
                SemanticallyValid: true,
                IsLowTrustValid: lowTrust,
                Structured: structured,
                InvestigationResult: result,
                ReasonCodes: reasonCodes.Take(MaxReasonCodes).ToArray(),
                FailureReason: null,
                FailureCode: null);
        }
    }

    private static MerchantInvestigationParseResult BuildFailure(
        IReadOnlyList<string> reasonCodes,
        string failureReason,
        string failureCode)
    {
        return new MerchantInvestigationParseResult(
            ParsedSuccessfully: false,
            SemanticallyValid: false,
            IsLowTrustValid: false,
            Structured: null,
            InvestigationResult: new MerchantInvestigationResult(
                Succeeded: false,
                InsufficientEvidence: true,
                Candidates: [],
                Evidence: [],
                FailureReason: failureReason,
                Recommendation: MerchantInvestigationRecommendation.Unresolved,
                OverallConfidence: 0d,
                AmbiguityLevel: 1d,
                AliasSuggestions: [],
                InvestigationReasonCodes: reasonCodes.ToArray(),
                ParserRejected: true),
            ReasonCodes: reasonCodes.ToArray(),
            FailureReason: failureReason,
            FailureCode: failureCode);
    }
}
