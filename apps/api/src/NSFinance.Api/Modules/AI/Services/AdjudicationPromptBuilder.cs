using System.Globalization;
using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public interface IAdjudicationPromptBuilder
{
    FinancialAdviceAdjudicationInputPacket BuildInputPacket(
        FinancialAdviceAdjudicationExecutionRequest request,
        IReadOnlyList<FinancialAdviceFinding> targetFindings);

    string BuildUserPrompt(string packetJson);

    string BuildSystemInstructions();
}

public sealed class AdjudicationPromptBuilder : IAdjudicationPromptBuilder
{
    public FinancialAdviceAdjudicationInputPacket BuildInputPacket(
        FinancialAdviceAdjudicationExecutionRequest request,
        IReadOnlyList<FinancialAdviceFinding> targetFindings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetFindings);

        var profileSummary = BuildProfileSummary(request.Profile);
        var uncertainty = targetFindings
            .SelectMany(finding => finding.UncertaintyMarkers)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var policyConstraints = new[]
        {
            "no_new_findings_or_numbers",
            "no_policy_override_on_protected_or_essential_categories",
            "only_refine_within_provided_evidence_bounds",
            "prefer_lower_confidence_or_reject_when_evidence_is_weak"
        };

        return new FinancialAdviceAdjudicationInputPacket(
            UserQuery: request.UserQuery,
            Intent: request.Intent,
            Mode: request.Plan.Mode,
            Findings: targetFindings
                .Select(finding => new FinancialAdviceFindingForAdjudication(
                    FindingId: finding.FindingId,
                    FindingType: finding.FindingType,
                    Severity: finding.Severity,
                    PriorityScore: finding.PriorityScore,
                    Confidence: finding.Confidence,
                    EvidenceSummary: finding.EvidenceSummary,
                    SupportingMetrics: finding.SupportingMetrics.Take(6).ToArray(),
                    UncertaintyMarkers: finding.UncertaintyMarkers.Take(6).ToArray(),
                    PolicyWarnings: finding.PolicyWarnings.Take(6).ToArray(),
                    RecommendedActions: finding.RecommendedActions.Take(4).ToArray()))
                .ToArray(),
            ProfileSummary: profileSummary,
            EvidenceSummary: request.EvidenceSummary.Take(8).ToArray(),
            UncertaintyFlags: uncertainty,
            PolicyConstraints: policyConstraints,
            AllowedOutcomes:
            [
                "approve",
                "approve_with_refinement",
                "lower_confidence",
                "reject_conclusion",
                "insufficient_evidence"
            ]);
    }

    public string BuildUserPrompt(string packetJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packetJson);

        return
            $$"""
              AdjudicationInputJson:
              {{packetJson}}

              Return strict JSON:
              {
                "outcome": "approve|approve_with_refinement|lower_confidence|reject_conclusion|insufficient_evidence",
                "summaryRefinement": "string|null",
                "adjustments": [
                  {
                    "findingId": "string",
                    "outcome": "approve|approve_with_refinement|lower_confidence|reject_conclusion|insufficient_evidence",
                    "confidenceDelta": -0.15,
                    "refinedSummary": "string|null",
                    "nuanceNotes": ["string"],
                    "reasonCode": "string|null"
                  }
                ],
                "warnings": ["string"],
                "rationale": "string|null"
              }
              """;
    }

    public string BuildSystemInstructions()
    {
        return
            """
            You are a constrained financial adjudication reviewer.
            You MUST only review provided deterministic findings and policy-reviewed evidence.
            You MUST NOT introduce new findings, new numeric facts, or unsupported claims.
            You MUST NOT override policy constraints or protected-category restrictions.
            You MAY approve, refine wording, lower confidence, reject weak findings, or mark insufficient evidence.
            """;
    }

    private static FinancialAdviceProfileSummaryForAdjudication BuildProfileSummary(UserFinancialContextSnapshot profile)
    {
        var recurringBaseline = ParseRecurringBaseline(profile.KnownObligationsJson);
        var avgDailyBaseline = ParseAverageDailySpend(profile.SpendingTendenciesJson);
        var protectedHints = ParseProtectedHints(profile.CategoryFlexibilityMarkersJson);

        return new FinancialAdviceProfileSummaryForAdjudication(
            Currency: profile.Currency,
            MonthlyIncomeRange: profile.MonthlyIncomeRange,
            AdviceStylePreference: profile.AdviceStylePreference,
            BaselineRecurringMonthly: recurringBaseline,
            BaselineAverageDailySpend: avgDailyBaseline,
            ProtectedPreferenceHints: protectedHints);
    }

    private static decimal? ParseRecurringBaseline(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            decimal total = 0m;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!TryGetPropertyCaseInsensitive(item, "amount", out var amountElement)
                    || !TryReadDecimal(amountElement, out var amount))
                {
                    continue;
                }

                total += Math.Abs(amount);
            }

            return total <= 0m ? null : decimal.Round(total, 2, MidpointRounding.AwayFromZero);
        }
        catch
        {
            return null;
        }
    }

    private static decimal? ParseAverageDailySpend(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !TryGetPropertyCaseInsensitive(document.RootElement, "averageDailySpend", out var property)
                || !TryReadDecimal(property, out var value))
            {
                return null;
            }

            return value;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ParseProtectedHints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var hints = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        hints.Add(value.Trim());
                    }
                }
            }

            return hints.Take(6).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool TryGetPropertyCaseInsensitive(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value;
            return true;
        }

        return false;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
    }
}
