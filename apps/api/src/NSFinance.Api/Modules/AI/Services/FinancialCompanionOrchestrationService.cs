using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialCompanionContextAssembler
{
    Task<CompanionContextAssemblyResult> AssembleAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        UserFinancialContextSnapshot profile,
        CancellationToken cancellationToken);
}

public sealed record CompanionContextAssemblyResult(
    FinancialCompanionContext Context,
    IReadOnlyList<string> ToolsUsed,
    CompanionContextEvidence Evidence,
    IReadOnlyList<string> Warnings,
    bool HasInsufficientData,
    IReadOnlyList<string> InsufficientDataReasons,
    bool CanProceedToAI);

public sealed class FinancialCompanionContextAssembler(
    IUserFinancialSummaryService summaryService,
    ISpendingAnalysisService spendingAnalysisService,
    IRecurringObligationsService recurringObligationsService,
    IBudgetStatusService budgetStatusService,
    ITransactionQueryService transactionQueryService,
    IPlacesSearchService placesSearchService,
    IPlaceDetailsService placeDetailsService,
    IReviewInsightsService reviewInsightsService,
    ILogger<FinancialCompanionContextAssembler> logger) : IFinancialCompanionContextAssembler
{
    private const int MaxToolCallsPerRequest = 6;
    private const int MaxContextKeys = 7;
    private const int MaxSerializedContextChars = 10_000;
    private const int MaxSpendDomains = 6;
    private const int MaxRecurringItems = 5;
    private const int MaxTransactionRows = 8;
    private const int MaxPlaceItems = 3;
    private const int MaxSummaryTextLength = 200;

    private static readonly JsonSerializerOptions ContextJsonOptions = new()
    {
        WriteIndented = false
    };

    private static readonly IReadOnlyDictionary<FinancialCompanionIntent, CompanionIntentToolPolicy> PolicyByIntent
        = new Dictionary<FinancialCompanionIntent, CompanionIntentToolPolicy>
        {
            [FinancialCompanionIntent.SpendingAnalysis] = new(
                Required: [CompanionTool.FinancialSummary, CompanionTool.SpendingAnalysis],
                Optional: [CompanionTool.BudgetStatus]),
            [FinancialCompanionIntent.SavingsCutbackAdvice] = new(
                Required: [CompanionTool.FinancialSummary, CompanionTool.SpendingAnalysis, CompanionTool.RecurringObligations],
                Optional: [CompanionTool.BudgetStatus]),
            [FinancialCompanionIntent.Affordability] = new(
                Required: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                Optional: [CompanionTool.TransactionQuery, CompanionTool.RecurringObligations]),
            [FinancialCompanionIntent.BudgetStatus] = new(
                Required: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                Optional: [CompanionTool.SpendingAnalysis]),
            [FinancialCompanionIntent.PlanProgress] = new(
                Required: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                Optional: [CompanionTool.RecurringObligations, CompanionTool.SpendingAnalysis]),
            [FinancialCompanionIntent.LocalPlacesOutings] = new(
                Required: [CompanionTool.FinancialSummary, CompanionTool.BudgetStatus],
                Optional: [CompanionTool.PlacesSearch, CompanionTool.PlaceDetails, CompanionTool.ReviewInsights]),
            [FinancialCompanionIntent.GeneralFinancialQuestion] = new(
                Required: [CompanionTool.FinancialSummary],
                Optional: [CompanionTool.BudgetStatus, CompanionTool.SpendingAnalysis, CompanionTool.RecurringObligations]),
            [FinancialCompanionIntent.Ambiguous] = new(
                Required: [],
                Optional: []),
            [FinancialCompanionIntent.Unsupported] = new(
                Required: [],
                Optional: [])
        };

    private static readonly IReadOnlyDictionary<FinancialCompanionIntent, IReadOnlyList<CompanionTool>> SecondaryOptionalAllowlistByPrimary
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

    private static readonly IReadOnlyDictionary<CompanionTool, int> OptionalPriority = new Dictionary<CompanionTool, int>
    {
        [CompanionTool.BudgetStatus] = 10,
        [CompanionTool.SpendingAnalysis] = 20,
        [CompanionTool.RecurringObligations] = 30,
        [CompanionTool.TransactionQuery] = 40,
        [CompanionTool.PlacesSearch] = 50,
        [CompanionTool.PlaceDetails] = 60,
        [CompanionTool.ReviewInsights] = 70
    };

    public async Task<CompanionContextAssemblyResult> AssembleAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        UserFinancialContextSnapshot profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>(4);
        var insufficientReasons = new List<string>(4);
        var missingRequiredTools = new List<string>(2);
        var usedRequiredTools = new List<string>(4);
        var usedOptionalTools = new List<string>(4);
        var skippedTools = new List<string>(4);
        var toolsUsed = new List<string>(8);
        var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (routing.IsAmbiguous || routing.IsUnsupported)
        {
            var reason = routing.IsUnsupported
                ? "unsupported_query_scope"
                : "ambiguous_query_requires_clarification";
            insufficientReasons.Add(reason);
            warnings.Add(reason);

            var emptyContext = new FinancialCompanionContext(
                Intent: routing.PrimaryIntent,
                Profile: profile,
                ToolOutputs: outputs,
                ToolsUsed: toolsUsed);
            var emptyEvidence = new CompanionContextEvidence(
                ToolsUsed: toolsUsed,
                RequiredToolsUsed: usedRequiredTools,
                OptionalToolsUsed: usedOptionalTools,
                MissingRequiredTools: missingRequiredTools,
                BasisSummary: [],
                SkippedTools: skippedTools);
            return new CompanionContextAssemblyResult(
                Context: emptyContext with { Evidence = emptyEvidence },
                ToolsUsed: toolsUsed,
                Evidence: emptyEvidence,
                Warnings: warnings,
                HasInsufficientData: true,
                InsufficientDataReasons: insufficientReasons,
                CanProceedToAI: false);
        }

        var executionPlan = BuildExecutionPlan(routing);
        var toolCallCount = 0;

        foreach (var tool in executionPlan.Required)
        {
            if (toolCallCount >= MaxToolCallsPerRequest)
            {
                missingRequiredTools.Add(ToContractName(tool));
                insufficientReasons.Add("max_tool_call_budget_hit_required");
                warnings.Add("max_tool_call_budget_hit_required");
                continue;
            }

            var outcome = await ExecuteToolAsync(request, routing, profile, tool, outputs, cancellationToken);
            toolCallCount += 1;

            if (!outcome.Succeeded || !outcome.HasSufficientData)
            {
                missingRequiredTools.Add(ToContractName(tool));
                insufficientReasons.Add($"missing_required_{ToReasonSuffix(tool)}");
                warnings.Add($"required_tool_missing_{ToReasonSuffix(tool)}");
                if (!string.IsNullOrWhiteSpace(outcome.WarningReason))
                {
                    warnings.Add(outcome.WarningReason);
                }

                continue;
            }

            AddOutput(outputs, outcome, warnings);
            AddTool(toolsUsed, ToContractName(tool));
            AddTool(usedRequiredTools, ToContractName(tool));
        }

        foreach (var tool in executionPlan.Optional)
        {
            if (toolCallCount >= MaxToolCallsPerRequest)
            {
                skippedTools.Add($"{ToContractName(tool)}:max_tool_call_budget_hit");
                continue;
            }

            if (outputs.Count >= MaxContextKeys)
            {
                skippedTools.Add($"{ToContractName(tool)}:max_context_keys_hit");
                continue;
            }

            if (tool is CompanionTool.PlaceDetails or CompanionTool.ReviewInsights)
            {
                if (!outputs.TryGetValue("place_search", out var placeSearchObj)
                    || placeSearchObj is not CompanionPlacesSearchContext placeSearchContext
                    || placeSearchContext.Items.Count == 0)
                {
                    skippedTools.Add($"{ToContractName(tool)}:place_search_unavailable");
                    continue;
                }
            }

            var outcome = await ExecuteToolAsync(request, routing, profile, tool, outputs, cancellationToken);
            toolCallCount += 1;

            if (!outcome.Succeeded)
            {
                skippedTools.Add($"{ToContractName(tool)}:tool_failed");
                if (!string.IsNullOrWhiteSpace(outcome.WarningReason))
                {
                    warnings.Add(outcome.WarningReason);
                }

                continue;
            }

            if (!outcome.HasSufficientData)
            {
                skippedTools.Add($"{ToContractName(tool)}:insufficient_data");
                continue;
            }

            AddOutput(outputs, outcome, warnings);
            AddTool(toolsUsed, ToContractName(tool));
            AddTool(usedOptionalTools, ToContractName(tool));
        }

        TrimToPayloadBudget(outputs, usedOptionalTools, toolsUsed, warnings, skippedTools);

        var basisSummary = BuildBasisSummary(outputs);
        var evidence = new CompanionContextEvidence(
            ToolsUsed: toolsUsed,
            RequiredToolsUsed: usedRequiredTools,
            OptionalToolsUsed: usedOptionalTools,
            MissingRequiredTools: missingRequiredTools,
            BasisSummary: basisSummary,
            SkippedTools: skippedTools);
        var context = new FinancialCompanionContext(
            Intent: routing.PrimaryIntent,
            Profile: profile,
            ToolOutputs: outputs,
            ToolsUsed: toolsUsed,
            Evidence: evidence);

        var hasInsufficient = missingRequiredTools.Count > 0;
        var canProceed = !hasInsufficient;
        if (hasInsufficient && insufficientReasons.Count == 0)
        {
            insufficientReasons.Add("required_grounding_missing");
        }

        return new CompanionContextAssemblyResult(
            Context: context,
            ToolsUsed: toolsUsed,
            Evidence: evidence,
            Warnings: warnings,
            HasInsufficientData: hasInsufficient,
            InsufficientDataReasons: insufficientReasons,
            CanProceedToAI: canProceed);
    }

    private static CompanionToolExecutionPlan BuildExecutionPlan(CompanionIntentRoutingResult routing)
    {
        var primaryIntent = routing.PrimaryIntent;
        if (!PolicyByIntent.TryGetValue(primaryIntent, out var primaryPolicy))
        {
            primaryPolicy = PolicyByIntent[FinancialCompanionIntent.GeneralFinancialQuestion];
        }

        var required = new HashSet<CompanionTool>(primaryPolicy.Required);
        var optional = new HashSet<CompanionTool>(primaryPolicy.Optional);

        if (routing.IntentFamily == FinancialCompanionIntent.MixedQuery && routing.SecondaryIntents.Count > 0)
        {
            var candidateSecondaryOptionals = new HashSet<CompanionTool>();
            foreach (var secondary in routing.SecondaryIntents)
            {
                if (!PolicyByIntent.TryGetValue(secondary, out var secondaryPolicy))
                {
                    continue;
                }

                foreach (var tool in secondaryPolicy.Required.Concat(secondaryPolicy.Optional))
                {
                    if (tool != CompanionTool.FinancialSummary)
                    {
                        candidateSecondaryOptionals.Add(tool);
                    }
                }
            }

            if (SecondaryOptionalAllowlistByPrimary.TryGetValue(primaryIntent, out var allowlist))
            {
                foreach (var tool in candidateSecondaryOptionals
                             .Where(tool => allowlist.Contains(tool))
                             .OrderBy(tool => OptionalPriority.GetValueOrDefault(tool, 100)))
                {
                    optional.Add(tool);
                }
            }
        }

        optional.ExceptWith(required);

        var orderedRequired = required
            .OrderBy(tool => ToolOrder(tool))
            .ToArray();
        var orderedOptional = optional
            .OrderBy(tool => OptionalPriority.GetValueOrDefault(tool, 100))
            .ThenBy(tool => ToolOrder(tool))
            .ToArray();

        return new CompanionToolExecutionPlan(orderedRequired, orderedOptional);
    }

    private async Task<ToolExecutionOutcome> ExecuteToolAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        UserFinancialContextSnapshot profile,
        CompanionTool tool,
        IReadOnlyDictionary<string, object?> currentOutputs,
        CancellationToken cancellationToken)
    {
        try
        {
            return tool switch
            {
                CompanionTool.FinancialSummary => await ExecuteFinancialSummaryAsync(request.UserId, cancellationToken),
                CompanionTool.SpendingAnalysis => await ExecuteSpendingAnalysisAsync(request.UserId, routing.PrimaryIntent, cancellationToken),
                CompanionTool.RecurringObligations => await ExecuteRecurringAsync(request.UserId, cancellationToken),
                CompanionTool.BudgetStatus => await ExecuteBudgetStatusAsync(request.UserId, cancellationToken),
                CompanionTool.TransactionQuery => await ExecuteTransactionQueryAsync(request.UserId, request.UserQuery, cancellationToken),
                CompanionTool.PlacesSearch => await ExecutePlaceSearchAsync(request.UserQuery, profile.Country, cancellationToken),
                CompanionTool.PlaceDetails => await ExecutePlaceDetailsAsync(currentOutputs, cancellationToken),
                CompanionTool.ReviewInsights => await ExecuteReviewInsightsAsync(currentOutputs, cancellationToken),
                _ => ToolExecutionOutcome.Failed(tool, "tool_unhandled")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Companion orchestration tool failed for {Tool}", tool);
            return ToolExecutionOutcome.Failed(tool, $"tool_failed_{ToReasonSuffix(tool)}");
        }
    }

    private async Task<ToolExecutionOutcome> ExecuteFinancialSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var summary = await summaryService.GetSummaryAsync(userId, cancellationToken);
        var normalized = new CompanionFinancialSummaryContext(
            summary.IncomeLast30Days,
            summary.SpendLast30Days,
            summary.NetLast30Days,
            summary.Currency);
        var hasData = summary.Currency.Length > 0;
        return ToolExecutionOutcome.Success(CompanionTool.FinancialSummary, "financial_summary", normalized, hasData);
    }

    private async Task<ToolExecutionOutcome> ExecuteSpendingAnalysisAsync(
        Guid userId,
        FinancialCompanionIntent primaryIntent,
        CancellationToken cancellationToken)
    {
        var lookbackDays = primaryIntent == FinancialCompanionIntent.SavingsCutbackAdvice ? 90 : 60;
        var spending = await spendingAnalysisService.AnalyzeAsync(userId, lookbackDays, cancellationToken);
        var topDomains = spending.SpendByDomain
            .OrderByDescending(x => Math.Abs(x.Value))
            .Take(MaxSpendDomains)
            .Select(x => new CompanionDomainSpendContextItem(x.Key, x.Value))
            .ToArray();

        var normalized = new CompanionSpendingAnalysisContext(
            TopDomainSpend: topDomains,
            DomainCount: spending.SpendByDomain.Count,
            AverageDailySpend: spending.AverageDailySpend,
            LargestExpense: spending.LargestExpense);

        var hasData = topDomains.Length > 0 || spending.LargestExpense > 0m || spending.AverageDailySpend > 0m;
        return ToolExecutionOutcome.Success(CompanionTool.SpendingAnalysis, "spending_analysis", normalized, hasData);
    }

    private async Task<ToolExecutionOutcome> ExecuteRecurringAsync(Guid userId, CancellationToken cancellationToken)
    {
        var recurring = await recurringObligationsService.GetRecurringAsync(userId, cancellationToken);
        var topItems = recurring.Items
            .OrderByDescending(x => Math.Abs(x.Amount))
            .Take(MaxRecurringItems)
            .Select(x => new CompanionRecurringItemContext(
                Name: ClampText(x.Name, 60),
                Amount: x.Amount,
                Currency: x.Currency,
                FrequencyDays: x.FrequencyDays))
            .ToArray();
        var normalized = new CompanionRecurringObligationsContext(
            TotalItemCount: recurring.Items.Count,
            EstimatedMonthlyTotal: recurring.EstimatedMonthlyTotal,
            TopItems: topItems);
        var hasData = topItems.Length > 0 || recurring.EstimatedMonthlyTotal > 0m;
        return ToolExecutionOutcome.Success(CompanionTool.RecurringObligations, "recurring_obligations", normalized, hasData);
    }

    private async Task<ToolExecutionOutcome> ExecuteBudgetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var budget = await budgetStatusService.GetBudgetStatusAsync(userId, cancellationToken);
        var normalized = new CompanionBudgetStatusContext(
            budget.HasBudgetPlan,
            budget.MonthlyBudget,
            budget.MonthToDateSpend,
            budget.RemainingBudget);
        var hasData = budget.HasBudgetPlan || budget.MonthToDateSpend > 0m || budget.MonthlyBudget.HasValue || budget.RemainingBudget.HasValue;
        return ToolExecutionOutcome.Success(CompanionTool.BudgetStatus, "budget_status", normalized, hasData);
    }

    private async Task<ToolExecutionOutcome> ExecuteTransactionQueryAsync(Guid userId, string query, CancellationToken cancellationToken)
    {
        var result = await transactionQueryService.QueryAsync(userId, query, MaxTransactionRows, cancellationToken);
        var normalizedItems = result.Items
            .Take(MaxTransactionRows)
            .Select(item => new CompanionTransactionMatchContext(
                BookedDateUtc: item.BookedAtUtc.Date,
                Amount: item.Amount,
                Currency: item.Currency,
                Description: ClampText(item.Description, 90),
                DomainCode: item.DomainCode,
                CategoryCode: item.CategoryCode))
            .ToArray();
        var normalized = new CompanionTransactionMatchesContext(
            TotalItemCount: result.Items.Count,
            Items: normalizedItems);
        var hasData = normalizedItems.Length > 0;
        return ToolExecutionOutcome.Success(CompanionTool.TransactionQuery, "transaction_matches", normalized, hasData);
    }

    private async Task<ToolExecutionOutcome> ExecutePlaceSearchAsync(string query, string country, CancellationToken cancellationToken)
    {
        var result = await placesSearchService.SearchAsync(query, country, cancellationToken);
        var items = result.Items
            .Take(MaxPlaceItems)
            .Select(item => new CompanionPlaceSearchContextItem(
                PlaceId: ClampText(item.PlaceId, 80),
                Name: ClampText(item.Name, 80),
                Category: ClampText(item.Category, 40),
                PriceLevel: ClampText(item.PriceLevel, 20)))
            .ToArray();
        var normalized = new CompanionPlacesSearchContext(
            TotalItemCount: result.Items.Count,
            Items: items);
        var hasData = items.Length > 0;
        return ToolExecutionOutcome.Success(CompanionTool.PlacesSearch, "place_search", normalized, hasData);
    }

    private async Task<ToolExecutionOutcome> ExecutePlaceDetailsAsync(
        IReadOnlyDictionary<string, object?> currentOutputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetTopPlaceId(currentOutputs, out var placeId))
        {
            return ToolExecutionOutcome.Failed(CompanionTool.PlaceDetails, "place_details_requires_place_id");
        }

        var details = await placeDetailsService.GetDetailsAsync(placeId, cancellationToken);
        var normalized = new CompanionPlaceDetailsContext(
            PlaceId: ClampText(details.PlaceId, 80),
            Name: ClampText(details.Name, 80),
            Address: ClampText(details.Address, 120),
            Website: ClampText(details.Website, 120),
            PriceLevel: ClampText(details.PriceLevel, 20));
        var hasData = !string.IsNullOrWhiteSpace(normalized.PlaceId) || !string.IsNullOrWhiteSpace(normalized.Name);
        return ToolExecutionOutcome.Success(CompanionTool.PlaceDetails, "place_details", normalized, hasData);
    }

    private async Task<ToolExecutionOutcome> ExecuteReviewInsightsAsync(
        IReadOnlyDictionary<string, object?> currentOutputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetTopPlaceId(currentOutputs, out var placeId))
        {
            return ToolExecutionOutcome.Failed(CompanionTool.ReviewInsights, "review_insights_requires_place_id");
        }

        var insights = await reviewInsightsService.GetInsightsAsync(placeId, cancellationToken);
        var normalized = new CompanionReviewInsightsContext(
            PlaceId: ClampText(insights.PlaceId, 80),
            Summary: ClampText(insights.Summary, MaxSummaryTextLength),
            AverageRating: insights.AverageRating);
        var hasData = !string.IsNullOrWhiteSpace(normalized.Summary) || normalized.AverageRating.HasValue;
        return ToolExecutionOutcome.Success(CompanionTool.ReviewInsights, "review_insights", normalized, hasData);
    }

    private static bool TryGetTopPlaceId(IReadOnlyDictionary<string, object?> currentOutputs, out string placeId)
    {
        placeId = string.Empty;
        if (!currentOutputs.TryGetValue("place_search", out var placeSearchObj)
            || placeSearchObj is not CompanionPlacesSearchContext placeSearch
            || placeSearch.Items.Count == 0)
        {
            return false;
        }

        placeId = placeSearch.Items[0].PlaceId ?? string.Empty;
        return !string.IsNullOrWhiteSpace(placeId);
    }

    private static void AddOutput(
        IDictionary<string, object?> outputs,
        ToolExecutionOutcome outcome,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(outcome.OutputKey))
        {
            return;
        }

        outputs[outcome.OutputKey] = outcome.Output;
        if (!string.IsNullOrWhiteSpace(outcome.WarningReason))
        {
            warnings.Add(outcome.WarningReason);
        }
    }

    private static void AddTool(ICollection<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }

    private static IReadOnlyList<string> BuildBasisSummary(IReadOnlyDictionary<string, object?> outputs)
    {
        var basis = new List<string>(outputs.Count);
        if (outputs.ContainsKey("financial_summary"))
        {
            basis.Add("based_on_financial_summary");
        }

        if (outputs.ContainsKey("budget_status"))
        {
            basis.Add("based_on_budget_status");
        }

        if (outputs.ContainsKey("spending_analysis"))
        {
            basis.Add("based_on_spending_analysis");
        }

        if (outputs.ContainsKey("recurring_obligations"))
        {
            basis.Add("based_on_recurring_obligations");
        }

        if (outputs.ContainsKey("transaction_matches"))
        {
            basis.Add("based_on_recent_transaction_matches");
        }

        if (outputs.ContainsKey("place_search"))
        {
            basis.Add("based_on_places_search");
        }

        if (outputs.ContainsKey("place_details"))
        {
            basis.Add("based_on_place_details");
        }

        if (outputs.ContainsKey("review_insights"))
        {
            basis.Add("based_on_review_insights");
        }

        return basis;
    }

    private static void TrimToPayloadBudget(
        IDictionary<string, object?> outputs,
        IList<string> optionalToolsUsed,
        IList<string> toolsUsed,
        ICollection<string> warnings,
        ICollection<string> skippedTools)
    {
        var removable = new List<(string key, string contract, int rank)>
        {
            ("review_insights", "IReviewInsightsService", 100),
            ("place_details", "IPlaceDetailsService", 90),
            ("place_search", "IPlacesSearchService", 80),
            ("transaction_matches", "ITransactionQueryService", 70),
            ("spending_analysis", "ISpendingAnalysisService", 60),
            ("recurring_obligations", "IRecurringObligationsService", 50),
            ("budget_status", "IBudgetStatusService", 40)
        };

        string Serialize() => JsonSerializer.Serialize(outputs, ContextJsonOptions);
        while (Serialize().Length > MaxSerializedContextChars)
        {
            var candidate = removable
                .Where(x => outputs.ContainsKey(x.key) && optionalToolsUsed.Contains(x.contract, StringComparer.Ordinal))
                .OrderByDescending(x => x.rank)
                .FirstOrDefault();
            if (candidate.key is null)
            {
                warnings.Add("context_payload_over_budget");
                break;
            }

            outputs.Remove(candidate.key);
            optionalToolsUsed.Remove(candidate.contract);
            toolsUsed.Remove(candidate.contract);
            skippedTools.Add($"{candidate.contract}:payload_budget_trimmed");
            warnings.Add("context_payload_trimmed");
        }
    }

    private static string ToContractName(CompanionTool tool)
    {
        return tool switch
        {
            CompanionTool.FinancialSummary => "IUserFinancialSummaryService",
            CompanionTool.SpendingAnalysis => "ISpendingAnalysisService",
            CompanionTool.RecurringObligations => "IRecurringObligationsService",
            CompanionTool.BudgetStatus => "IBudgetStatusService",
            CompanionTool.TransactionQuery => "ITransactionQueryService",
            CompanionTool.PlacesSearch => "IPlacesSearchService",
            CompanionTool.PlaceDetails => "IPlaceDetailsService",
            CompanionTool.ReviewInsights => "IReviewInsightsService",
            _ => tool.ToString()
        };
    }

    private static string ToReasonSuffix(CompanionTool tool)
    {
        return tool switch
        {
            CompanionTool.FinancialSummary => "financial_summary",
            CompanionTool.SpendingAnalysis => "spending_analysis",
            CompanionTool.RecurringObligations => "recurring_obligations",
            CompanionTool.BudgetStatus => "budget_status",
            CompanionTool.TransactionQuery => "transaction_query",
            CompanionTool.PlacesSearch => "places_search",
            CompanionTool.PlaceDetails => "place_details",
            CompanionTool.ReviewInsights => "review_insights",
            _ => tool.ToString().ToLowerInvariant()
        };
    }

    private static int ToolOrder(CompanionTool tool)
    {
        return tool switch
        {
            CompanionTool.FinancialSummary => 10,
            CompanionTool.BudgetStatus => 20,
            CompanionTool.SpendingAnalysis => 30,
            CompanionTool.RecurringObligations => 40,
            CompanionTool.TransactionQuery => 50,
            CompanionTool.PlacesSearch => 60,
            CompanionTool.PlaceDetails => 70,
            CompanionTool.ReviewInsights => 80,
            _ => 100
        };
    }

    private static string? ClampText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        var effectiveMax = Math.Clamp(maxLength, 12, MaxSummaryTextLength);
        if (trimmed.Length <= effectiveMax)
        {
            return trimmed;
        }

        return trimmed[..effectiveMax];
    }

    private enum CompanionTool
    {
        FinancialSummary = 0,
        SpendingAnalysis = 1,
        RecurringObligations = 2,
        BudgetStatus = 3,
        TransactionQuery = 4,
        PlacesSearch = 5,
        PlaceDetails = 6,
        ReviewInsights = 7
    }

    private sealed record CompanionIntentToolPolicy(
        IReadOnlyList<CompanionTool> Required,
        IReadOnlyList<CompanionTool> Optional);

    private sealed record CompanionToolExecutionPlan(
        IReadOnlyList<CompanionTool> Required,
        IReadOnlyList<CompanionTool> Optional);

    private sealed record ToolExecutionOutcome(
        CompanionTool Tool,
        bool Succeeded,
        string OutputKey,
        object? Output,
        bool HasSufficientData,
        string? WarningReason = null)
    {
        public static ToolExecutionOutcome Success(
            CompanionTool tool,
            string outputKey,
            object? output,
            bool hasSufficientData,
            string? warningReason = null)
        {
            return new ToolExecutionOutcome(
                Tool: tool,
                Succeeded: true,
                OutputKey: outputKey,
                Output: output,
                HasSufficientData: hasSufficientData,
                WarningReason: warningReason);
        }

        public static ToolExecutionOutcome Failed(CompanionTool tool, string? warningReason = null)
        {
            return new ToolExecutionOutcome(
                Tool: tool,
                Succeeded: false,
                OutputKey: string.Empty,
                Output: null,
                HasSufficientData: false,
                WarningReason: warningReason);
        }
    }
}

