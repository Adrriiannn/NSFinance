using System.Text.Json;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class MerchantInvestigationResponseParser(
    ILogger<MerchantInvestigationResponseParser> logger) : IMerchantInvestigationResponseParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool TryParse(AIResponse response, out MerchantInvestigationResult result, out IReadOnlyList<string> reasonCodes)
    {
        var localReasonCodes = new List<string>();

        if (!response.Succeeded)
        {
            localReasonCodes.Add("ai_response_failed");
            result = new MerchantInvestigationResult(
                Succeeded: false,
                InsufficientEvidence: true,
                Candidates: [],
                Evidence: [],
                FailureReason: response.FailureReason ?? "AI provider failed.");
            reasonCodes = localReasonCodes;
            return false;
        }

        var payload = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(payload))
        {
            localReasonCodes.Add("empty_payload");
            result = new MerchantInvestigationResult(
                Succeeded: false,
                InsufficientEvidence: true,
                Candidates: [],
                Evidence: [],
                FailureReason: "AI response did not include structured payload.");
            reasonCodes = localReasonCodes;
            return false;
        }

        MerchantInvestigationStructuredResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MerchantInvestigationStructuredResponse>(payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Merchant investigation response parse failed due to malformed JSON");
            localReasonCodes.Add("invalid_json_payload");
            result = new MerchantInvestigationResult(
                Succeeded: false,
                InsufficientEvidence: true,
                Candidates: [],
                Evidence: [],
                FailureReason: "AI response schema mismatch.");
            reasonCodes = localReasonCodes;
            return false;
        }

        if (parsed is null || parsed.Summary is null)
        {
            localReasonCodes.Add("missing_summary");
            result = new MerchantInvestigationResult(
                Succeeded: false,
                InsufficientEvidence: true,
                Candidates: [],
                Evidence: [],
                FailureReason: "AI response did not provide investigation summary.");
            reasonCodes = localReasonCodes;
            return false;
        }

        var candidates = parsed.Candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.CanonicalName))
            .Select(candidate => new MerchantInvestigationCandidate(
                ExistingMerchantId: null,
                CanonicalName: candidate.CanonicalName.Trim(),
                DisplayName: string.IsNullOrWhiteSpace(candidate.DisplayName) ? candidate.CanonicalName.Trim() : candidate.DisplayName.Trim(),
                MerchantType: candidate.MerchantType,
                MerchantUsageType: candidate.MerchantUsageType,
                PrimaryCountryCode: NormalizeCountryCode(candidate.PrimaryCountryCode),
                Confidence: Math.Clamp(candidate.Confidence, 0d, 1d),
                AmbiguityScore: Math.Clamp(parsed.Summary.AmbiguityLevel, 0d, 1d),
                MixedUseRisk: candidate.MixedUseRisk,
                HasContradictions: candidate.HasContradictions,
                OfficialWebsite: candidate.LikelyOfficialWebsite,
                DescriptionSummary: string.IsNullOrWhiteSpace(candidate.BusinessSummary)
                    ? null
                    : candidate.BusinessSummary.Trim(),
                AliasCandidates: candidate.AliasCandidates
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(alias => alias.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();

        var evidence = parsed.Evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.EvidenceSummary))
            .Select(item => new MerchantInvestigationEvidence(
                item.EvidenceType,
                item.EvidenceSummary.Trim(),
                Math.Clamp(item.Confidence, 0d, 1d),
                item.SourceReference))
            .ToArray();

        var insufficientEvidence = parsed.Summary.Recommendation is MerchantInvestigationRecommendation.InsufficientEvidence
                                   || parsed.Summary.Recommendation is MerchantInvestigationRecommendation.Unresolved
                                   || candidates.Length == 0;

        if (insufficientEvidence)
        {
            localReasonCodes.Add("insufficient_structured_evidence");
        }

        localReasonCodes.Add($"recommendation_{parsed.Summary.Recommendation.ToString().ToLowerInvariant()}");

        result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: insufficientEvidence,
            Candidates: candidates,
            Evidence: evidence,
            FailureReason: null);

        reasonCodes = localReasonCodes;
        return true;
    }

    private static string NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return "US";
        }

        var trimmed = countryCode.Trim();
        return trimmed.Length <= 3 ? trimmed.ToUpperInvariant() : trimmed[..3].ToUpperInvariant();
    }
}
