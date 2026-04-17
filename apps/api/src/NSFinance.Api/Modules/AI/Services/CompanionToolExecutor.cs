using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionToolExecutor
{
    Task<CompanionToolExecutionResult> ExecuteAsync(
        FinancialCompanionRequest request,
        CompanionExecutionPlan executionPlan,
        UserFinancialContextSnapshot profile,
        CancellationToken cancellationToken);
}

public sealed class CompanionToolExecutor(
    IUserFinancialSummaryService summaryService,
    ISpendingAnalysisService spendingAnalysisService,
    IRecurringObligationsService recurringObligationsService,
    IBudgetStatusService budgetStatusService,
    ITransactionQueryService transactionQueryService,
    IPlacesSearchService placesSearchService,
    IPlaceDetailsService placeDetailsService,
    IReviewInsightsService reviewInsightsService,
    ICompanionContextShaper contextShaper,
    IOptions<CompanionOrchestrationOptions> options,
    ILogger<CompanionToolExecutor> logger) : ICompanionToolExecutor
{
    private readonly CompanionOrchestrationOptions _options = options.Value;

    public async Task<CompanionToolExecutionResult> ExecuteAsync(
        FinancialCompanionRequest request,
        CompanionExecutionPlan executionPlan,
        UserFinancialContextSnapshot profile,
        CancellationToken cancellationToken)
    {
        var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var records = new List<CompanionToolExecutionRecord>(executionPlan.PlannedTools.Count);
        var warnings = new List<string>(4);
        var executedCalls = 0;

        foreach (var plannedTool in executionPlan.PlannedTools.OrderBy(entry => entry.Order))
        {
            if (executedCalls >= _options.MaxToolCallsPerRequest)
            {
                records.Add(BuildSkippedRecord(
                    plannedTool,
                    status: CompanionToolExecutionStatus.SkippedCap,
                    reasonCode: $"{CompanionOrchestrationReasonCodes.CapExceededOrSkippedPrefix}:tool_call_budget"));
                continue;
            }

            if (!plannedTool.IsRequired && outputs.Count >= _options.MaxContextKeys)
            {
                records.Add(BuildSkippedRecord(
                    plannedTool,
                    status: CompanionToolExecutionStatus.SkippedContextCap,
                    reasonCode: $"{CompanionOrchestrationReasonCodes.CapExceededOrSkippedPrefix}:context_key_budget"));
                continue;
            }

            if (plannedTool.Tool is CompanionTool.PlaceDetails or CompanionTool.ReviewInsights)
            {
                if (!TryGetTopPlaceId(outputs, out _))
                {
                    records.Add(BuildSkippedRecord(
                        plannedTool,
                        status: CompanionToolExecutionStatus.SkippedDependency,
                        reasonCode: $"{CompanionOrchestrationReasonCodes.CapExceededOrSkippedPrefix}:missing_place_search_dependency"));
                    continue;
                }
            }

            CompanionShapedToolData shaped;
            try
            {
                shaped = await ExecuteAndShapeAsync(request, plannedTool.Tool, profile, outputs, cancellationToken);
                executedCalls += 1;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var reasonCode = CompanionOrchestrationReasonCodes.WithTool(
                    CompanionOrchestrationReasonCodes.TimeoutOrCancellationPrefix,
                    plannedTool.Tool);
                records.Add(new CompanionToolExecutionRecord(
                    PlannedTool: plannedTool,
                    Status: CompanionToolExecutionStatus.Failed,
                    ContractName: plannedTool.Tool.ToContractName(),
                    OutputKey: plannedTool.Tool.ToOutputKey(),
                    Output: null,
                    ReasonCode: reasonCode,
                    Warnings: [reasonCode],
                    IncludedInContext: false));
                continue;
            }
            catch (Exception ex)
            {
                var reasonCode = ClassifyException(plannedTool.Tool, ex);
                logger.LogWarning(ex, "Companion tool execution failed for {Tool}", plannedTool.Tool);
                records.Add(new CompanionToolExecutionRecord(
                    PlannedTool: plannedTool,
                    Status: CompanionToolExecutionStatus.Failed,
                    ContractName: plannedTool.Tool.ToContractName(),
                    OutputKey: plannedTool.Tool.ToOutputKey(),
                    Output: null,
                    ReasonCode: reasonCode,
                    Warnings: [reasonCode],
                    IncludedInContext: false));
                continue;
            }

            if (!shaped.HasData)
            {
                var reasonCode = CompanionOrchestrationReasonCodes.WithTool(
                    "tool_returned_no_data",
                    plannedTool.Tool);
                records.Add(new CompanionToolExecutionRecord(
                    PlannedTool: plannedTool,
                    Status: CompanionToolExecutionStatus.NoData,
                    ContractName: plannedTool.Tool.ToContractName(),
                    OutputKey: shaped.OutputKey,
                    Output: null,
                    ReasonCode: reasonCode,
                    Warnings: shaped.Warnings.Concat([reasonCode]).ToArray(),
                    IncludedInContext: false));
                continue;
            }

            outputs[shaped.OutputKey] = shaped.Output;
            warnings.AddRange(shaped.Warnings);
            warnings.AddRange(shaped.TrimIndicators);
            records.Add(new CompanionToolExecutionRecord(
                PlannedTool: plannedTool,
                Status: CompanionToolExecutionStatus.Success,
                ContractName: plannedTool.Tool.ToContractName(),
                OutputKey: shaped.OutputKey,
                Output: shaped.Output,
                ReasonCode: null,
                Warnings: shaped.Warnings.Concat(shaped.TrimIndicators).ToArray(),
                IncludedInContext: true));
        }

        return new CompanionToolExecutionResult(
            ContextOutputs: outputs,
            Records: records,
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<CompanionShapedToolData> ExecuteAndShapeAsync(
        FinancialCompanionRequest request,
        CompanionTool tool,
        UserFinancialContextSnapshot profile,
        IReadOnlyDictionary<string, object?> currentOutputs,
        CancellationToken cancellationToken)
    {
        return tool switch
        {
            CompanionTool.FinancialSummary => contextShaper.ShapeFinancialSummary(
                await summaryService.GetSummaryAsync(request.UserId, cancellationToken)),
            CompanionTool.SpendingAnalysis => contextShaper.ShapeSpendingAnalysis(
                await spendingAnalysisService.AnalyzeAsync(request.UserId, 60, cancellationToken)),
            CompanionTool.RecurringObligations => contextShaper.ShapeRecurringObligations(
                await recurringObligationsService.GetRecurringAsync(request.UserId, cancellationToken)),
            CompanionTool.BudgetStatus => contextShaper.ShapeBudgetStatus(
                await budgetStatusService.GetBudgetStatusAsync(request.UserId, cancellationToken)),
            CompanionTool.TransactionQuery => contextShaper.ShapeTransactionMatches(
                await transactionQueryService.QueryAsync(request.UserId, request.UserQuery, _options.MaxTransactionRows, cancellationToken)),
            CompanionTool.PlacesSearch => contextShaper.ShapePlaceSearch(
                await placesSearchService.SearchAsync(request.UserQuery, profile.Country, cancellationToken)),
            CompanionTool.PlaceDetails => contextShaper.ShapePlaceDetails(
                await placeDetailsService.GetDetailsAsync(GetTopPlaceId(currentOutputs), cancellationToken)),
            CompanionTool.ReviewInsights => contextShaper.ShapeReviewInsights(
                await reviewInsightsService.GetInsightsAsync(GetTopPlaceId(currentOutputs), cancellationToken)),
            _ => throw new InvalidOperationException($"Unhandled companion tool: {tool}")
        };
    }

    private static string GetTopPlaceId(IReadOnlyDictionary<string, object?> outputs)
    {
        if (!TryGetTopPlaceId(outputs, out var placeId))
        {
            throw new InvalidOperationException("place_search dependency missing for place details/review insights.");
        }

        return placeId;
    }

    private static bool TryGetTopPlaceId(IReadOnlyDictionary<string, object?> outputs, out string placeId)
    {
        placeId = string.Empty;
        if (!outputs.TryGetValue(CompanionTool.PlacesSearch.ToOutputKey(), out var placeSearchObj)
            || placeSearchObj is not CompanionPlacesSearchContext search
            || search.Items.Count == 0)
        {
            return false;
        }

        placeId = search.Items[0].PlaceId ?? string.Empty;
        return !string.IsNullOrWhiteSpace(placeId);
    }

    private static CompanionToolExecutionRecord BuildSkippedRecord(
        CompanionPlannedTool plannedTool,
        CompanionToolExecutionStatus status,
        string reasonCode)
    {
        return new CompanionToolExecutionRecord(
            PlannedTool: plannedTool,
            Status: status,
            ContractName: plannedTool.Tool.ToContractName(),
            OutputKey: plannedTool.Tool.ToOutputKey(),
            Output: null,
            ReasonCode: reasonCode,
            Warnings: [reasonCode],
            IncludedInContext: false);
    }

    private static string ClassifyException(CompanionTool tool, Exception ex)
    {
        var prefix = ex switch
        {
            TimeoutException => CompanionOrchestrationReasonCodes.TimeoutOrCancellationPrefix,
            InvalidOperationException => CompanionOrchestrationReasonCodes.ProviderUnavailablePrefix,
            _ => "tool_failed"
        };
        return CompanionOrchestrationReasonCodes.WithTool(prefix, tool);
    }
}
