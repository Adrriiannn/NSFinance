using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionExecutionPlanBuilder
{
    CompanionExecutionPlan Build(CompanionIntentRoutingResult routing);
}

public sealed class CompanionExecutionPlanBuilder(
    ICompanionIntentToolPolicyProvider policyProvider,
    ICompanionMixedIntentMergePolicy mixedIntentMergePolicy,
    IOptions<CompanionOrchestrationOptions> options) : ICompanionExecutionPlanBuilder
{
    private readonly CompanionOrchestrationOptions _options = options.Value;

    public CompanionExecutionPlan Build(CompanionIntentRoutingResult routing)
    {
        var warnings = new List<string>(2);
        var skipped = new List<CompanionSkippedToolDecision>(8);

        if (routing.IsAmbiguous || routing.IsUnsupported)
        {
            return new CompanionExecutionPlan([], skipped, warnings);
        }

        var primaryPolicy = policyProvider.Resolve(routing.PrimaryIntent);
        var requiredTools = new HashSet<CompanionTool>(primaryPolicy.RequiredTools);
        var optionalTools = new HashSet<CompanionTool>(primaryPolicy.OptionalTools);

        foreach (var disallowed in primaryPolicy.DisallowedTools)
        {
            requiredTools.Remove(disallowed);
            optionalTools.Remove(disallowed);
        }

        if (routing.IntentFamily == FinancialCompanionIntent.MixedQuery && routing.SecondaryIntents.Count > 0)
        {
            var merge = mixedIntentMergePolicy.Merge(routing.PrimaryIntent, routing.SecondaryIntents);
            foreach (var skippedTool in merge.SkippedTools)
            {
                skipped.Add(skippedTool);
            }

            foreach (var optional in merge.AddedOptionalTools)
            {
                if (requiredTools.Contains(optional))
                {
                    skipped.Add(new CompanionSkippedToolDecision(
                        optional,
                        "mixed_secondary_duplicate_required",
                        routing.SecondaryIntents));
                    continue;
                }

                optionalTools.Add(optional);
            }
        }

        var availableOptionalSlots = Math.Max(0, _options.MaxToolCallsPerRequest - requiredTools.Count);
        var selectedOptionalTools = optionalTools
            .OrderBy(tool => tool.ToOptionalPriority())
            .ThenBy(tool => tool.ToExecutionOrder())
            .Take(availableOptionalSlots)
            .ToHashSet();

        foreach (var suppressedOptional in optionalTools
                     .Where(tool => !selectedOptionalTools.Contains(tool))
                     .OrderBy(tool => tool.ToOptionalPriority()))
        {
            skipped.Add(new CompanionSkippedToolDecision(
                suppressedOptional,
                "cap_exceeded_or_skipped:plan_optional_budget",
                [routing.PrimaryIntent]));
        }

        var planned = new List<CompanionPlannedTool>(requiredTools.Count + selectedOptionalTools.Count);
        planned.AddRange(requiredTools
            .OrderBy(tool => tool.ToExecutionOrder())
            .Select(tool => new CompanionPlannedTool(
                Tool: tool,
                IsRequired: true,
                Order: tool.ToExecutionOrder(),
                InclusionReason: "primary_required",
                SourceIntents: [routing.PrimaryIntent])));
        planned.AddRange(selectedOptionalTools
            .OrderBy(tool => tool.ToOptionalPriority())
            .ThenBy(tool => tool.ToExecutionOrder())
            .Select(tool => new CompanionPlannedTool(
                Tool: tool,
                IsRequired: false,
                Order: tool.ToExecutionOrder(),
                InclusionReason: routing.IntentFamily == FinancialCompanionIntent.MixedQuery
                    ? "primary_or_mixed_optional"
                    : "primary_optional",
                SourceIntents: BuildSourceIntents(routing, tool))));

        if (planned.Count > _options.MaxToolCallsPerRequest)
        {
            warnings.Add("execution_plan_exceeds_tool_budget");
        }

        return new CompanionExecutionPlan(
            PlannedTools: planned,
            SkippedTools: skipped,
            Warnings: warnings);
    }

    private static IReadOnlyList<FinancialCompanionIntent> BuildSourceIntents(
        CompanionIntentRoutingResult routing,
        CompanionTool tool)
    {
        var intents = new HashSet<FinancialCompanionIntent> { routing.PrimaryIntent };
        if (routing.IntentFamily == FinancialCompanionIntent.MixedQuery)
        {
            foreach (var secondary in routing.SecondaryIntents)
            {
                intents.Add(secondary);
            }
        }

        return intents
            .OrderBy(intent => (int)intent)
            .ToArray();
    }
}
