using System.Globalization;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public interface IAdjudicationResultValidator
{
    FinancialAdviceAdjudicationValidationResult Validate(
        FinancialAdviceAdjudicationValidationRequest request);
}

public sealed class AdjudicationResultValidator : IAdjudicationResultValidator
{
    private static readonly Regex NumericTokenRegex = new(@"\d+(\.\d+)?", RegexOptions.Compiled);

    public FinancialAdviceAdjudicationValidationResult Validate(
        FinancialAdviceAdjudicationValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warnings = new List<string>(request.PlanReasonCodes.Count + 4);
        warnings.AddRange(request.PlanReasonCodes);
        if (request.Response.Warnings is { Count: > 0 })
        {
            warnings.AddRange(request.Response.Warnings.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        var fallbackOutcome = ParseOutcome(request.Response.Outcome);
        var targetIds = request.TargetFindings
            .Select(finding => finding.FindingId)
            .ToHashSet(StringComparer.Ordinal);
        var outcomes = new List<FinancialAdviceFindingAdjudication>(request.TargetFindings.Count);

        if (request.Response.Adjustments is { Count: > 0 })
        {
            foreach (var adjustment in request.Response.Adjustments)
            {
                if (string.IsNullOrWhiteSpace(adjustment.FindingId) || !targetIds.Contains(adjustment.FindingId))
                {
                    warnings.Add("adjudication_unknown_finding_ignored");
                    continue;
                }

                var sanitizedSummary = SanitizeRefinedSummary(
                    adjustment.RefinedSummary,
                    request.EvidenceSummary,
                    request.TargetFindings);
                if (!string.Equals(sanitizedSummary, adjustment.RefinedSummary, StringComparison.Ordinal))
                {
                    warnings.Add("adjudication_refined_summary_numbers_sanitized");
                }

                outcomes.Add(
                    new FinancialAdviceFindingAdjudication(
                        FindingId: adjustment.FindingId,
                        Outcome: ParseOutcome(adjustment.Outcome),
                        RefinedSummary: sanitizedSummary,
                        ConfidenceDelta: CoerceConfidenceDelta(adjustment.ConfidenceDelta),
                        NuanceNotes: adjustment.NuanceNotes?.Where(item => !string.IsNullOrWhiteSpace(item)).Take(4).ToArray() ?? [],
                        ReasonCode: string.IsNullOrWhiteSpace(adjustment.ReasonCode) ? null : adjustment.ReasonCode.Trim()));
            }
        }

        foreach (var missingId in targetIds.Except(outcomes.Select(item => item.FindingId), StringComparer.Ordinal))
        {
            outcomes.Add(
                new FinancialAdviceFindingAdjudication(
                    FindingId: missingId,
                    Outcome: fallbackOutcome,
                    RefinedSummary: null,
                    ConfidenceDelta: null,
                    NuanceNotes: [],
                    ReasonCode: "fallback_global_outcome"));
        }

        var responseSummary = SanitizeRefinedSummary(
            request.Response.SummaryRefinement,
            request.EvidenceSummary,
            request.TargetFindings);
        if (!string.Equals(responseSummary, request.Response.SummaryRefinement, StringComparison.Ordinal))
        {
            warnings.Add("adjudication_summary_refinement_sanitized");
        }

        return new FinancialAdviceAdjudicationValidationResult(
            FindingOutcomes: outcomes
                .OrderBy(item => item.FindingId, StringComparer.Ordinal)
                .ToArray(),
            ResponseSummary: responseSummary,
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static FinancialAdviceAdjudicationOutcome ParseOutcome(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return FinancialAdviceAdjudicationOutcome.InsufficientEvidence;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "approve" => FinancialAdviceAdjudicationOutcome.Approve,
            "approve_with_refinement" => FinancialAdviceAdjudicationOutcome.ApproveWithRefinement,
            "lower_confidence" => FinancialAdviceAdjudicationOutcome.LowerConfidence,
            "reject_conclusion" => FinancialAdviceAdjudicationOutcome.RejectConclusion,
            "insufficient_evidence" => FinancialAdviceAdjudicationOutcome.InsufficientEvidence,
            _ => FinancialAdviceAdjudicationOutcome.InsufficientEvidence
        };
    }

    private static double? CoerceConfidenceDelta(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Math.Clamp(value.Value, -0.35d, 0d);
    }

    private static string? SanitizeRefinedSummary(
        string? candidate,
        IReadOnlyList<string> evidenceSummary,
        IReadOnlyList<FinancialAdviceFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();
        var allowedNumbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidenceLine in evidenceSummary)
        {
            foreach (Match match in NumericTokenRegex.Matches(evidenceLine))
            {
                allowedNumbers.Add(match.Value);
            }
        }

        foreach (var finding in findings)
        {
            foreach (var metric in finding.SupportingMetrics)
            {
                allowedNumbers.Add(metric.Value.ToString("0.####", CultureInfo.InvariantCulture));
            }
        }

        foreach (Match match in NumericTokenRegex.Matches(trimmed))
        {
            if (allowedNumbers.Contains(match.Value))
            {
                continue;
            }

            return null;
        }

        return trimmed;
    }
}
