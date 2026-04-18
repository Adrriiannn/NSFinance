using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdvicePolicyService
{
    IReadOnlyList<FinancialAdvicePolicyReviewedFinding> ApplyPolicy(
        FinancialCompanionContext context,
        IReadOnlyList<FinancialAdviceFinding> deterministicFindings);
}

public sealed class FinancialAdvicePolicyService : IFinancialAdvicePolicyService
{
    private static readonly HashSet<FinancialAdviceActionType> SupportedActionTypes =
    [
        FinancialAdviceActionType.ReviewSpend,
        FinancialAdviceActionType.ReduceSpend,
        FinancialAdviceActionType.TrackRecurringCharge,
        FinancialAdviceActionType.AdjustBudget,
        FinancialAdviceActionType.BuildBuffer,
        FinancialAdviceActionType.ReviewPlan,
        FinancialAdviceActionType.KeepCourse
    ];

    public IReadOnlyList<FinancialAdvicePolicyReviewedFinding> ApplyPolicy(
        FinancialCompanionContext context,
        IReadOnlyList<FinancialAdviceFinding> deterministicFindings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deterministicFindings);

        var protectedPreferenceHints = ParseProtectedPreferenceHints(context.Profile.CategoryFlexibilityMarkersJson);
        var reviewed = new List<FinancialAdvicePolicyReviewedFinding>(deterministicFindings.Count);
        foreach (var finding in deterministicFindings)
        {
            var warnings = new List<string>(4);
            var exclusions = new List<string>(4);
            var adjustedActions = new List<FinancialAdviceActionCandidate>(finding.RecommendedActions.Count);
            var confidence = finding.Confidence;
            var severity = finding.Severity;
            var aiAllowed = finding.AiAdjudicationAllowed;
            var decision = FinancialAdvicePolicyDecision.Approved;

            foreach (var action in finding.RecommendedActions)
            {
                if (!SupportedActionTypes.Contains(action.ActionType))
                {
                    exclusions.Add("unsupported_action_outside_current_scope");
                    continue;
                }

                if (action.ActionType == FinancialAdviceActionType.ReduceSpend)
                {
                    var blockedByProtectedCategory = action.IsProtectedCategory
                        || finding.ProtectedCategoryFlags.Count > 0;
                    if (blockedByProtectedCategory)
                    {
                        exclusions.Add("protected_category_reduction_blocked");
                        warnings.Add("policy_protected_category_guardrail_applied");
                        continue;
                    }

                    if (action.SuggestedMagnitude.GetValueOrDefault() > 0.20m && confidence < 0.85d)
                    {
                        exclusions.Add("aggressive_reduction_blocked_without_strong_evidence");
                        warnings.Add("policy_aggressive_reduction_guardrail_applied");
                        continue;
                    }

                    if (confidence < 0.60d)
                    {
                        exclusions.Add("reduction_blocked_low_confidence");
                        warnings.Add("policy_low_confidence_reduction_guardrail_applied");
                        continue;
                    }

                    if (ConflictsWithProtectedPreference(action, finding, protectedPreferenceHints))
                    {
                        exclusions.Add("profile_protected_preference_conflict");
                        warnings.Add("policy_profile_preference_guardrail_applied");
                        continue;
                    }
                }

                adjustedActions.Add(action);
            }

            if (finding.UncertaintyMarkers.Count > 0 && confidence > 0.75d)
            {
                confidence = 0.75d;
                warnings.Add("confidence_capped_due_to_uncertainty");
            }

            if (confidence < 0.45d && severity >= FinancialAdviceSeverity.Moderate)
            {
                severity = LowerSeverity(severity);
                warnings.Add("severity_downgraded_due_to_weak_evidence");
            }

            if (adjustedActions.Count == 0
                && finding.RecommendedActions.Count > 0
                && finding.FindingType is FinancialAdviceFindingType.DiscretionaryOverspend or FinancialAdviceFindingType.BudgetSlippage
                && confidence < 0.55d)
            {
                decision = FinancialAdvicePolicyDecision.Rejected;
                aiAllowed = false;
                warnings.Add("finding_rejected_policy_insufficient_safe_actions");
            }
            else if (warnings.Count > 0 || exclusions.Count > 0 || adjustedActions.Count != finding.RecommendedActions.Count || Math.Abs(confidence - finding.Confidence) > 0.001d || severity != finding.Severity)
            {
                decision = FinancialAdvicePolicyDecision.ApprovedWithAdjustments;
            }

            var adjustedFinding = finding with
            {
                Severity = severity,
                PriorityScore = RecomputePriority(severity, confidence),
                Confidence = confidence,
                RecommendedActions = adjustedActions,
                PolicyWarnings = finding.PolicyWarnings.Concat(warnings).Distinct(StringComparer.Ordinal).ToArray(),
                PolicyExclusions = finding.PolicyExclusions.Concat(exclusions).Distinct(StringComparer.Ordinal).ToArray(),
                AiAdjudicationAllowed = aiAllowed
            };

            reviewed.Add(new FinancialAdvicePolicyReviewedFinding(
                Finding: adjustedFinding,
                Decision: decision,
                Warnings: warnings,
                Exclusions: exclusions));
        }

