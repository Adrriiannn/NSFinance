namespace NSFinance.Api.Modules.AI.Services;

public enum CompanionTool
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

public enum CompanionToolExecutionStatus
{
    Success = 0,
    Failed = 1,
    NoData = 2,
    SkippedPlan = 3,
    SkippedCap = 4,
    SkippedContextCap = 5,
    SkippedDependency = 6,
    TrimmedOut = 7
}

public sealed record CompanionIntentToolPolicy(
    FinancialCompanionIntent Intent,
    IReadOnlyList<CompanionTool> RequiredTools,
    IReadOnlyList<CompanionTool> OptionalTools,
    IReadOnlyList<CompanionTool> DisallowedTools);

public sealed record CompanionPlannedTool(
    CompanionTool Tool,
    bool IsRequired,
    int Order,
    string InclusionReason,
    IReadOnlyList<FinancialCompanionIntent> SourceIntents);

public sealed record CompanionSkippedToolDecision(
    CompanionTool Tool,
    string ReasonCode,
    IReadOnlyList<FinancialCompanionIntent> SourceIntents);

public sealed record CompanionExecutionPlan(
    IReadOnlyList<CompanionPlannedTool> PlannedTools,
    IReadOnlyList<CompanionSkippedToolDecision> SkippedTools,
    IReadOnlyList<string> Warnings);

public sealed record CompanionMixedIntentMergeResult(
    IReadOnlyList<CompanionTool> AddedOptionalTools,
    IReadOnlyList<CompanionSkippedToolDecision> SkippedTools);

public sealed record CompanionToolExecutionRecord(
    CompanionPlannedTool PlannedTool,
    CompanionToolExecutionStatus Status,
    string ContractName,
    string OutputKey,
    object? Output,
    string? ReasonCode,
    IReadOnlyList<string> Warnings,
    bool IncludedInContext);

public sealed record CompanionToolExecutionResult(
    IReadOnlyDictionary<string, object?> ContextOutputs,
    IReadOnlyList<CompanionToolExecutionRecord> Records,
    IReadOnlyList<string> Warnings);

public sealed record CompanionContextTrimResult(
    IReadOnlyDictionary<string, object?> Outputs,
    IReadOnlyList<string> TrimmedIndicators,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CompanionToolExecutionRecord> AdjustedRecords);

public sealed record CompanionInsufficiencyDecision(
    bool CanProceedToAI,
    bool HasInsufficientData,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> MissingRequiredTools,
    IReadOnlyList<string> Warnings);

public static class CompanionOrchestrationReasonCodes
{
    public const string AmbiguousQueryRequiresClarification = "ambiguous_query_requires_clarification";
    public const string UnsupportedQueryScope = "unsupported_query_scope";
    public const string RequiredToolFailedPrefix = "required_tool_failed";
    public const string RequiredToolReturnedNoDataPrefix = "required_tool_returned_no_data";
    public const string OptionalToolFailedPrefix = "optional_tool_failed";
    public const string OptionalToolReturnedNoDataPrefix = "optional_tool_returned_no_data";
    public const string CapExceededOrSkippedPrefix = "cap_exceeded_or_skipped";
    public const string PayloadTrimmed = "payload_trimmed";
    public const string GroundingIncomplete = "grounding_incomplete";
    public const string ProviderUnavailablePrefix = "provider_unavailable";
    public const string TimeoutOrCancellationPrefix = "timeout_or_cancellation";

    public static string WithTool(string prefix, CompanionTool tool)
    {
        return $"{prefix}:{ToReasonSuffix(tool)}";
    }

    public static string ToReasonSuffix(CompanionTool tool)
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
}

public static class CompanionToolMetadata
{
    public static string ToContractName(this CompanionTool tool)
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

    public static string ToOutputKey(this CompanionTool tool)
    {
        return tool switch
        {
            CompanionTool.FinancialSummary => "financial_summary",
            CompanionTool.SpendingAnalysis => "spending_analysis",
            CompanionTool.RecurringObligations => "recurring_obligations",
            CompanionTool.BudgetStatus => "budget_status",
            CompanionTool.TransactionQuery => "transaction_matches",
            CompanionTool.PlacesSearch => "place_search",
            CompanionTool.PlaceDetails => "place_details",
            CompanionTool.ReviewInsights => "review_insights",
            _ => tool.ToString().ToLowerInvariant()
        };
    }

    public static int ToExecutionOrder(this CompanionTool tool)
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

    public static int ToOptionalPriority(this CompanionTool tool)
    {
        return tool switch
        {
            CompanionTool.BudgetStatus => 10,
            CompanionTool.SpendingAnalysis => 20,
            CompanionTool.RecurringObligations => 30,
            CompanionTool.TransactionQuery => 40,
            CompanionTool.PlacesSearch => 50,
            CompanionTool.PlaceDetails => 60,
            CompanionTool.ReviewInsights => 70,
            _ => 100
        };
    }
}
