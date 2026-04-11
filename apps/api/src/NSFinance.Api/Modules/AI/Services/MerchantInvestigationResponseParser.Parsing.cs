using System.Text.Json;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed partial class MerchantInvestigationResponseParser
{
    private static (
        bool Succeeded,
        IReadOnlyList<MerchantInvestigationCandidate> Candidates,
        string? FailureReason,
        string? FailureCode) ParseCandidates(
        JsonElement root,
        List<string> reasonCodes)
    {
        if (!TryGetArray(root, "candidates", out var candidatesElement))
        {
            reasonCodes.Add("missing_candidates_array");
            return (false, [], "AI response schema mismatch: candidates must be an array.", "missing_candidates_array");
        }

        var list = new List<MerchantInvestigationCandidate>();
        var index = 0;
        foreach (var candidateElement in candidatesElement.EnumerateArray())
        {
            index++;
            if (index > MerchantInvestigationContract.MaxCandidateCount)
            {
                reasonCodes.Add("candidate_count_exceeded");
                return (false, [], "AI response exceeds candidate count limits.", "candidate_count_exceeded");
            }

            if (candidateElement.ValueKind != JsonValueKind.Object)
            {
                reasonCodes.Add("candidate_not_object");
                return (false, [], "Candidate entry must be an object.", "candidate_not_object");
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
                return (false, [], "Candidate entry is missing required fields or contains invalid values.", $"candidate_required_fields_invalid_{index}");
            }

            if (!TryGetOptionalBool(candidateElement, "hasContradictions", out var contradictions))
            {
                reasonCodes.Add($"candidate_invalid_has_contradictions_{index}");
                return (false, [], "Candidate contradiction flag must be boolean.", $"candidate_invalid_has_contradictions_{index}");
            }

            if (!TryGetOptionalString(candidateElement, "displayName", 160, out var displayName)
                || !TryGetOptionalString(candidateElement, "likelyOfficialWebsite", 512, out var website)
                || !TryGetOptionalString(candidateElement, "businessSummary", 1200, out var businessSummary)
                || !TryGetOptionalCountryCode(candidateElement, "primaryCountryCode", out var countryCode))
            {
                reasonCodes.Add($"candidate_optional_fields_invalid_{index}");
                return (false, [], "Candidate optional fields are invalid.", $"candidate_optional_fields_invalid_{index}");
            }

            if (!TryGetStringArray(candidateElement, "aliasCandidates", 12, 180, out var aliasCandidates, out var aliasCandidatesErrorCode))
            {
                reasonCodes.Add(aliasCandidatesErrorCode ?? $"invalid_alias_candidates_{index}");
                return (false, [], "Candidate alias candidates are invalid.", aliasCandidatesErrorCode ?? $"invalid_alias_candidates_{index}");
            }

            var candidateAliasParse = ParseAliasSuggestions(candidateElement, "aliasSuggestions", reasonCodes, optional: true);
            if (!candidateAliasParse.Succeeded)
            {
                return (false, [], "Candidate alias suggestions are invalid.", candidateAliasParse.FailureCode);
            }

            var candidateEvidenceParse = ParseEvidenceArray(candidateElement, "evidenceItems", reasonCodes, optional: true);
            if (!candidateEvidenceParse.Succeeded)
            {
                return (false, [], "Candidate evidence items are invalid.", candidateEvidenceParse.FailureCode);
            }

            list.Add(new MerchantInvestigationCandidate(
                ExistingMerchantId: null,
                CanonicalName: canonicalName,
                DisplayName: string.IsNullOrWhiteSpace(displayName) ? canonicalName : displayName!,
                MerchantType: merchantType,
                MerchantUsageType: usageType,
                PrimaryCountryCode: countryCode ?? string.Empty,
                Confidence: confidence,
                AmbiguityScore: 0d,
                MixedUseRisk: mixedUseRisk,
                HasContradictions: contradictions,
                OfficialWebsite: website,
                DescriptionSummary: businessSummary,
                AliasCandidates: aliasCandidates,
                WhyItMayMatch: whyMatch,
                WhyItMayBeWrong: whyWrong,
                DescriptorMatchStrength: descriptorStrength,
                EntityMatchStrength: entityStrength,
                AliasSuggestions: candidateAliasParse.AliasSuggestions,
                EvidenceItems: candidateEvidenceParse.Evidence));
        }

        return (true, list, null, null);
    }

    private static (
        bool Succeeded,
        IReadOnlyList<MerchantInvestigationEvidence> Evidence,
        string? FailureReason,
        string? FailureCode) ParseEvidenceArray(
        JsonElement container,
        string propertyName,
        List<string> reasonCodes,
        bool optional = false)
    {
        if (!TryGetArray(container, propertyName, out var evidenceArray))
        {
            if (optional)
            {
                return (true, [], null, null);
            }

            reasonCodes.Add($"invalid_{propertyName}");
            return (false, [], "Evidence collection is invalid.", $"invalid_{propertyName}");
        }

        var list = new List<MerchantInvestigationEvidence>();
        var index = 0;
        foreach (var evidenceElement in evidenceArray.EnumerateArray())
        {
            index++;
            if (index > MerchantInvestigationContract.MaxEvidenceCount)
            {
                reasonCodes.Add($"{propertyName}_count_exceeded");
                return (false, [], "Evidence count exceeds allowed limit.", $"{propertyName}_count_exceeded");
            }

            if (evidenceElement.ValueKind != JsonValueKind.Object
                || !TryGetEnumString<MerchantEvidenceType>(evidenceElement, "evidenceType", out var evidenceType)
                || !TryGetRequiredString(evidenceElement, "sourceClass", 120, out var sourceClass)
                || !TryGetRequiredString(evidenceElement, "summary", 1200, out var summary)
                || !TryGetBoundedDouble(evidenceElement, "confidence", 0d, 1d, out var confidence)
                || !TryGetBoundedDouble(evidenceElement, "relevance", 0d, 1d, out var relevance))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_{index}");
                return (false, [], "Evidence item has invalid required fields.", $"invalid_{propertyName}_item_{index}");
            }

            if (!TryGetOptionalString(evidenceElement, "sourceReference", 1024, out var sourceReference))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_source_reference_{index}");
                return (false, [], "Evidence sourceReference must be a valid string.", $"invalid_{propertyName}_item_source_reference_{index}");
            }

            list.Add(new MerchantInvestigationEvidence(
                EvidenceType: evidenceType,
                EvidenceSummary: summary,
                Confidence: confidence,
                SourceReference: sourceReference,
                SourceClass: sourceClass,
                Relevance: relevance));
        }

        return (true, list, null, null);
    }

    private static (
        bool Succeeded,
        IReadOnlyList<MerchantInvestigationAliasSuggestion> AliasSuggestions,
        string? FailureReason,
        string? FailureCode) ParseAliasSuggestions(
        JsonElement container,
        string propertyName,
        List<string> reasonCodes,
        bool optional = false)
    {
        if (!TryGetArray(container, propertyName, out var aliasArray))
        {
            if (optional)
            {
                return (true, [], null, null);
            }

            reasonCodes.Add($"invalid_{propertyName}");
            return (false, [], "Alias suggestions must be an array.", $"invalid_{propertyName}");
        }

        var list = new List<MerchantInvestigationAliasSuggestion>();
        var index = 0;
        foreach (var aliasElement in aliasArray.EnumerateArray())
        {
            index++;
            if (index > MerchantInvestigationContract.MaxAliasSuggestionCount)
            {
                reasonCodes.Add($"{propertyName}_count_exceeded");
                return (false, [], "Alias suggestion count exceeds limit.", $"{propertyName}_count_exceeded");
            }

            if (aliasElement.ValueKind != JsonValueKind.Object
                || !TryGetRequiredString(aliasElement, "aliasText", 180, out var aliasText)
                || !TryGetRequiredString(aliasElement, "aliasType", 80, out var aliasTypeRaw)
                || !TryGetBoundedDouble(aliasElement, "confidence", 0d, 1d, out var confidence))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_{index}");
                return (false, [], "Alias suggestion item is invalid.", $"invalid_{propertyName}_item_{index}");
            }

            if (!Enum.TryParse<MerchantAliasType>(aliasTypeRaw, ignoreCase: true, out var aliasType))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_alias_type_{index}");
                return (false, [], "Alias suggestion aliasType is invalid.", $"invalid_{propertyName}_item_alias_type_{index}");
            }

            if (!TryGetOptionalBool(aliasElement, "isPreferred", out var isPreferred))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_is_preferred_{index}");
                return (false, [], "Alias suggestion isPreferred must be boolean.", $"invalid_{propertyName}_item_is_preferred_{index}");
            }

            if (!TryGetOptionalString(aliasElement, "notes", 256, out var notes))
            {
                reasonCodes.Add($"invalid_{propertyName}_item_notes_{index}");
                return (false, [], "Alias suggestion notes must be valid string.", $"invalid_{propertyName}_item_notes_{index}");
            }

            list.Add(new MerchantInvestigationAliasSuggestion(
                AliasText: aliasText,
                AliasType: aliasType.ToString(),
                Confidence: confidence,
                Notes: notes,
                IsPreferred: isPreferred));
        }

        return (true, list, null, null);
    }
}