public sealed record CompanionFinancialSummaryContext(
    decimal IncomeLast30Days,
    decimal SpendLast30Days,
    decimal NetLast30Days,
    string Currency);

public sealed record CompanionSpendingAnalysisContext(
    IReadOnlyList<CompanionDomainSpendContextItem> TopDomainSpend,
    int DomainCount,
    decimal AverageDailySpend,
    decimal LargestExpense);

public sealed record CompanionDomainSpendContextItem(
    int DomainCode,
    decimal Amount);

public sealed record CompanionRecurringObligationsContext(
    int TotalItemCount,
    decimal EstimatedMonthlyTotal,
    IReadOnlyList<CompanionRecurringItemContext> TopItems);

public sealed record CompanionRecurringItemContext(
    string? Name,
    decimal Amount,
    string Currency,
    int FrequencyDays);

public sealed record CompanionBudgetStatusContext(
    bool HasBudgetPlan,
    decimal? MonthlyBudget,
    decimal MonthToDateSpend,
    decimal? RemainingBudget);

public sealed record CompanionTransactionMatchesContext(
    int TotalItemCount,
    IReadOnlyList<CompanionTransactionMatchContext> Items);

public sealed record CompanionTransactionMatchContext(
    DateTime BookedDateUtc,
    decimal Amount,
    string Currency,
    string? Description,
    int? DomainCode,
    int? CategoryCode);

public sealed record CompanionPlacesSearchContext(
    int TotalItemCount,
    IReadOnlyList<CompanionPlaceSearchContextItem> Items);

public sealed record CompanionPlaceSearchContextItem(
    string? PlaceId,
    string? Name,
    string? Category,
    string? PriceLevel);

public sealed record CompanionPlaceDetailsContext(
    string? PlaceId,
    string? Name,
    string? Address,
    string? Website,
    string? PriceLevel);

public sealed record CompanionReviewInsightsContext(
    string? PlaceId,
    string? Summary,
    double? AverageRating);
