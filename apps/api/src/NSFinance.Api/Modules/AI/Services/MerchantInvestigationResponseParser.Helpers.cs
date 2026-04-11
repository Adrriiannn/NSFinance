using System.Text.Json;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed partial class MerchantInvestigationResponseParser
{
    private static (bool IsValid, int ViableCandidateCount, string? FailureCode, string? FailureReason) ValidateSemanticConsistency(
        MerchantInvestigationRecommendation recommendation,
        double overallConfidence,
        double ambiguityLevel,
        IReadOnlyList<MerchantInvestigationCandidate> candidates,
        IReadOnlyList<MerchantInvestigationAliasSuggestion> rootAliasSuggestions,
        List<string> reasonCodes)
    {
        var viableCandidateCount = candidates.Count(candidate =>
            candidate.Confidence >= MinViableCandidateConfidence
            && candidate.DescriptorMatchStrength >= 0.55d
            && candidate.EntityMatchStrength >= 0.55d);

        if (recommendation == MerchantInvestigationRecommendation.AcceptCandidate && candidates.Count == 0)
        {
            reasonCodes.Add("invalid_recommendation_accept_without_candidates");
            return (false, viableCandidateCount, "invalid_recommendation_accept_without_candidates", "Recommendation accept_candidate requires at least one candidate.");
        }

        if (recommendation == MerchantInvestigationRecommendation.AcceptCandidate && viableCandidateCount == 0)
        {
            reasonCodes.Add("invalid_recommendation_accept_without_viable_candidate");
            return (false, viableCandidateCount, "invalid_recommendation_accept_without_viable_candidate", "Recommendation accept_candidate requires at least one viable candidate.");
        }

        if (recommendation == MerchantInvestigationRecommendation.AcceptCandidate && ambiguityLevel > 0.45d)
        {
            reasonCodes.Add("invalid_recommendation_accept_high_ambiguity");
            return (false, viableCandidateCount, "invalid_recommendation_accept_high_ambiguity", "Recommendation accept_candidate is inconsistent with high ambiguity.");
        }

        if (recommendation == MerchantInvestigationRecommendation.AcceptCandidate && overallConfidence < 0.60d)
        {
            reasonCodes.Add("invalid_recommendation_accept_low_confidence");
            return (false, viableCandidateCount, "invalid_recommendation_accept_low_confidence", "Recommendation accept_candidate is inconsistent with low confidence.");
        }

        if (recommendation == MerchantInvestigationRecommendation.AcceptCautiously && viableCandidateCount == 0)
        {
            reasonCodes.Add("invalid_recommendation_cautious_without_viable_candidate");
            return (false, viableCandidateCount, "invalid_recommendation_cautious_without_viable_candidate", "Recommendation accept_cautiously requires at least one viable candidate.");
        }

        if (recommendation == MerchantInvestigationRecommendation.ConflictingCandidates && viableCandidateCount < 2)
        {
            reasonCodes.Add("invalid_recommendation_conflicting_without_multiple_candidates");
            return (false, viableCandidateCount, "invalid_recommendation_conflicting_without_multiple_candidates", "Recommendation conflicting_candidates requires multiple viable candidates.");
        }

        if (recommendation == MerchantInvestigationRecommendation.InsufficientEvidence && viableCandidateCount > 0 && overallConfidence >= 0.80d)
        {
            reasonCodes.Add("invalid_recommendation_insufficient_with_high_confidence_candidates");
            return (false, viableCandidateCount, "invalid_recommendation_insufficient_with_high_confidence_candidates", "Recommendation insufficient_evidence conflicts with high-confidence viable candidates.");
        }

        foreach (var candidate in candidates)
        {
            if (candidate.MerchantUsageType == MerchantUsageType.NarrowUse
                && candidate.MixedUseRisk
                && !HasMixedUseExplanation(candidate.WhyItMayBeWrong))
            {
                reasonCodes.Add("invalid_candidate_narrow_use_mixed_risk_without_explanation");
                return (false, viableCandidateCount, "invalid_candidate_narrow_use_mixed_risk_without_explanation", "Narrow-use candidate cannot carry mixed-use risk without explicit explanation.");
            }
        }

        if (HasUnsafeBroadAlias(rootAliasSuggestions) || candidates.Any(x => HasUnsafeBroadAlias(x.AliasSuggestions)))
        {
            reasonCodes.Add("unsafe_broad_alias_suggestion_detected");
            return (false, viableCandidateCount, "unsafe_broad_alias_suggestion_detected", "Unsafe broad alias suggestions detected for dangerous merchant family.");
        }

        return (true, viableCandidateCount, null, null);
    }

    private static bool HasMixedUseExplanation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("mixed", StringComparison.OrdinalIgnoreCase)
               || text.Contains("broad", StringComparison.OrdinalIgnoreCase)
               || text.Contains("ambig", StringComparison.OrdinalIgnoreCase)
               || text.Contains("family", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnsafeBroadAlias(IReadOnlyList<MerchantInvestigationAliasSuggestion>? suggestions)
    {
        if (suggestions is null || suggestions.Count == 0)
        {
            return false;
        }

        foreach (var suggestion in suggestions)
        {
            if (suggestion.Confidence < 0.60d)
            {
                continue;
            }

            var aliasText = suggestion.AliasText.Trim().ToLowerInvariant();
            if (DangerousBroadAliasTokens.Contains(aliasText))
            {
                return !HasBroadAliasWarningNote(suggestion.Notes);
            }

            var tokens = aliasText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 1 && DangerousBroadAliasTokens.Contains(tokens[0]))
            {
                return !HasBroadAliasWarningNote(suggestion.Notes);
            }
        }

        return false;
    }

    private static bool HasBroadAliasWarningNote(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return false;
        }

        return notes.Contains("broad", StringComparison.OrdinalIgnoreCase)
               || notes.Contains("family", StringComparison.OrdinalIgnoreCase)
               || notes.Contains("generic", StringComparison.OrdinalIgnoreCase)
               || notes.Contains("caution", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMapRecommendation(string token, out MerchantInvestigationRecommendation recommendation)
    {
        recommendation = token switch
        {
            MerchantInvestigationContract.RecommendationAcceptCandidate => MerchantInvestigationRecommendation.AcceptCandidate,
            MerchantInvestigationContract.RecommendationAcceptCautiously => MerchantInvestigationRecommendation.AcceptCautiously,
            MerchantInvestigationContract.RecommendationUnresolved => MerchantInvestigationRecommendation.Unresolved,
            MerchantInvestigationContract.RecommendationInsufficientEvidence => MerchantInvestigationRecommendation.InsufficientEvidence,
            MerchantInvestigationContract.RecommendationConflictingCandidates => MerchantInvestigationRecommendation.ConflictingCandidates,
            _ => MerchantInvestigationRecommendation.Unresolved
        };

        return MerchantInvestigationContract.AllowedRecommendations.Contains(token);
    }

    private static string ToContractRecommendation(MerchantInvestigationRecommendation recommendation)
    {
        return recommendation switch
        {
            MerchantInvestigationRecommendation.AcceptCandidate => MerchantInvestigationContract.RecommendationAcceptCandidate,
            MerchantInvestigationRecommendation.AcceptCautiously => MerchantInvestigationContract.RecommendationAcceptCautiously,
            MerchantInvestigationRecommendation.InsufficientEvidence => MerchantInvestigationContract.RecommendationInsufficientEvidence,
            MerchantInvestigationRecommendation.ConflictingCandidates => MerchantInvestigationContract.RecommendationConflictingCandidates,
            _ => MerchantInvestigationContract.RecommendationUnresolved
        };
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement result)
    {
        result = default;
        return element.TryGetProperty(propertyName, out result) && result.ValueKind == JsonValueKind.Array;
    }

    private static bool TryGetRequiredString(JsonElement element, string propertyName, int maxLength, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > maxLength)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryGetOptionalString(JsonElement element, string propertyName, int maxLength, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var parsed = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(parsed))
        {
            return true;
        }

        if (parsed.Length > maxLength)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetOptionalCountryCode(JsonElement element, string propertyName, out string? countryCode)
    {
        countryCode = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var token = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        if (token.Length != 2 || !token.All(char.IsLetter))
        {
            return false;
        }

        countryCode = token.ToUpperInvariant();
        return true;
    }

    private static bool TryGetBoundedDouble(JsonElement element, string propertyName, double min, double max, out double value)
    {
        value = 0d;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!property.TryGetDouble(out value))
        {
            return false;
        }

        return value >= min && value <= max;
    }

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => (value = true) || true,
            JsonValueKind.False => (value = false) || true,
            _ => false
        };
    }

    private static bool TryGetOptionalBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryGetEnumString<TEnum>(JsonElement element, string propertyName, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var token = property.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(token) && Enum.TryParse(token, ignoreCase: true, out value);
    }

    private static bool TryGetStringArray(
        JsonElement element,
        string propertyName,
        int maxCount,
        int maxLength,
        out IReadOnlyList<string> values,
        out string? errorCode)
    {
        var parsed = new List<string>();
        if (!element.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind == JsonValueKind.Null)
        {
            values = parsed;
            errorCode = null;
            return true;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            values = [];
            errorCode = $"invalid_{propertyName}_array";
            return false;
        }

        var index = 0;
        foreach (var item in arrayElement.EnumerateArray())
        {
            index++;
            if (index > maxCount)
            {
                values = [];
                errorCode = $"{propertyName}_count_exceeded";
                return false;
            }

            if (item.ValueKind != JsonValueKind.String)
            {
                values = [];
                errorCode = $"invalid_{propertyName}_item";
                return false;
            }

            var token = item.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(token) || token.Length > maxLength)
            {
                values = [];
                errorCode = $"invalid_{propertyName}_item";
                return false;
            }

            parsed.Add(token);
        }

        values = parsed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        errorCode = null;
        return true;
    }

    private static MerchantInvestigationStructuredCandidate MapCandidate(MerchantInvestigationCandidate candidate)
    {
        return new MerchantInvestigationStructuredCandidate(
            CanonicalName: candidate.CanonicalName,
            DisplayName: candidate.DisplayName,
            LikelyOfficialWebsite: candidate.OfficialWebsite,
            ParentBrand: null,
            MerchantType: candidate.MerchantType,
            MerchantUsageType: candidate.MerchantUsageType,
            BusinessSummary: candidate.DescriptionSummary,
            SupportsSubscriptions: null,
            SupportsRecurringPayments: null,
            SupportsOneTimePurchases: null,
            SupportsMarketplacePayments: null,
            SupportsInAppPurchases: null,
            LikelyCategoryFamilies: null,
            Confidence: candidate.Confidence,
            DescriptorMatchStrength: candidate.DescriptorMatchStrength,
            EntityMatchStrength: candidate.EntityMatchStrength,
            MixedUseRisk: candidate.MixedUseRisk,
            HasContradictions: candidate.HasContradictions,
            WhyItMayMatch: candidate.WhyItMayMatch,
            WhyItMayBeWrong: candidate.WhyItMayBeWrong,
            PrimaryCountryCode: string.IsNullOrWhiteSpace(candidate.PrimaryCountryCode) ? null : candidate.PrimaryCountryCode,
            AliasCandidates: candidate.AliasCandidates,
            AliasSuggestions: candidate.AliasSuggestions?.Select(MapAliasSuggestion).ToArray(),
            EvidenceItems: candidate.EvidenceItems?.Select(MapEvidence).ToArray());
    }

    private static MerchantInvestigationAliasSuggestionPayload MapAliasSuggestion(MerchantInvestigationAliasSuggestion alias)
    {
        return new MerchantInvestigationAliasSuggestionPayload(
            AliasText: alias.AliasText,
            AliasType: alias.AliasType,
            Confidence: alias.Confidence,
            Notes: alias.Notes,
            IsPreferred: alias.IsPreferred);
    }

    private static MerchantInvestigationStructuredEvidence MapEvidence(MerchantInvestigationEvidence evidence)
    {
        return new MerchantInvestigationStructuredEvidence(
            EvidenceType: evidence.EvidenceType,
            SourceClass: evidence.SourceClass ?? "unknown",
            Summary: evidence.EvidenceSummary,
            Confidence: evidence.Confidence,
            Relevance: evidence.Relevance,
            SourceReference: evidence.SourceReference);
    }
}
