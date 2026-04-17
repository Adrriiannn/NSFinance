namespace NSFinance.Api.Modules.AI.Services;

public enum FinancialCompanionIntent
{
    SpendingAnalysis = 0,
    SavingsCutbackAdvice = 1,
    Affordability = 2,
    BudgetStatus = 3,
    PlanProgress = 4,
    LocalPlacesOutings = 5,
    GeneralFinancialQuestion = 6,
    MixedQuery = 7,
    Ambiguous = 8,
    Unsupported = 9
}

public sealed record CompanionIntentRoutingResult(
    FinancialCompanionIntent IntentFamily,
    FinancialCompanionIntent PrimaryIntent,
    IReadOnlyList<FinancialCompanionIntent> SecondaryIntents,
    double Confidence,
    IReadOnlyList<string> ReasonCodes,
    bool IsAmbiguous,
    bool IsUnsupported);

public sealed record FinancialCompanionRequest(
    Guid UserId,
    string SessionId,
    string UserQuery,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string CorrelationId = "");

public sealed record FinancialCompanionResponse(
    string ReplyText,
    FinancialCompanionIntent Intent,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<string> Warnings,
    bool Succeeded,
    string? FailureReason,
    string ModelUsed,
    int InputTokens,
    int OutputTokens,
    CompanionResponseEvidence? Evidence = null,
    bool HasInsufficientData = false,
    IReadOnlyList<string>? InsufficientDataReasons = null);

public sealed record FinancialCompanionContext(
    FinancialCompanionIntent Intent,
    UserFinancialContextSnapshot Profile,
    IReadOnlyDictionary<string, object?> ToolOutputs,
    IReadOnlyList<string> ToolsUsed,
    CompanionContextEvidence? Evidence = null);

public sealed record CompanionContextEvidence(
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<string> RequiredToolsUsed,
    IReadOnlyList<string> OptionalToolsUsed,
    IReadOnlyList<string> MissingRequiredTools,
    IReadOnlyList<string> BasisSummary,
    IReadOnlyList<string> SkippedTools);

public sealed record CompanionResponseEvidence(
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<string> RequiredToolsUsed,
    IReadOnlyList<string> OptionalToolsUsed,
    IReadOnlyList<string> MissingRequiredTools,
    IReadOnlyList<string> BasisSummary,
    IReadOnlyList<string> SkippedTools);

public sealed record UserFinancialContextSnapshot(
    string Country,
    string Currency,
    string? MonthlyIncomeRange,
    string KnownObligationsJson,
    string BudgetStructureJson,
    string ActivePlansJson,
    string SpendingTendenciesJson,
    string CategoryFlexibilityMarkersJson,
    string AdviceStylePreference);
