using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdviceAdjudicationExecutionRequest(
    string UserQuery,
    FinancialCompanionIntent Intent,
    UserFinancialContextSnapshot Profile,
    FinancialAdviceAdjudicationPlan Plan,
    IReadOnlyList<FinancialAdvicePolicyReviewedFinding> PolicyReviewedFindings,
    IReadOnlyList<string> EvidenceSummary,
    AIModelClass PreferredModelClass,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IFinancialAdviceAdjudicationService
{
    Task<FinancialAdviceAdjudicationResult> AdjudicateAsync(
        FinancialAdviceAdjudicationExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed class FinancialAdviceAdjudicationService(
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IOptions<CompanionAdviceOptions> options) : IFinancialAdviceAdjudicationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex NumericTokenRegex = new(@"\d+(\.\d+)?", RegexOptions.Compiled);
    private readonly CompanionAdviceOptions _options = options.Value;

    public async Task<FinancialAdviceAdjudicationResult> AdjudicateAsync(
        FinancialAdviceAdjudicationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Plan.Mode == FinancialAdviceAdjudicationMode.Skipped || request.Plan.TargetFindingIds.Count == 0)
        {
            return new FinancialAdviceAdjudicationResult(
                UsedAi: false,
                Succeeded: true,
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                ModelUsed: "deterministic_only",
                InputTokens: 0,
                OutputTokens: 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: request.Plan.ReasonCodes);
        }

        var targetFindings = request.PolicyReviewedFindings
            .Where(item => request.Plan.TargetFindingIds.Contains(item.Finding.FindingId, StringComparer.Ordinal))
            .Where(item => item.Decision != FinancialAdvicePolicyDecision.Rejected && item.Finding.AiAdjudicationAllowed)
            .Select(item => item.Finding)
            .Take(Math.Clamp(_options.MaxAdjudicatedFindings, 1, 6))
            .ToArray();
        if (targetFindings.Length == 0)
        {
            return new FinancialAdviceAdjudicationResult(
                UsedAi: false,
                Succeeded: true,
                Mode: FinancialAdviceAdjudicationMode.Skipped,
                ModelUsed: "deterministic_only",
                InputTokens: 0,
                OutputTokens: 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: ["adjudication_skipped_no_eligible_findings"]);
        }

        var packet = BuildInputPacket(request, targetFindings);
        var packetJson = JsonSerializer.Serialize(packet, SerializerOptions);
        if (packetJson.Length > _options.MaxAdjudicationInputChars)
        {
            packet = packet with
            {
                Findings = packet.Findings.Take(Math.Max(1, packet.Findings.Count - 1)).ToArray(),
                EvidenceSummary = packet.EvidenceSummary.Take(4).ToArray()
            };
            packetJson = JsonSerializer.Serialize(packet, SerializerOptions);
        }

        var route = modelRouter.Resolve(
            taskType: AITaskType.FinancialReasoning,
            preferredModelClass: request.PreferredModelClass,
            complexityHint: $"adjudication:{request.Intent}:{request.Plan.Mode}");
        var aiRequest = AIRequest.Create(
            taskType: AITaskType.FinancialReasoning,
            preferredModelClass: route.ModelClass,
            messages:
            [
                AIMessage.User(
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
                      """)
            ],
            correlationId: request.CorrelationId,
            systemInstructions:
            """
            You are a constrained financial adjudication reviewer.
            You MUST only review provided deterministic findings and policy-reviewed evidence.
            You MUST NOT introduce new findings, new numeric facts, or unsupported claims.
            You MUST NOT override policy constraints or protected-category restrictions.
            You MAY approve, refine wording, lower confidence, reject weak findings, or mark insufficient evidence.
            """,
            structuredOutputSchemaName: "financial_advice_adjudication_v1",
            temperature: 0.1d,
            maxOutputTokens: Math.Clamp(_options.MaxAdjudicationOutputTokens, 120, 800),
            metadata: request.Metadata);

        var aiResponse = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        if (!aiResponse.Succeeded)
        {
            return new FinancialAdviceAdjudicationResult(
                UsedAi: true,
                Succeeded: false,
                Mode: request.Plan.Mode,
                ModelUsed: route.Model,
                InputTokens: aiResponse.InputTokenEstimate ?? 0,
                OutputTokens: aiResponse.OutputTokenEstimate ?? 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: ["adjudication_ai_request_failed", aiResponse.FailureReason ?? "unknown_ai_failure"]);
        }

        var payload = aiResponse.StructuredPayloadJson ?? aiResponse.Content;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new FinancialAdviceAdjudicationResult(
                UsedAi: true,
                Succeeded: false,
                Mode: request.Plan.Mode,
                ModelUsed: route.Model,
                InputTokens: aiResponse.InputTokenEstimate ?? 0,
                OutputTokens: aiResponse.OutputTokenEstimate ?? 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: ["adjudication_empty_payload"]);
        }

        FinancialAdviceAdjudicationStructuredResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<FinancialAdviceAdjudicationStructuredResponse>(payload, SerializerOptions);
        }
        catch
        {
            parsed = null;
        }

        if (parsed is null)
        {
            return new FinancialAdviceAdjudicationResult(
                UsedAi: true,
                Succeeded: false,
                Mode: request.Plan.Mode,
                ModelUsed: route.Model,
                InputTokens: aiResponse.InputTokenEstimate ?? 0,
                OutputTokens: aiResponse.OutputTokenEstimate ?? 0,
                ResponseSummary: null,
                FindingOutcomes: [],
                Warnings: ["adjudication_parse_failed"]);
        }

        var warnings = new List<string>(request.Plan.ReasonCodes.Count + 4);
        warnings.AddRange(request.Plan.ReasonCodes);
        if (parsed.Warnings is { Count: > 0 })
        {
            warnings.AddRange(parsed.Warnings.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        var fallbackOutcome = ParseOutcome(parsed.Outcome);
        var targetIds = targetFindings.Select(finding => finding.FindingId).ToHashSet(StringComparer.Ordinal);
        var outcomes = new List<FinancialAdviceFindingAdjudication>(targetFindings.Length);

        if (parsed.Adjustments is { Count: > 0 })
        {
            foreach (var adjustment in parsed.Adjustments)
            {
                if (string.IsNullOrWhiteSpace(adjustment.FindingId) || !targetIds.Contains(adjustment.FindingId))
                {
                    warnings.Add("adjudication_unknown_finding_ignored");
                    continue;
                }

                var sanitizedSummary = SanitizeRefinedSummary(adjustment.RefinedSummary, request.EvidenceSummary, targetFindings);
                if (!string.Equals(sanitizedSummary, adjustment.RefinedSummary, StringComparison.Ordinal))
                {
                    warnings.Add("adjudication_refined_summary_numbers_sanitized");
                }

                outcomes.Add(new FinancialAdviceFindingAdjudication(
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
            outcomes.Add(new FinancialAdviceFindingAdjudication(
                FindingId: missingId,
                Outcome: fallbackOutcome,
                RefinedSummary: null,
                ConfidenceDelta: null,
                NuanceNotes: [],
                ReasonCode: "fallback_global_outcome"));
        }

        var responseSummary = SanitizeRefinedSummary(parsed.SummaryRefinement, request.EvidenceSummary, targetFindings);
        if (!string.Equals(responseSummary, parsed.SummaryRefinement, StringComparison.Ordinal))
        {
            warnings.Add("adjudication_summary_refinement_sanitized");
        }

        return new FinancialAdviceAdjudicationResult(
            UsedAi: true,
            Succeeded: true,
            Mode: request.Plan.Mode,
            ModelUsed: route.Model,
            InputTokens: aiResponse.InputTokenEstimate ?? 0,
            OutputTokens: aiResponse.OutputTokenEstimate ?? 0,
            ResponseSummary: responseSummary,
            FindingOutcomes: outcomes
                .OrderBy(item => item.FindingId, StringComparer.Ordinal)
                .ToArray(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private FinancialAdviceAdjudicationInputPacket BuildInputPacket(
        FinancialAdviceAdjudicationExecutionRequest request,
        IReadOnlyList<FinancialAdviceFinding> targetFindings)
    {
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
                allowedNumbers.Add(metric.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
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

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
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
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
    }

    private sealed record FinancialAdviceAdjudicationStructuredResponse(
        string? Outcome,
        string? SummaryRefinement,
        IReadOnlyList<FinancialAdviceAdjudicationStructuredAdjustment>? Adjustments,
        IReadOnlyList<string>? Warnings,
        string? Rationale);

    private sealed record FinancialAdviceAdjudicationStructuredAdjustment(
        string? FindingId,
        string? Outcome,
        double? ConfidenceDelta,
        string? RefinedSummary,
        IReadOnlyList<string>? NuanceNotes,
        string? ReasonCode);
}
