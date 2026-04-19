namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionEvidenceBuilder
{
    CompanionContextEvidence Build(
        CompanionExecutionPlan plan,
        IReadOnlyList<CompanionToolExecutionRecord> records,
        IReadOnlyDictionary<string, object?> contextOutputs,
        CompanionInsufficiencyDecision insufficiency,
        IReadOnlyList<string> trimIndicators,
        IReadOnlyList<string> warnings);
}

public sealed class CompanionEvidenceBuilder : ICompanionEvidenceBuilder
{
    public CompanionContextEvidence Build(
        CompanionExecutionPlan plan,
        IReadOnlyList<CompanionToolExecutionRecord> records,
        IReadOnlyDictionary<string, object?> contextOutputs,
        CompanionInsufficiencyDecision insufficiency,
        IReadOnlyList<string> trimIndicators,
        IReadOnlyList<string> warnings)
    {
        var plannedTools = plan.PlannedTools
            .Select(entry => entry.Tool.ToContractName())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var toolsUsed = records
            .Where(record => record.IncludedInContext && record.Status == CompanionToolExecutionStatus.Success)
            .Select(record => record.ContractName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var requiredUsed = records
            .Where(record => record.IncludedInContext && record.PlannedTool.IsRequired)
            .Select(record => record.ContractName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var optionalUsed = records
            .Where(record => record.IncludedInContext && !record.PlannedTool.IsRequired)
            .Select(record => record.ContractName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var skipped = new List<string>(plan.SkippedTools.Count + records.Count);
        skipped.AddRange(plan.SkippedTools.Select(skip => $"{skip.Tool.ToContractName()}:{skip.ReasonCode}"));
        skipped.AddRange(records
            .Where(record => !record.IncludedInContext && record.Status != CompanionToolExecutionStatus.Success)
            .Select(record => $"{record.ContractName}:{record.ReasonCode ?? record.Status.ToString()}"));

        var basisSummary = BuildBasisSummary(plan, records, contextOutputs);
        return new CompanionContextEvidence(
            ToolsUsed: toolsUsed,
            RequiredToolsUsed: requiredUsed,
            OptionalToolsUsed: optionalUsed,
            MissingRequiredTools: insufficiency.MissingRequiredTools,
            BasisSummary: basisSummary,
            SkippedTools: skipped.Distinct(StringComparer.Ordinal).ToArray(),
            PlannedTools: plannedTools,
            TrimmedPayloadIndicators: trimIndicators,
            InsufficiencySummary: insufficiency.Reasons,
            ExecutionWarnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<string> BuildBasisSummary(
        CompanionExecutionPlan plan,
        IReadOnlyList<CompanionToolExecutionRecord> records,
        IReadOnlyDictionary<string, object?> outputs)
    {
        var basis = new List<string>(outputs.Count);
        if (outputs.ContainsKey(CompanionTool.FinancialSummary.ToOutputKey()))
        {
            basis.Add("based_on_financial_summary");
        }

        if (outputs.ContainsKey(CompanionTool.BudgetStatus.ToOutputKey()))
        {
            basis.Add("based_on_budget_status");
        }

        if (outputs.ContainsKey(CompanionTool.SpendingAnalysis.ToOutputKey()))
        {
            basis.Add("based_on_spending_analysis");
        }

        if (outputs.ContainsKey(CompanionTool.RecurringObligations.ToOutputKey()))
        {
            basis.Add("based_on_recurring_obligations");
        }

        if (outputs.ContainsKey(CompanionTool.TransactionQuery.ToOutputKey()))
        {
            basis.Add("based_on_recent_transaction_matches");
        }

        if (outputs.ContainsKey(CompanionTool.PlacesSearch.ToOutputKey()))
        {
            basis.Add("based_on_places_search");
        }

        if (outputs.ContainsKey(CompanionTool.PlaceDetails.ToOutputKey()))
        {
            basis.Add("based_on_place_details");
        }

        if (outputs.ContainsKey(CompanionTool.ReviewInsights.ToOutputKey()))
        {
            basis.Add("based_on_review_insights");
        }

        AppendPlacesDiagnostics(plan, records, outputs, basis);

        return basis;
    }

    private static void AppendPlacesDiagnostics(
        CompanionExecutionPlan plan,
        IReadOnlyList<CompanionToolExecutionRecord> records,
        IReadOnlyDictionary<string, object?> outputs,
        ICollection<string> basis)
    {
        if (plan.PlannedTools.Any(tool => tool.Tool == CompanionTool.PlacesSearch))
        {
            basis.Add("places_search_planned");
        }

        var placesRecord = records.FirstOrDefault(record => record.PlannedTool.Tool == CompanionTool.PlacesSearch);
        if (placesRecord is not null)
        {
            basis.Add(placesRecord.Status switch
            {
                CompanionToolExecutionStatus.Success => "places_search_succeeded",
                CompanionToolExecutionStatus.NoData => "places_search_no_data",
                CompanionToolExecutionStatus.Failed => "places_search_failed",
                _ => "places_search_skipped"
            });
        }

        if (outputs.TryGetValue(CompanionTool.PlacesSearch.ToOutputKey(), out var output)
            && output is CompanionPlacesSearchContext placesSearch)
        {
            basis.Add($"places_search_candidate_count:{placesSearch.Items.Count}");
        }

        if (plan.PlannedTools.Any(tool => tool.Tool == CompanionTool.PlaceDetails))
        {
            basis.Add("place_details_planned");
        }

        var detailsRecord = records.FirstOrDefault(record => record.PlannedTool.Tool == CompanionTool.PlaceDetails);
        if (detailsRecord is not null)
        {
            basis.Add(detailsRecord.Status switch
            {
                CompanionToolExecutionStatus.Success => "place_details_succeeded",
                CompanionToolExecutionStatus.NoData => "place_details_no_data",
                CompanionToolExecutionStatus.SkippedDependency => "place_details_skipped_missing_dependency",
                CompanionToolExecutionStatus.Failed => "place_details_failed",
                _ => "place_details_skipped"
            });
        }
    }
}
