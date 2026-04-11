using System.Text.Json;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class MerchantInvestigationResponseParser(
    ILogger<MerchantInvestigationResponseParser> logger) : IMerchantInvestigationResponseParser
{
    private const int MaxSummaryLength = 1200;
    private const int MaxReasonCodes = 24;

    public MerchantInvestigationParseResult Parse(AIResponse response)
    {
        var reasonCodes = new List<string>();

        if (!response.Succeeded)
        {
            reasonCodes.Add("ai_response_failed");
            return BuildFailure(
                reasonCodes,
                response.FailureReason ?? "AI provider failed.");
        }

        var payload = response.StructuredPayloadJson ?? response.Content;
        if (string.IsNullOrWhiteSpace(payload))
        {
            reasonCodes.Add("empty_payload");
            return BuildFailure(reasonCodes, "AI response did not include structured payload.");
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
            return BuildFailure(reasonCodes, "AI response schema mismatch: invalid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reasonCodes.Add("root_not_object");
                return BuildFailure(reasonCodes, "AI response schema mismatch: root must be an object.");
            }

            if (!TryParseSummary(root, reasonCodes, out var summary, out var recommendation))
            {
                return BuildFailure(reasonCodes, "AI response schema mismatch: invalid summary section.");
            }

            var candidateParse = ParseCandidates(root, reasonCodes);
            if (!candidateParse.Succeeded)
            {
                return BuildFailure(reasonCodes, candidateParse.FailureReason ?? "AI response schema mismatch: invalid candidates.");
            }

            var evidenceParse = ParseEvidenceArray(root, "evidence", reasonCodes);
            if (!evidenceParse.Succeeded)
            {
                return BuildFailure(reasonCodes, evidenceParse.FailureReason ?? "AI response schema mismatch: invalid evidence list.");
            }

            var aliasParse = ParseAliasSuggestions(root, "aliasSuggestions", reasonCodes);
            if (!aliasParse.Succeeded)
            {
                return BuildFailure(reasonCodes, aliasParse.FailureReason ?? "AI response schema mismatch: invalid alias suggestions.");
            }

            if (!ValidateRecommendationConsistency(
                    recommendation,
                    summary.OverallConfidence,
                    summary.AmbiguityLevel,
                    candidateParse.Candidates.Count,
                    reasonCodes,
                    out var recommendationFailure))
            {
                return BuildFailure(reasonCodes, recommendationFailure ?? "AI response recommendation is semantically invalid.");
            }

            var insufficientEvidence = recommendation is MerchantInvestigationRecommendation.InsufficientEvidence
                                       or MerchantInvestigationRecommendation.Unresolved
                                       || candidateParse.Candidates.Count == 0;

            var lowTrust = insufficientEvidence
                           || recommendation == MerchantInvestigationRecommendation.ConflictingCandidates
                           || summary.OverallConfidence < 0.75d
                           || summary.AmbiguityLevel > 0.35d;

            if (lowTrust)
            {
                reasonCodes.Add("low_trust_valid_output");
            }

            reasonCodes.Add($"recommendation_{ToContractRecommendation(recommendation)}");

            var normalizedCandidates = candidateParse.Candidates
                .Select(candidate => candidate with { AmbiguityScore = summary.AmbiguityLevel })
                .ToArray();

            var result = new MerchantInvestigationResult(
                Succeeded: true,
                InsufficientEvidence: insufficientEvidence,
                Candidates: normalizedCandidates,
                Evidence: evidenceParse.Evidence,
                FailureReason: null,
                Recommendation: recommendation,
                OverallConfidence: summary.OverallConfidence,
                AmbiguityLevel: summary.AmbiguityLevel,
                AliasSuggestions: aliasParse.AliasSuggestions,
                InvestigationReasonCodes: reasonCodes.Take(MaxReasonCodes).ToArray(),
                ParserRejected: false);

            var structured = new MerchantInvestigationStructuredResponse(
                Summary: new MerchantInvestigationSummary(
                    summary.OverallConfidence,
                    summary.AmbiguityLevel,
                    ToContractRecommendation(recommendation),
                    summary.Summary),
                Candidates: [],
                AliasSuggestions: [],
                Evidence: []);

            return new MerchantInvestigationParseResult(
                ParsedSuccessfully: true,
                SemanticallyValid: true,
                IsLowTrustValid: lowTrust,
                Structured: structured,
                InvestigationResult: result,
                ReasonCodes: reasonCodes.Take(MaxReasonCodes).ToArray(),
                FailureReason: null);
        }
    }

    private static MerchantInvestigationParseResult BuildFailure(IReadOnlyList<string> reasonCodes, string failureReason)
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
            FailureReason: failureReason);
    }

    private static bool TryParseSummary(
        JsonElement root,
        List<string> reasonCodes,
        out (double OverallConfidence, double AmbiguityLevel, string Summary) summary,
        out MerchantInvestigationRecommendation recommendation)
    {
        summary = default;
        recommendation = MerchantInvestigationRecommendation.Unresolved;

        if (!TryGetObject(root, "summary", out var summaryElement))
        {
            reasonCodes.Add("missing_summary");
            return false;
        }

        if (!TryGetBoundedDouble(summaryElement, "overallConfidence", 0d, 1d, out var overallConfidence))
        {
            reasonCodes.Add("invalid_overall_confidence");
            return false;
        }

        if (!TryGetBoundedDouble(summaryElement, "ambiguityLevel", 0d, 1d, out var ambiguityLevel))
        {
            reasonCodes.Add("invalid_ambiguity_level");
            return false;
        }

        if (!TryGetRequiredString(summaryElement, "summary", maxLength: MaxSummaryLength, out var summaryText))
        {
            reasonCodes.Add("invalid_summary_text");
            return false;
        }

        if (!TryGetRequiredString(summaryElement, "recommendation", maxLength: 64, out var recommendationToken)
            || !TryMapRecommendation(recommendationToken, out recommendation))
        {
            reasonCodes.Add("invalid_recommendation");
            return false;
        }

        summary = (overallConfidence, ambiguityLevel, summaryText);
        return true;
    }

    private static (bool Succeeded, IReadOnlyList<MerchantInvestigationCandidate> Candidates, string? FailureReason) ParseCandidates(
        JsonElement root,
        List<string> reasonCodes)
    {
        if (!TryGetArray(root, "candidates", out var candidatesElement))
        {
            reasonCodes.Add("missing_candidates_array");
            return (false, [], "AI response schema mismatch: candidates must be an array.");
        }

        var list = new List<MerchantInvestigationCandidate>();
        var index = 0;
        foreach (var candidateElement in candidatesElement.EnumerateArray())
        {
            index++;
            if (index > MerchantInvestigationContract.MaxCandidateCount)
            {
                reasonCodes.Add("candidate_count_exceeded");
                return (false, [], "AI response exceeds candidate count limits.");
            }

            if (candidateElement.ValueKind != JsonValueKind.Object)
            {
                reasonCodes.Add("candidate_not_object");
                return (false, [], "Candidate entry must be an object.");
            }

            if (!TryGetRequiredString(candidateElement, "canonicalName", 160, out var canonicalName)
                || !TryGetBoundedDouble(candidateElement, "confidence", 0d, 1d, out var confidence)
                || !TryGetEnumString<MerchantType>(candidateElement, "merchantType", out var merchantType)
                || !TryGetEnumString<MerchantUsageType>(candidateElement, "merchantUsageType", out var usageType)
                || !TryGetBoundedDouble(candidateElement, "descriptorMatchStrength", 0d, 1d, out var descriptorStrength)
                || !TryGetBoundedDouble(candidateElement, "entityMatchStrength", 0d, 1d, out var entityStrength)
                || !TryGetBool(candidateElement, "mixedUseRisk", out var mixedUseRisk)
                || !TryGetRequiredString(candidateElement, "whyItMayMatch", 800, out var whyMatch)
                || !TryGetRequiredString(candidateElement, "whyItMayBeWrong", 800, out var whyWrong))
            {
                reasonCodes.Add($"candidate_required_fields_invalid_{index}");
                return (false, [], "Candidate entry is missing required fields or contains invalid values.");
            }

            if (!TryGetOptionalBool(candidateElement, "hasContradictions", out var contradictions))
            {
                reasonCodes.Add($"candidate_invalid_has_contradictions_{index}");
                return (false, [], "Candidate contradiction flag must be boolean.");
            }

            var displayName = TryGetOptionalString(candidateElement, "displayName", 160);
            var website = TryGetOptionalString(candidateElement, "likelyOfficialWebsite", 512);
            var businessSummary = TryGetOptionalString(candidateElement, "businessSummary", 1200);
            var countryCode = TryGetOptionalString(candidateElement, "primaryCountryCode", 3);

            if (!TryGetStringArray(candidateElement, "aliasCandidates", 12, 180, out var aliasCandidates, out var aliasCandidatesErrorCode))
            {
                reasonCodes.Add(aliasCandidatesErrorCode ?? $"invalid_alias_candidates_{index}");
                return (false, [], "Candidate alias candidates are invalid.");
            }

            var candidateAliasParse = ParseAliasSuggestions(candidateElement, "aliasSuggestions", reasonCodes, optional: true);
            if (!candidateAliasParse.Succeeded)
            {
                return (false, [], "Candidate alias suggestions are invalid.");
            }

            var candidateEvidenceParse = ParseEvidenceArray(candidateElement, "evidenceItems", reasonCodes, optional: true);
            if (!candidateEvidenceParse.Succeeded)
            {
                return (false, [], "Candidate evidence items are invalid.");
            }

            list.Add(new MerchantInvestigationCandidate(
                ExistingMerchantId: null,
                CanonicalName: canonicalName,
                DisplayName: string.IsNullOrWhiteSpace(displayName) ? canonicalName : displayName!,
                MerchantType: merchantType,
                MerchantUsageType: usageType,
                PrimaryCountryCode: NormalizeCountryCode(countryCode),
                Confidence: confidence,
                AmbiguityScore: 0d,
                MixedUseRisk: mixedUseRisk,
                HasContradictions: contradictions,
                OfficialWebsite: website,
                DescriptionSummary: businessSummary,
                AliasCandidates: aliasCandidates,
                DescriptorMatchStrength: descriptorStrength,
                EntityMatchStrength: entityStrength,
                AliasSuggestions: candidateAliasParse.AliasSuggestions,
                EvidenceItems: candidateEvidenceParse.Evidence));
        }

        return (true, list, null);
    }

    private static (bool Succeeded, IReadOnlyList<MerchantInvestigationEvidence> Evidence, string? FailureReason) ParseEvidenceArray(
        JsonElement container,
        string propertyName,
        List<string> reasonCodes,
        bool optional = false)
    {
        if (!TryGetArray(container, propertyName, out var evidenceArray))
        {
            if (optional)
            {
                return (true, [], null);
            }

            reasonCodes.Add($"invalid_{propertyName}");
            return (false, [], "Evidence collection is invalid.");
        }

        var list = new List<MerchantInvestigationEvidence>();
        var index = 0;
        foreach (var evidenceElement in evidenceArray.EnumerateArray())
        {
            index++;
            if (index > MerchantInvestigationContract.MaxEvidenceCount)
            {
                reasonCodes.Add($"{propertyName}_count_exceeded");
                return (false, [], "Evidence count exceeds allowed limit.");
            }

            if (evidenceElement.ValueKind != JsonValueKind.Object
                || !TryGetEnumString<MerchantEvidenceType>(evidenceElement, "evidenceType", out var evidenceType)
                || !TryGetRequiredString(evidenceElement, "sourceClass", 120, out var sourceClass)
                || !TryGetRequiredString(evidenceElement, "summary", 1200, out var summary)
                || !TryGetBoundedDouble(evidenceElement, "confidence", 0d, 1d, out var confidence)
                || !TryGetBoundedDouble(evidenceElement, "relevance", 0d, 1d, out var relevance))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_{index}");
                return (false, [], "Evidence item has invalid required fields.");
            }

            list.Add(new MerchantInvestigationEvidence(
                EvidenceType: evidenceType,
                EvidenceSummary: summary,
                Confidence: confidence,
                SourceReference: TryGetOptionalString(evidenceElement, "sourceReference", 1024),
                SourceClass: sourceClass,
                Relevance: relevance));
        }

        return (true, list, null);
    }

    private static (bool Succeeded, IReadOnlyList<MerchantInvestigationAliasSuggestion> AliasSuggestions, string? FailureReason) ParseAliasSuggestions(
        JsonElement container,
        string propertyName,
        List<string> reasonCodes,
        bool optional = false)
    {
        if (!TryGetArray(container, propertyName, out var aliasArray))
        {
            if (optional)
            {
                return (true, [], null);
            }

            reasonCodes.Add($"invalid_{propertyName}");
            return (false, [], "Alias suggestions must be an array.");
        }

        var list = new List<MerchantInvestigationAliasSuggestion>();
        var index = 0;
        foreach (var aliasElement in aliasArray.EnumerateArray())
        {
            index++;
            if (index > MerchantInvestigationContract.MaxAliasSuggestionCount)
            {
                reasonCodes.Add($"{propertyName}_count_exceeded");
                return (false, [], "Alias suggestion count exceeds limit.");
            }

            if (aliasElement.ValueKind != JsonValueKind.Object
                || !TryGetRequiredString(aliasElement, "aliasText", 180, out var aliasText)
                || !TryGetRequiredString(aliasElement, "aliasType", 80, out var aliasType)
                || !TryGetBoundedDouble(aliasElement, "confidence", 0d, 1d, out var confidence))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_{index}");
                return (false, [], "Alias suggestion item is invalid.");
            }

            if (!TryGetOptionalBool(aliasElement, "isPreferred", out var isPreferred))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_is_preferred_{index}");
                return (false, [], "Alias suggestion isPreferred must be boolean.");
            }

            list.Add(new MerchantInvestigationAliasSuggestion(
                AliasText: aliasText,
                AliasType: aliasType,
                Confidence: confidence,
                Notes: TryGetOptionalString(aliasElement, "notes", 256),
                IsPreferred: isPreferred));
        }

        return (true, list, null);
    }

    private static bool ValidateRecommendationConsistency(
        MerchantInvestigationRecommendation recommendation,
        double overallConfidence,
        double ambiguityLevel,
        int candidateCount,
        List<string> reasonCodes,
        out string? failureReason)
    {
        failureReason = null;
        switch (recommendation)
        {
            case MerchantInvestigationRecommendation.AcceptCandidate when candidateCount == 0:
                reasonCodes.Add("invalid_recommendation_accept_without_candidates");
                failureReason = "Recommendation accept_candidate requires at least one candidate.";
                return false;
            case MerchantInvestigationRecommendation.AcceptCandidate when overallConfidence < 0.50d:
                reasonCodes.Add("invalid_recommendation_accept_low_confidence");
                failureReason = "Recommendation accept_candidate is inconsistent with low confidence.";
                return false;
            case MerchantInvestigationRecommendation.AcceptCandidate when ambiguityLevel > 0.55d:
                reasonCodes.Add("invalid_recommendation_accept_high_ambiguity");
                failureReason = "Recommendation accept_candidate is inconsistent with high ambiguity.";
                return false;
            case MerchantInvestigationRecommendation.ConflictingCandidates when candidateCount < 2:
                reasonCodes.Add("invalid_recommendation_conflicting_without_multiple_candidates");
                failureReason = "Recommendation conflicting_candidates requires multiple candidates.";
                return false;
            case MerchantInvestigationRecommendation.InsufficientEvidence when candidateCount > 0 && overallConfidence > 0.80d:
                reasonCodes.Add("invalid_recommendation_insufficient_with_high_confidence_candidates");
                failureReason = "Recommendation insufficient_evidence conflicts with high-confidence candidate set.";
                return false;
        }

        return true;
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

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement result)
    {
        result = default;
        return element.TryGetProperty(propertyName, out result) && result.ValueKind == JsonValueKind.Object;
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
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        value = candidate.Length <= maxLength ? candidate : candidate[..maxLength];
        return true;
    }

    private static string? TryGetOptionalString(JsonElement element, string propertyName, int maxLength)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
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
        return !string.IsNullOrWhiteSpace(token) && Enum.TryParse<TEnum>(token, ignoreCase: true, out value);
    }

    private static bool TryGetStringArray(
        JsonElement element,
        string propertyName,
        int maxCount,
        int maxLength,
        out IReadOnlyList<string> values,
        out string? errorCode)
    {
        var internalValues = new List<string>();
        if (!element.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind == JsonValueKind.Null)
        {
            values = internalValues;
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
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            internalValues.Add(token.Length <= maxLength ? token : token[..maxLength]);
        }

        values = internalValues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        errorCode = null;
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
