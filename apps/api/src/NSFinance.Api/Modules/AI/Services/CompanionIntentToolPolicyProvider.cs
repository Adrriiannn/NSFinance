using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionIntentToolPolicyProvider
{
    CompanionIntentToolPolicy Resolve(FinancialCompanionIntent intent);
}

public interface ICompanionMixedIntentMergePolicy
{
    CompanionMixedIntentMergeResult Merge(
        FinancialCompanionIntent primaryIntent,
        IReadOnlyList<FinancialCompanionIntent> secondaryIntents);
}

public sealed class CompanionIntentToolPolicyProvider : ICompanionIntentToolPolicyProvider
{
    private static readonly IReadOnlyDictionary<FinancialCompanionIntent, CompanionIntentToolPolicy> PolicyByIntent
        = new Dictionary<FinancialCompanionIntent, CompanionIntentToolPolicy>
        {
            [FinancialCompanionIntent.SpendingAnalysis] = new(
                FinancialCompanionIntent.SpendingAnalysis,
                RequiredTools: [CompanionTool.FinancialSummary, CompanionTool.SpendingAnalysis],
                OptionalTools: [CompanionTool.BudgetStatus],
                DisallowedTools: []),
            [FinancialCompanionIntent.SavingsCutbackAdvice] = new(
                FinancialCompanionIntent.SavingsCutbackAdvice,
                RequiredTools: [CompanionTool.FinancialSummary, CompanionTool.SpendingAnalysis, CompanionTool.RecurringObligations],
                OptionalTools: [CompanionTool.BudgetStatus],
                DisallowedTools: []),
            [FinancialCompanionIntent.Affordability] = new(
                FinancialCompanionIntent.Affordability,
                RequiredTools: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                OptionalTools: [CompanionTool.TransactionQuery, CompanionTool.RecurringObligations],
                DisallowedTools: []),
            [FinancialCompanionIntent.BudgetStatus] = new(
                FinancialCompanionIntent.BudgetStatus,
                RequiredTools: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                OptionalTools: [CompanionTool.SpendingAnalysis],
                DisallowedTools: []),
            [FinancialCompanionIntent.PlanProgress] = new(
                FinancialCompanionIntent.PlanProgress,
                RequiredTools: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                OptionalTools: [CompanionTool.RecurringObligations, CompanionTool.SpendingAnalysis],
                DisallowedTools: []),
            [FinancialCompanionIntent.LocalPlacesOutings] = new(
                FinancialCompanionIntent.LocalPlacesOutings,
                RequiredTools: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                OptionalTools: [CompanionTool.PlacesSearch, CompanionTool.PlaceDetails, CompanionTool.ReviewInsights],
                DisallowedTools: []),
            [FinancialCompanionIntent.GeneralFinancialQuestion] = new(
                FinancialCompanionIntent.GeneralFinancialQuestion,
                RequiredTools: [CompanionTool.FinancialSummary],
                OptionalTools: [CompanionTool.BudgetStatus, CompanionTool.SpendingAnalysis, CompanionTool.RecurringObligations],
                DisallowedTools: []),
            [FinancialCompanionIntent.Ambiguous] = new(
                FinancialCompanionIntent.Ambiguous,
                RequiredTools: [],
                OptionalTools: [],
                DisallowedTools: Enum.GetValues<CompanionTool>()),
            [FinancialCompanionIntent.Unsupported] = new(
                FinancialCompanionIntent.Unsupported,
                RequiredTools: [],
                OptionalTools: [],
                DisallowedTools: Enum.GetValues<CompanionTool>())
        };

    public CompanionIntentToolPolicy Resolve(FinancialCompanionIntent intent)
    {
        return PolicyByIntent.GetValueOrDefault(intent, PolicyByIntent[FinancialCompanionIntent.GeneralFinancialQuestion]);
    }
}