        return reviewed
            .OrderByDescending(item => item.Finding.PriorityScore)
            .ThenBy(item => item.Finding.FindingId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ConflictsWithProtectedPreference(
        FinancialAdviceActionCandidate action,
        FinancialAdviceFinding finding,
        IReadOnlyList<string> protectedPreferenceHints)
    {
        if (protectedPreferenceHints.Count == 0)
        {
            return false;
        }

        var domainToken = action.TargetDomainCode?.ToString() ?? finding.DomainCode?.ToString();
        var categoryToken = action.TargetCategoryCode?.ToString() ?? finding.CategoryCode?.ToString();
        var domainName = finding.DomainName ?? string.Empty;
        var categoryName = finding.CategoryName ?? string.Empty;
        var combined = $"{domainToken}|{categoryToken}|{domainName}|{categoryName}".ToLowerInvariant();

        foreach (var hint in protectedPreferenceHints)
        {
            var hintLower = hint.ToLowerInvariant();
            if (combined.Contains(hintLower, StringComparison.Ordinal))
            {
                return true;
            }

            if (hintLower.Contains("essential", StringComparison.Ordinal)
                || hintLower.Contains("protected", StringComparison.Ordinal)
                || hintLower.Contains("do_not_cut", StringComparison.Ordinal))
            {
                if (finding.ProtectedCategoryFlags.Count > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int RecomputePriority(FinancialAdviceSeverity severity, double confidence)
    {
        var severityWeight = severity switch
        {
            FinancialAdviceSeverity.Critical => 95,
            FinancialAdviceSeverity.High => 82,
            FinancialAdviceSeverity.Moderate => 65,
            FinancialAdviceSeverity.Low => 45,
            _ => 25
        };
        return Math.Clamp(severityWeight + (int)Math.Round(Math.Clamp(confidence, 0d, 1d) * 10d), 1, 100);
    }

    private static FinancialAdviceSeverity LowerSeverity(FinancialAdviceSeverity severity)
    {
        return severity switch
        {
            FinancialAdviceSeverity.Critical => FinancialAdviceSeverity.High,
            FinancialAdviceSeverity.High => FinancialAdviceSeverity.Moderate,
            FinancialAdviceSeverity.Moderate => FinancialAdviceSeverity.Low,
            FinancialAdviceSeverity.Low => FinancialAdviceSeverity.Info,
            _ => FinancialAdviceSeverity.Info
        };
    }

    private static IReadOnlyList<string> ParseProtectedPreferenceHints(string? json)
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

                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in item.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            hints.Add(value.Trim());
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        hints.Add(property.Value.ToString());
                    }
                }
            }

            return hints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
