using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdviceEngine
{
    IReadOnlyList<FinancialAdviceFinding> ComputeDeterministicFindings(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        DateTime nowUtc);
}

public sealed class FinancialAdviceEngine(
    ExpenseTaxonomyService taxonomyService,
    IOptions<CompanionAdviceOptions> options) : IFinancialAdviceEngine
{
    private readonly CompanionAdviceOptions _options = options.Value;

    private static readonly HashSet<string> ProtectedDomainKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "health",
        "medical",
        "transport",
        "transit",
        "grocer",
        "grocery",
        "childcare",
        "dependent",
        "tax",
        "debt",
        "loan",
        "housing",
        "rent",
        "mortgage",
        "utility"
    };

    private static readonly HashSet<string> DiscretionaryDomainKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "dining",
        "restaurant",
        "entertainment",
        "hobby",
        "shopping",
        "travel",
        "leisure",
        "subscription",
        "lifestyle"
    };

    public IReadOnlyList<FinancialAdviceFinding> ComputeDeterministicFindings(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(context);

        var baseline = BuildBaseline(context.Profile);
        var findings = new List<FinancialAdviceFinding>(8);
        var idSequence = 0;

        var summary = TryGetContext<CompanionFinancialSummaryContext>(context.ToolOutputs, CompanionTool.FinancialSummary.ToOutputKey());
        var spending = TryGetContext<CompanionSpendingAnalysisContext>(context.ToolOutputs, CompanionTool.SpendingAnalysis.ToOutputKey());
        var recurring = TryGetContext<CompanionRecurringObligationsContext>(context.ToolOutputs, CompanionTool.RecurringObligations.ToOutputKey());
        var budget = TryGetContext<CompanionBudgetStatusContext>(context.ToolOutputs, CompanionTool.BudgetStatus.ToOutputKey());

        EvaluateCategoryPressure(routing, summary, spending, baseline, nowUtc, findings, ref idSequence);
        EvaluateRecurringPressure(routing, summary, recurring, baseline, nowUtc, findings, ref idSequence);
        EvaluateBudgetHealth(routing, budget, nowUtc, findings, ref idSequence);
        EvaluateAffordability(routing, summary, recurring, budget, baseline, nowUtc, findings, ref idSequence);
        EvaluatePlanState(routing, summary, baseline, nowUtc, findings, ref idSequence);
        EvaluatePositiveSignals(routing, summary, budget, findings, nowUtc, ref idSequence);

        if (findings.Count == 0)
        {
            if (summary is null && spending is null && recurring is null && budget is null)
            {
                findings.Add(CreateFinding(
                    findingId: NextFindingId(FinancialAdviceFindingType.InsufficientEvidence, ref idSequence),
                    findingType: FinancialAdviceFindingType.InsufficientEvidence,
                    intent: routing.PrimaryIntent,
                    severity: FinancialAdviceSeverity.Info,
                    confidence: 0.35d,
                    evidenceSummary: "Not enough grounded financial inputs were available to compute reliable guidance.",
                    metrics: [],
                    domainCode: null,
                    domainName: null,
                    categoryCode: null,
                    categoryName: null,
                    protectedFlags: [],
                    actions:
                    [
                        new FinancialAdviceActionCandidate(
                            ActionId: "collect_grounding_data",
                            ActionType: FinancialAdviceActionType.ReviewSpend,
                            Title: "Wait for more grounded data",
                            Guidance: "Sync recent transactions and budget data so guidance can be evidence-backed.")
                    ],
                    uncertaintyMarkers: ["missing_core_context_tools"],
                    aiAllowed: false,
                    aiRecommended: false,
                    freshness: BuildFreshness(FinancialAdviceFindingType.InsufficientEvidence, FinancialAdviceSeverity.Info, nowUtc),
                    renderingHints: BuildRenderingHints("insufficient_evidence")));
            }
            else
            {
                findings.Add(CreateFinding(
                    findingId: NextFindingId(FinancialAdviceFindingType.NoMaterialIssueDetected, ref idSequence),
                    findingType: FinancialAdviceFindingType.NoMaterialIssueDetected,
                    intent: routing.PrimaryIntent,
                    severity: FinancialAdviceSeverity.Info,
                    confidence: 0.72d,
                    evidenceSummary: "No material financial pressure signal was detected from your current baseline and recent activity.",
                    metrics: [],
                    domainCode: null,
                    domainName: null,
                    categoryCode: null,
                    categoryName: null,
                    protectedFlags: [],
                    actions:
                    [
                        new FinancialAdviceActionCandidate(
                            ActionId: "maintain_course",
                            ActionType: FinancialAdviceActionType.KeepCourse,
                            Title: "Stay on your current path",
                            Guidance: "Keep monitoring weekly to catch changes early.")
                    ],
                    uncertaintyMarkers: [],
                    aiAllowed: false,
                    aiRecommended: false,
                    freshness: BuildFreshness(FinancialAdviceFindingType.NoMaterialIssueDetected, FinancialAdviceSeverity.Info, nowUtc),
                    renderingHints: BuildRenderingHints("no_material_issue")));
            }
        }

        return findings
            .OrderByDescending(finding => finding.PriorityScore)
            .ThenByDescending(finding => finding.Confidence)
            .ToArray();
    }

    private void EvaluateCategoryPressure(
        CompanionIntentRoutingResult routing,
        CompanionFinancialSummaryContext? summary,
        CompanionSpendingAnalysisContext? spending,
        CompanionProfileBaseline baseline,
        DateTime nowUtc,
        List<FinancialAdviceFinding> findings,
        ref int idSequence)
    {
        if (spending is null || spending.TopDomainSpend.Count == 0)
        {
            return;
        }

        var totalSpend = summary?.SpendLast30Days ?? spending.TopDomainSpend.Sum(x => x.Amount);
        foreach (var domain in spending.TopDomainSpend.Take(3))
        {
            var currentAmount = Math.Abs(domain.Amount);
            if (currentAmount <= 0m)
            {
                continue;
            }

            var domainName = taxonomyService.GetDomainName(domain.DomainCode);
            var baselineAmount = baseline.BaselineSpendByDomain.GetValueOrDefault(domain.DomainCode);
            var hasBaseline = baselineAmount > 0m;
            var ratio = hasBaseline
                ? currentAmount / baselineAmount
                : totalSpend > 0m
                    ? currentAmount / totalSpend
                    : 0m;
            var delta = hasBaseline ? currentAmount - baselineAmount : currentAmount;
            var concentration = totalSpend > 0m ? currentAmount / totalSpend : 0m;
            var isProtectedDomain = IsProtectedDomain(domain.DomainCode, domainName);
            var isDiscretionary = IsDiscretionaryDomain(domainName);

            var thresholdHit = hasBaseline
                ? ratio >= _options.CategoryPressureIncreaseRatioThreshold
                  && delta >= _options.CategoryPressureAbsoluteDeltaThreshold
                : concentration >= 0.35m && currentAmount >= _options.MaterialSpendThreshold;
            if (!thresholdHit)
            {
                continue;
            }

            var severity = ratio >= 1.55m || concentration >= 0.5m
                ? FinancialAdviceSeverity.High
                : FinancialAdviceSeverity.Moderate;
            var confidence = hasBaseline ? 0.82d : 0.64d;
            var uncertainty = hasBaseline
                ? Array.Empty<string>()
                : new[] { "missing_category_specific_baseline" };

            var actions = new List<FinancialAdviceActionCandidate>(2);
            if (isProtectedDomain)
            {
                actions.Add(new FinancialAdviceActionCandidate(
                    ActionId: $"review_{domain.DomainCode}_usage",
                    ActionType: FinancialAdviceActionType.ReviewSpend,
                    Title: "Review billing and usage changes",
                    Guidance: "This category appears essential. Focus on billing accuracy, duplicate charges, or provider plans before reductions.",
                    TargetDomainCode: domain.DomainCode,
                    IsProtectedCategory: true));
            }
            else
            {
                var suggestedCutRatio = decimal.Clamp((ratio - 1m) / 2m, 0.05m, 0.20m);
                actions.Add(new FinancialAdviceActionCandidate(
                    ActionId: $"reduce_{domain.DomainCode}_spend",
                    ActionType: FinancialAdviceActionType.ReduceSpend,
                    Title: "Set a temporary cap",
                    Guidance: "Apply a temporary cap for this category and review results over the next two weeks.",
                    SuggestedMagnitude: decimal.Round(suggestedCutRatio, 2, MidpointRounding.AwayFromZero),
                    TargetDomainCode: domain.DomainCode,
                    IsProtectedCategory: false));
                actions.Add(new FinancialAdviceActionCandidate(
                    ActionId: $"track_{domain.DomainCode}_trend",
                    ActionType: FinancialAdviceActionType.ReviewSpend,
                    Title: "Track weekly trend",
                    Guidance: "Track this category weekly against your own baseline to confirm whether pressure is easing.",
                    TargetDomainCode: domain.DomainCode));
            }

            var findingType = isDiscretionary
                ? FinancialAdviceFindingType.DiscretionaryOverspend
                : FinancialAdviceFindingType.CategoryPressure;
            var finding = CreateFinding(
                findingId: NextFindingId(findingType, ref idSequence),
                findingType: findingType,
                intent: routing.PrimaryIntent,
                severity: severity,
                confidence: confidence,
                evidenceSummary: hasBaseline
                    ? $"{domainName ?? "Category"} spend is {FormatRatio(ratio)} vs your own baseline ({FormatCurrency(delta, summary?.Currency)} above baseline)."
                    : $"{domainName ?? "Category"} accounts for {FormatPercentage(concentration)} of tracked spending and stands out as a concentration pressure point.",
                metrics:
                [
                    new FinancialAdviceEvidenceMetric("currentDomainSpend", currentAmount, summary?.Currency ?? "currency"),
                    new FinancialAdviceEvidenceMetric("baselineDomainSpend", baselineAmount, summary?.Currency ?? "currency"),
                    new FinancialAdviceEvidenceMetric("domainSpendRatio", ratio, "ratio"),
                    new FinancialAdviceEvidenceMetric("domainSpendConcentration", concentration, "ratio")
                ],
                domainCode: domain.DomainCode,
                domainName: domainName,
                categoryCode: null,
                categoryName: null,
                protectedFlags: isProtectedDomain ? ["protected_or_essential_domain"] : [],
                actions: actions,
                uncertaintyMarkers: uncertainty,
                aiAllowed: true,
                aiRecommended: confidence < _options.HighConfidenceSkipThreshold || severity >= FinancialAdviceSeverity.High,
                freshness: BuildFreshness(findingType, severity, nowUtc),
                renderingHints: BuildRenderingHints("category_pressure"));
            findings.Add(finding);

            if (findingType == FinancialAdviceFindingType.DiscretionaryOverspend)
            {
                break;
            }
        }
    }

    private void EvaluateRecurringPressure(
        CompanionIntentRoutingResult routing,
        CompanionFinancialSummaryContext? summary,
        CompanionRecurringObligationsContext? recurring,
        CompanionProfileBaseline baseline,
        DateTime nowUtc,
        List<FinancialAdviceFinding> findings,
        ref int idSequence)
    {
        if (recurring is null || recurring.EstimatedMonthlyTotal <= 0m)
        {
            return;
        }

        var recurringTotal = recurring.EstimatedMonthlyTotal;
        var baselineRecurring = baseline.BaselineRecurringMonthlyTotal;
        var hasBaselineRecurring = baselineRecurring > 0m;
        var ratio = hasBaselineRecurring ? recurringTotal / baselineRecurring : 0m;
        var delta = hasBaselineRecurring ? recurringTotal - baselineRecurring : recurringTotal;
        var income = summary?.IncomeLast30Days ?? 0m;
        var recurringToIncome = income > 0m ? recurringTotal / income : 0m;

        var hasPressure = hasBaselineRecurring
            ? ratio >= _options.RecurringPressureIncreaseRatioThreshold
              && delta >= _options.RecurringPressureAbsoluteDeltaThreshold
            : recurringToIncome >= _options.RecurringToIncomePressureRatioThreshold
              && recurringTotal >= _options.MaterialSpendThreshold;
        if (!hasPressure)
        {
            return;
        }

        var protectedFlags = recurring.TopItems.Any(item => IsProtectedRecurringName(item.Name))
            ? new[] { "contains_essential_or_obligatory_recurring_charges" }
            : Array.Empty<string>();
        var severity = recurringToIncome >= 0.65m || ratio >= 1.35m
            ? FinancialAdviceSeverity.High
            : FinancialAdviceSeverity.Moderate;
        var confidence = hasBaselineRecurring ? 0.78d : 0.69d;
        var uncertainty = hasBaselineRecurring ? Array.Empty<string>() : new[] { "missing_recurring_baseline" };

        var actions = new List<FinancialAdviceActionCandidate>(2)
        {
            new(
                ActionId: "review_recurring_charges",
                ActionType: FinancialAdviceActionType.TrackRecurringCharge,
                Title: "Review recurring charges",
                Guidance: "Check recurring charges for duplicates, outdated subscriptions, and negotiable plans."),
            new(
                ActionId: "sequence_recurring_schedule",
                ActionType: FinancialAdviceActionType.BuildBuffer,
                Title: "Align bill timing with cashflow",
                Guidance: "Where possible, align recurring charge dates with income timing to reduce pressure spikes.")
        };

        findings.Add(CreateFinding(
            findingId: NextFindingId(FinancialAdviceFindingType.RecurringSpendPressure, ref idSequence),
            findingType: FinancialAdviceFindingType.RecurringSpendPressure,
            intent: routing.PrimaryIntent,
            severity: severity,
            confidence: confidence,
            evidenceSummary: hasBaselineRecurring
                ? $"Recurring monthly commitments are {FormatRatio(ratio)} vs your historical recurring baseline."
                : $"Recurring commitments are taking about {FormatPercentage(recurringToIncome)} of recent monthly income.",
            metrics:
            [
                new FinancialAdviceEvidenceMetric("recurringMonthlyTotal", recurringTotal, summary?.Currency ?? "currency"),
                new FinancialAdviceEvidenceMetric("baselineRecurringMonthlyTotal", baselineRecurring, summary?.Currency ?? "currency"),
                new FinancialAdviceEvidenceMetric("recurringToIncomeRatio", recurringToIncome, "ratio"),
                new FinancialAdviceEvidenceMetric("recurringIncreaseRatio", ratio, "ratio")
            ],
            domainCode: null,
            domainName: null,
            categoryCode: null,
            categoryName: null,
            protectedFlags: protectedFlags,
            actions: actions,
            uncertaintyMarkers: uncertainty,
            aiAllowed: true,
            aiRecommended: severity >= FinancialAdviceSeverity.High || confidence < _options.HighConfidenceSkipThreshold,
            freshness: BuildFreshness(FinancialAdviceFindingType.RecurringSpendPressure, severity, nowUtc),
            renderingHints: BuildRenderingHints("recurring_pressure")));
    }

    private void EvaluateBudgetHealth(
        CompanionIntentRoutingResult routing,
        CompanionBudgetStatusContext? budget,
        DateTime nowUtc,
        List<FinancialAdviceFinding> findings,
        ref int idSequence)
    {
        if (budget is null || !budget.HasBudgetPlan || !budget.MonthlyBudget.HasValue || budget.MonthlyBudget <= 0m)
        {
            return;
        }

        var monthlyBudget = budget.MonthlyBudget.Value;
        var remaining = budget.RemainingBudget ?? (monthlyBudget - budget.MonthToDateSpend);
        var spendRatio = budget.MonthToDateSpend / monthlyBudget;
        var day = nowUtc.Day;
        var daysInMonth = DateTime.DaysInMonth(nowUtc.Year, nowUtc.Month);
        var monthProgress = daysInMonth > 0 ? (decimal)day / daysInMonth : 0.5m;

        if (remaining < 0m || spendRatio >= monthProgress + _options.BudgetSlippageRatioThreshold)
        {
            var severity = remaining < 0m ? FinancialAdviceSeverity.High : FinancialAdviceSeverity.Moderate;
            findings.Add(CreateFinding(
                findingId: NextFindingId(FinancialAdviceFindingType.BudgetSlippage, ref idSequence),
                findingType: FinancialAdviceFindingType.BudgetSlippage,
                intent: routing.PrimaryIntent,
                severity: severity,
                confidence: 0.88d,
                evidenceSummary: remaining < 0m
                    ? $"You are currently {FormatCurrency(Math.Abs(remaining), "currency")} over your active monthly budget."
                    : $"Month-to-date spend is ahead of budget pacing ({FormatPercentage(spendRatio)} spent with {FormatPercentage(monthProgress)} of month elapsed).",
                metrics:
                [
                    new FinancialAdviceEvidenceMetric("monthlyBudget", monthlyBudget, "currency"),
                    new FinancialAdviceEvidenceMetric("monthToDateSpend", budget.MonthToDateSpend, "currency"),
                    new FinancialAdviceEvidenceMetric("remainingBudget", remaining, "currency"),
                    new FinancialAdviceEvidenceMetric("budgetSpendRatio", spendRatio, "ratio"),
                    new FinancialAdviceEvidenceMetric("monthProgressRatio", monthProgress, "ratio")
                ],
                domainCode: null,
                domainName: null,
                categoryCode: null,
                categoryName: null,
                protectedFlags: [],
                actions:
                [
                    new FinancialAdviceActionCandidate(
                        ActionId: "rebalance_monthly_budget",
                        ActionType: FinancialAdviceActionType.AdjustBudget,
                        Title: "Rebalance the monthly budget",
                        Guidance: "Rebalance discretionary categories first and protect essentials while recovering plan alignment."),
                    new FinancialAdviceActionCandidate(
                        ActionId: "pause_nonessential_spend_short_term",
                        ActionType: FinancialAdviceActionType.ReduceSpend,
                        Title: "Pause non-essential spend briefly",
                        Guidance: "Use a short pause on non-essential spend to stabilize this month's budget.",
                        SuggestedMagnitude: 0.10m)
                ],
                uncertaintyMarkers: [],
                aiAllowed: true,
                aiRecommended: true,
                freshness: BuildFreshness(FinancialAdviceFindingType.BudgetSlippage, severity, nowUtc),
                renderingHints: BuildRenderingHints("budget_slippage")));
            return;
        }

        var remainingRatio = remaining / monthlyBudget;
        if (remainingRatio <= _options.BudgetLowRemainingRatioThreshold && monthProgress <= 0.75m)
        {
            findings.Add(CreateFinding(
                findingId: NextFindingId(FinancialAdviceFindingType.BudgetSlippage, ref idSequence),
                findingType: FinancialAdviceFindingType.BudgetSlippage,
                intent: routing.PrimaryIntent,
                severity: FinancialAdviceSeverity.Low,
                confidence: 0.78d,
                evidenceSummary: $"Remaining budget is down to {FormatPercentage(remainingRatio)} with part of the month still ahead.",
                metrics:
                [
                    new FinancialAdviceEvidenceMetric("remainingBudgetRatio", remainingRatio, "ratio"),
                    new FinancialAdviceEvidenceMetric("monthProgressRatio", monthProgress, "ratio")
                ],
                domainCode: null,
                domainName: null,
                categoryCode: null,
                categoryName: null,
                protectedFlags: [],
                actions:
                [
                    new FinancialAdviceActionCandidate(
                        ActionId: "tighten_discretionary_budget",
                        ActionType: FinancialAdviceActionType.AdjustBudget,
                        Title: "Tighten discretionary budget",
                        Guidance: "Tighten flexible categories to preserve room for essentials.")
                ],
                uncertaintyMarkers: [],
                aiAllowed: true,
                aiRecommended: false,
                freshness: BuildFreshness(FinancialAdviceFindingType.BudgetSlippage, FinancialAdviceSeverity.Low, nowUtc),
                renderingHints: BuildRenderingHints("budget_watch")));
        }
    }

    private void EvaluateAffordability(
        CompanionIntentRoutingResult routing,
        CompanionFinancialSummaryContext? summary,
        CompanionRecurringObligationsContext? recurring,
        CompanionBudgetStatusContext? budget,
        CompanionProfileBaseline baseline,
        DateTime nowUtc,
        List<FinancialAdviceFinding> findings,
        ref int idSequence)
    {
        if (summary is null)
        {
            return;
        }

        var recurringMonthly = recurring?.EstimatedMonthlyTotal ?? baseline.BaselineRecurringMonthlyTotal;
        var income = summary.IncomeLast30Days;
        var net = summary.NetLast30Days;
        var affordabilityRoom = net - recurringMonthly;
        var roomToIncome = income > 0m ? affordabilityRoom / income : 0m;
        var budgetRemaining = budget?.RemainingBudget ?? 0m;

        if (net < 0m || affordabilityRoom < 0m || roomToIncome < _options.AffordabilityBufferRatioThreshold)
        {
            var severity = net < 0m || affordabilityRoom < 0m
                ? FinancialAdviceSeverity.High
                : FinancialAdviceSeverity.Moderate;
            findings.Add(CreateFinding(
                findingId: NextFindingId(FinancialAdviceFindingType.AffordabilityRisk, ref idSequence),
                findingType: FinancialAdviceFindingType.AffordabilityRisk,
                intent: routing.PrimaryIntent,
                severity: severity,
                confidence: 0.86d,
                evidenceSummary: net < 0m
                    ? "Recent monthly net cashflow is negative, which indicates affordability pressure."
                    : $"Affordability room after recurring commitments is limited ({FormatPercentage(roomToIncome)} of income).",
                metrics:
                [
                    new FinancialAdviceEvidenceMetric("incomeLast30Days", income, summary.Currency),
                    new FinancialAdviceEvidenceMetric("netLast30Days", net, summary.Currency),
                    new FinancialAdviceEvidenceMetric("recurringMonthly", recurringMonthly, summary.Currency),
                    new FinancialAdviceEvidenceMetric("affordabilityRoom", affordabilityRoom, summary.Currency),
                    new FinancialAdviceEvidenceMetric("affordabilityRoomToIncomeRatio", roomToIncome, "ratio"),
                    new FinancialAdviceEvidenceMetric("budgetRemaining", budgetRemaining, summary.Currency)
                ],
                domainCode: null,
                domainName: null,
                categoryCode: null,
                categoryName: null,
                protectedFlags: [],
                actions:
                [
                    new FinancialAdviceActionCandidate(
                        ActionId: "build_affordability_buffer",
                        ActionType: FinancialAdviceActionType.BuildBuffer,
                        Title: "Rebuild affordability buffer",
                        Guidance: "Prioritize preserving essentials and rebuild a small monthly buffer before discretionary increases."),
                    new FinancialAdviceActionCandidate(
                        ActionId: "sequence_large_purchases",
                        ActionType: FinancialAdviceActionType.ReviewSpend,
                        Title: "Sequence large discretionary purchases",
                        Guidance: "Sequence larger discretionary purchases only after buffer recovery.")
                ],
                uncertaintyMarkers: [],
                aiAllowed: true,
                aiRecommended: true,
                freshness: BuildFreshness(FinancialAdviceFindingType.AffordabilityRisk, severity, nowUtc),
                renderingHints: BuildRenderingHints("affordability_risk")));
        }
    }

    private void EvaluatePlanState(
        CompanionIntentRoutingResult routing,
        CompanionFinancialSummaryContext? summary,
        CompanionProfileBaseline baseline,
        DateTime nowUtc,
        List<FinancialAdviceFinding> findings,
        ref int idSequence)
    {
        if (summary is null || baseline.ActivePlanExpectedSpendTotal <= 0m)
        {
            return;
        }

        var planSpendTarget = baseline.ActivePlanExpectedSpendTotal;
        var actualSpend = summary.SpendLast30Days;
        var ratio = planSpendTarget > 0m ? actualSpend / planSpendTarget : 0m;
        var hasDrift = ratio > 1.10m;
        var hasProgress = ratio < 0.90m && summary.NetLast30Days > 0m;

        if (hasDrift)
        {
            findings.Add(CreateFinding(
                findingId: NextFindingId(FinancialAdviceFindingType.PlanDrift, ref idSequence),
                findingType: FinancialAdviceFindingType.PlanDrift,
                intent: routing.PrimaryIntent,
                severity: FinancialAdviceSeverity.Moderate,
                confidence: 0.76d,
                evidenceSummary: $"Recent spending is {FormatRatio(ratio)} versus active plan targets, indicating drift.",
                metrics:
                [
                    new FinancialAdviceEvidenceMetric("activePlanExpectedSpend", planSpendTarget, summary.Currency),
                    new FinancialAdviceEvidenceMetric("actualSpendLast30Days", actualSpend, summary.Currency),
                    new FinancialAdviceEvidenceMetric("planSpendRatio", ratio, "ratio"),
                    new FinancialAdviceEvidenceMetric("activePlanCount", baseline.ActivePlanCount, "count")
                ],
                domainCode: null,
                domainName: null,
                categoryCode: null,
                categoryName: null,
                protectedFlags: [],
                actions:
                [
                    new FinancialAdviceActionCandidate(
                        ActionId: "review_active_plan_targets",
                        ActionType: FinancialAdviceActionType.ReviewPlan,
                        Title: "Review active plan targets",
                        Guidance: "Review target assumptions and adjust plan lines that no longer reflect current spending.")
                ],
                uncertaintyMarkers: [],
                aiAllowed: true,
                aiRecommended: true,
                freshness: BuildFreshness(FinancialAdviceFindingType.PlanDrift, FinancialAdviceSeverity.Moderate, nowUtc),
                renderingHints: BuildRenderingHints("plan_drift")));
            return;
        }

        if (hasProgress)
        {
            findings.Add(CreateFinding(
                findingId: NextFindingId(FinancialAdviceFindingType.PositiveProgress, ref idSequence),
                findingType: FinancialAdviceFindingType.PositiveProgress,
                intent: routing.PrimaryIntent,
                severity: FinancialAdviceSeverity.Low,
                confidence: 0.74d,
                evidenceSummary: "Spending is currently below active plan targets while net cashflow remains positive.",
                metrics:
                [
                    new FinancialAdviceEvidenceMetric("activePlanExpectedSpend", planSpendTarget, summary.Currency),
                    new FinancialAdviceEvidenceMetric("actualSpendLast30Days", actualSpend, summary.Currency),
                    new FinancialAdviceEvidenceMetric("planSpendRatio", ratio, "ratio"),
                    new FinancialAdviceEvidenceMetric("netLast30Days", summary.NetLast30Days, summary.Currency)
                ],
                domainCode: null,
                domainName: null,
                categoryCode: null,
                categoryName: null,
                protectedFlags: [],
                actions:
                [
                    new FinancialAdviceActionCandidate(
                        ActionId: "lock_in_plan_progress",
                        ActionType: FinancialAdviceActionType.KeepCourse,
                        Title: "Lock in recent progress",
                        Guidance: "Keep the current plan discipline and continue weekly checks to sustain progress.")
                ],
                uncertaintyMarkers: [],
                aiAllowed: true,
                aiRecommended: false,
                freshness: BuildFreshness(FinancialAdviceFindingType.PositiveProgress, FinancialAdviceSeverity.Low, nowUtc),
                renderingHints: BuildRenderingHints("plan_progress")));
        }
    }

    private void EvaluatePositiveSignals(
        CompanionIntentRoutingResult routing,
        CompanionFinancialSummaryContext? summary,
        CompanionBudgetStatusContext? budget,
        List<FinancialAdviceFinding> findings,
        DateTime nowUtc,
        ref int idSequence)
    {
        if (summary is null || budget is null || !budget.HasBudgetPlan || findings.Any(finding => finding.Severity >= FinancialAdviceSeverity.Moderate))
        {
            return;
        }

        if ((budget.RemainingBudget ?? 0m) > 0m && summary.NetLast30Days > 0m)
        {
            var positive = CreateFinding(
                findingId: NextFindingId(FinancialAdviceFindingType.PositiveProgress, ref idSequence),
                findingType: FinancialAdviceFindingType.PositiveProgress,
                intent: routing.PrimaryIntent,
                severity: FinancialAdviceSeverity.Info,
                confidence: 0.70d,
                evidenceSummary: "Current signals show a positive direction: budget remains positive and net cashflow is above zero.",
                metrics:
                [
                    new FinancialAdviceEvidenceMetric("remainingBudget", budget.RemainingBudget ?? 0m, summary.Currency),
                    new FinancialAdviceEvidenceMetric("netLast30Days", summary.NetLast30Days, summary.Currency)
                ],
                domainCode: null,
                domainName: null,
                categoryCode: null,
                categoryName: null,
                protectedFlags: [],
                actions:
                [
                    new FinancialAdviceActionCandidate(
                        ActionId: "continue_current_trajectory",
                        ActionType: FinancialAdviceActionType.KeepCourse,
                        Title: "Continue current trajectory",
                        Guidance: "Maintain current spending patterns and run a weekly variance check.")
                ],
                uncertaintyMarkers: [],
                aiAllowed: false,
                aiRecommended: false,
                freshness: BuildFreshness(FinancialAdviceFindingType.PositiveProgress, FinancialAdviceSeverity.Info, nowUtc),
                renderingHints: BuildRenderingHints("positive_progress"));

            findings.Add(positive);
        }
    }

    private FinancialAdviceFinding CreateFinding(
        string findingId,
        FinancialAdviceFindingType findingType,
        FinancialCompanionIntent intent,
        FinancialAdviceSeverity severity,
        double confidence,
        string evidenceSummary,
        IReadOnlyList<FinancialAdviceEvidenceMetric> metrics,
        int? domainCode,
        string? domainName,
        int? categoryCode,
        string? categoryName,
        IReadOnlyList<string> protectedFlags,
        IReadOnlyList<FinancialAdviceActionCandidate> actions,
        IReadOnlyList<string> uncertaintyMarkers,
        bool aiAllowed,
        bool aiRecommended,
        FinancialAdviceFreshnessMetadata freshness,
        IReadOnlyDictionary<string, string> renderingHints)
    {
        var priority = ResolvePriority(severity, confidence);
        return new FinancialAdviceFinding(
            FindingId: findingId,
            FindingType: findingType,
            RelatedIntent: intent,
            Severity: severity,
            PriorityScore: priority,
            Confidence: Math.Clamp(confidence, 0d, 1d),
            EvidenceSummary: evidenceSummary,
            SupportingMetrics: metrics,
            DomainCode: domainCode,
            DomainName: domainName,
            CategoryCode: categoryCode,
            CategoryName: categoryName,
            ProtectedCategoryFlags: protectedFlags,
            RecommendedActions: actions,
            UncertaintyMarkers: uncertaintyMarkers,
            PolicyWarnings: [],
            PolicyExclusions: [],
            AiAdjudicationAllowed: aiAllowed,
            AiAdjudicationRecommended: aiRecommended,
            Freshness: freshness,
            RenderingHints: renderingHints);
    }

    private FinancialAdviceFreshnessMetadata BuildFreshness(
        FinancialAdviceFindingType findingType,
        FinancialAdviceSeverity severity,
        DateTime computedAtUtc)
    {
        var windowHours = severity switch
        {
            FinancialAdviceSeverity.Critical => _options.BaseFreshnessHoursHighSeverity,
            FinancialAdviceSeverity.High => _options.BaseFreshnessHoursHighSeverity,
            FinancialAdviceSeverity.Moderate => _options.BaseFreshnessHoursModerateSeverity,
            FinancialAdviceSeverity.Low => _options.BaseFreshnessHoursLowSeverity,
            _ => _options.BaseFreshnessHoursInfoSeverity
        };

        var evidenceStartUtc = findingType switch
        {
            FinancialAdviceFindingType.RecurringSpendPressure => computedAtUtc.AddDays(-120),
            FinancialAdviceFindingType.CategoryPressure => computedAtUtc.AddDays(-60),
            FinancialAdviceFindingType.DiscretionaryOverspend => computedAtUtc.AddDays(-60),
            FinancialAdviceFindingType.BudgetSlippage => new DateTime(computedAtUtc.Year, computedAtUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => computedAtUtc.AddDays(-30)
        };

        var freshUntilUtc = computedAtUtc.AddHours(Math.Max(6, windowHours));
        var recheckAfterUtc = computedAtUtc.AddHours(Math.Max(3, windowHours / 2));
        var confidenceDecayPerDay = Math.Round(1d / Math.Max(1d, windowHours / 24d), 4, MidpointRounding.AwayFromZero);
        var relevanceScore = severity switch
        {
            FinancialAdviceSeverity.Critical => 0.95d,
            FinancialAdviceSeverity.High => 0.85d,
            FinancialAdviceSeverity.Moderate => 0.72d,
            FinancialAdviceSeverity.Low => 0.55d,
            _ => 0.42d
        };

        return new FinancialAdviceFreshnessMetadata(
            ComputedAtUtc: computedAtUtc,
            EvidencePeriodStartUtc: evidenceStartUtc,
            EvidencePeriodEndUtc: computedAtUtc,
            FreshUntilUtc: freshUntilUtc,
            RecheckAfterUtc: recheckAfterUtc,
            FreshnessState: FinancialAdviceFreshnessState.Fresh,
            ConfidenceDecayPerDay: confidenceDecayPerDay,
            RelevanceScore: relevanceScore,
            RequiresRecheck: true,
            InvalidationHints: ResolveInvalidationHints(findingType));
    }

    private static IReadOnlyList<string> ResolveInvalidationHints(FinancialAdviceFindingType findingType)
    {
        return findingType switch
        {
            FinancialAdviceFindingType.CategoryPressure or FinancialAdviceFindingType.DiscretionaryOverspend =>
            [
                "category_spend_materially_changed",
                "category_baseline_changed",
                "supporting_period_rolled_over",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.RecurringSpendPressure =>
            [
                "recurring_commitments_changed",
                "recurring_amount_materially_changed",
                "supporting_period_rolled_over",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.BudgetSlippage =>
            [
                "budget_state_materially_changed",
                "budget_plan_changed",
                "month_boundary_rollover",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.AffordabilityRisk =>
            [
                "income_or_spend_materially_changed",
                "required_payments_changed",
                "budget_state_materially_changed",
                "stale_age_exceeded"
            ],
            FinancialAdviceFindingType.PlanDrift or FinancialAdviceFindingType.PositiveProgress =>
            [
                "plan_state_materially_changed",
                "plan_targets_changed",
                "supporting_period_rolled_over",
                "stale_age_exceeded"
            ],
            _ =>
            [
                "supporting_data_changed",
                "stale_age_exceeded"
            ]
        };
    }

    private IReadOnlyDictionary<string, string> BuildRenderingHints(string family)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["insightFamily"] = family,
            ["surface"] = "key_insight_or_chat",
            ["tone"] = "supportive",
            ["scope"] = "financial_guidance"
        };
    }

    private static int ResolvePriority(FinancialAdviceSeverity severity, double confidence)
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

    private bool IsProtectedDomain(int? domainCode, string? domainName)
    {
        if (domainCode.HasValue)
        {
            var resolvedName = taxonomyService.GetDomainName(domainCode.Value);
            if (!string.IsNullOrWhiteSpace(resolvedName) && IsNameMatch(resolvedName, ProtectedDomainKeywords))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(domainName) && IsNameMatch(domainName, ProtectedDomainKeywords))
        {
            return true;
        }

        return false;
    }

    private static bool IsDiscretionaryDomain(string? domainName)
    {
        return !string.IsNullOrWhiteSpace(domainName)
               && IsNameMatch(domainName, DiscretionaryDomainKeywords);
    }

    private static bool IsNameMatch(string name, IReadOnlyCollection<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var lower = name.Trim().ToLowerInvariant();
        foreach (var keyword in keywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProtectedRecurringName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("rent", StringComparison.OrdinalIgnoreCase)
               || value.Contains("mortgage", StringComparison.OrdinalIgnoreCase)
               || value.Contains("loan", StringComparison.OrdinalIgnoreCase)
               || value.Contains("tax", StringComparison.OrdinalIgnoreCase)
               || value.Contains("insurance", StringComparison.OrdinalIgnoreCase);
    }

    private static TContext? TryGetContext<TContext>(
        IReadOnlyDictionary<string, object?> toolOutputs,
        string key) where TContext : class
    {
        if (toolOutputs.TryGetValue(key, out var obj) && obj is TContext typed)
        {
            return typed;
        }

        return null;
    }

    private static string NextFindingId(FinancialAdviceFindingType type, ref int sequence)
    {
        sequence += 1;
        return $"{type.ToString().ToLowerInvariant()}_{sequence.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatRatio(decimal ratio)
    {
        return $"{Math.Round(ratio, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)}x";
    }

    private static string FormatPercentage(decimal ratio)
    {
        return (ratio * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatCurrency(decimal value, string? currency)
    {
        var prefix = string.IsNullOrWhiteSpace(currency) ? string.Empty : $"{currency} ";
        return prefix + Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static CompanionProfileBaseline BuildBaseline(UserFinancialContextSnapshot profile)
    {
        var baselineSpendByDomain = ParseSpendByDomain(profile.SpendingTendenciesJson);
        var baselineAvgDailySpend = ParseDecimalProperty(profile.SpendingTendenciesJson, "averageDailySpend");
        var baselineRecurring = ParseRecurringMonthlyTotal(profile.KnownObligationsJson);
        var planState = ParseActivePlans(profile.ActivePlansJson);
        var protectedPreferences = ParseProtectedPreferenceHints(profile.CategoryFlexibilityMarkersJson);

        return new CompanionProfileBaseline(
            BaselineSpendByDomain: baselineSpendByDomain,
            BaselineAverageDailySpend: baselineAvgDailySpend,
            BaselineRecurringMonthlyTotal: baselineRecurring,
            ActivePlanExpectedSpendTotal: planState.TotalExpectedSpend,
            ActivePlanCount: planState.PlanCount,
            ProtectedPreferenceHints: protectedPreferences);
    }

    private static Dictionary<int, decimal> ParseSpendByDomain(string? json)
    {
        if (!TryParseJsonObject(json, out var root))
        {
            return [];
        }

        if (!TryGetPropertyCaseInsensitive(root, "spendByDomain", out var spendByDomain)
            || spendByDomain.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<int, decimal>();
        foreach (var property in spendByDomain.EnumerateObject())
        {
            if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var domainCode))
            {
                continue;
            }

            if (TryReadDecimal(property.Value, out var amount) && amount > 0m)
            {
                result[domainCode] = amount;
            }
        }

        return result;
    }

    private static decimal? ParseDecimalProperty(string? json, string propertyName)
    {
        if (!TryParseJsonObject(json, out var root))
        {
            return null;
        }

        if (!TryGetPropertyCaseInsensitive(root, propertyName, out var property))
        {
            return null;
        }

        return TryReadDecimal(property, out var value) ? value : null;
    }

    private static decimal ParseRecurringMonthlyTotal(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0m;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return 0m;
            }

            decimal total = 0m;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!TryGetPropertyCaseInsensitive(item, "amount", out var amountElement)
                    || !TryReadDecimal(amountElement, out var amount))
                {
                    continue;
                }

                var frequencyDays = 30m;
                if (TryGetPropertyCaseInsensitive(item, "frequencyDays", out var frequencyElement)
                    && TryReadDecimal(frequencyElement, out var parsedFrequency)
                    && parsedFrequency > 0m)
                {
                    frequencyDays = parsedFrequency;
                }

                total += amount * (30m / frequencyDays);
            }

            return Math.Round(total, 2, MidpointRounding.AwayFromZero);
        }
        catch
        {
            return 0m;
        }
    }

    private static ParsedPlanState ParseActivePlans(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParsedPlanState(0m, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new ParsedPlanState(0m, 0);
            }

            var total = 0m;
            var count = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (TryGetPropertyCaseInsensitive(item, "expectedSpendTotal", out var valueElement)
                    && TryReadDecimal(valueElement, out var expected))
                {
                    total += Math.Abs(expected);
                }

                count += 1;
            }

            return new ParsedPlanState(
                TotalExpectedSpend: Math.Round(total, 2, MidpointRounding.AwayFromZero),
                PlanCount: count);
        }
        catch
        {
            return new ParsedPlanState(0m, 0);
        }
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
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var values = new List<string>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            values.Add(text.Trim());
                        }
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        if (TryGetPropertyCaseInsensitive(item, "tag", out var tagElement)
                            && tagElement.ValueKind == JsonValueKind.String)
                        {
                            var tag = tagElement.GetString();
                            if (!string.IsNullOrWhiteSpace(tag))
                            {
                                values.Add(tag.Trim());
                            }
                        }
                    }
                }

                return values;
            }

            return [];
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParseJsonObject(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
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
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
    }

    private sealed record CompanionProfileBaseline(
        IReadOnlyDictionary<int, decimal> BaselineSpendByDomain,
        decimal? BaselineAverageDailySpend,
        decimal BaselineRecurringMonthlyTotal,
        decimal ActivePlanExpectedSpendTotal,
        int ActivePlanCount,
        IReadOnlyList<string> ProtectedPreferenceHints);

    private sealed record ParsedPlanState(
        decimal TotalExpectedSpend,
        int PlanCount);
}