public sealed class CompanionMixedIntentMergePolicy(
    ICompanionIntentToolPolicyProvider policyProvider,
    IOptions<CompanionOrchestrationOptions> options) : ICompanionMixedIntentMergePolicy
{
    private readonly CompanionOrchestrationOptions _options = options.Value;

    private static readonly IReadOnlyDictionary<FinancialCompanionIntent, IReadOnlyList<CompanionTool>> AllowlistByPrimary
        = new Dictionary<FinancialCompanionIntent, IReadOnlyList<CompanionTool>>
        {
            [FinancialCompanionIntent.Affordability] =
                [CompanionTool.TransactionQuery, CompanionTool.RecurringObligations, CompanionTool.PlacesSearch, CompanionTool.PlaceDetails, CompanionTool.ReviewInsights],
            [FinancialCompanionIntent.BudgetStatus] =
                [CompanionTool.SpendingAnalysis, CompanionTool.RecurringObligations, CompanionTool.TransactionQuery],
            [FinancialCompanionIntent.SpendingAnalysis] =
                [CompanionTool.BudgetStatus, CompanionTool.RecurringObligations],
            [FinancialCompanionIntent.SavingsCutbackAdvice] =
                [CompanionTool.BudgetStatus, CompanionTool.TransactionQuery],
            [FinancialCompanionIntent.PlanProgress] =
                [CompanionTool.SpendingAnalysis, CompanionTool.RecurringObligations, CompanionTool.TransactionQuery],
            [FinancialCompanionIntent.LocalPlacesOutings] =
                [CompanionTool.TransactionQuery, CompanionTool.RecurringObligations, CompanionTool.BudgetStatus],
            [FinancialCompanionIntent.GeneralFinancialQuestion] =
                [CompanionTool.BudgetStatus, CompanionTool.SpendingAnalysis, CompanionTool.RecurringObligations, CompanionTool.TransactionQuery]
        };

    public CompanionMixedIntentMergeResult Merge(
        FinancialCompanionIntent primaryIntent,
        IReadOnlyList<FinancialCompanionIntent> secondaryIntents)
    {
        if (secondaryIntents.Count == 0)
        {
            return new CompanionMixedIntentMergeResult([], []);
        }

        var allowlist = AllowlistByPrimary.GetValueOrDefault(primaryIntent, []);
        var candidateTools = new List<(CompanionTool tool, FinancialCompanionIntent source)>();
        foreach (var secondaryIntent in secondaryIntents)
        {
            var secondaryPolicy = policyProvider.Resolve(secondaryIntent);
            foreach (var tool in secondaryPolicy.RequiredTools.Concat(secondaryPolicy.OptionalTools))
            {
                if (tool != CompanionTool.FinancialSummary)
                {
                    candidateTools.Add((tool, secondaryIntent));
                }
            }
        }

        var grouped = candidateTools
            .GroupBy(x => x.tool)
            .OrderBy(group => group.Key.ToOptionalPriority())
            .ThenBy(group => group.Key.ToExecutionOrder())
            .ToList();
        var added = new List<CompanionTool>(Math.Max(0, _options.MaxSecondaryOptionalTools));
        var skipped = new List<CompanionSkippedToolDecision>(grouped.Count);
        foreach (var group in grouped)
        {
            var sourceIntents = group
                .Select(x => x.source)
                .Distinct()
                .OrderBy(intent => (int)intent)
                .ToArray();
            if (!allowlist.Contains(group.Key))
            {
                skipped.Add(new CompanionSkippedToolDecision(
                    group.Key,
                    "mixed_secondary_excluded_policy",
                    sourceIntents));
                continue;
            }

            if (added.Count >= Math.Max(0, _options.MaxSecondaryOptionalTools))
            {
                skipped.Add(new CompanionSkippedToolDecision(
                    group.Key,
                    "mixed_secondary_cap_exceeded",
                    sourceIntents));
                continue;
            }

            added.Add(group.Key);
        }

        return new CompanionMixedIntentMergeResult(
            AddedOptionalTools: added,
            SkippedTools: skipped);
    }
}
