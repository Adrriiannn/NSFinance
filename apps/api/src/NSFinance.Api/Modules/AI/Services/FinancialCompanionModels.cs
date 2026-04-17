namespace NSFinance.Api.Modules.AI.Services;

public enum FinancialCompanionIntent
{
    GeneralQuestion = 0,
    Budgeting = 1,
    SavingsAdvice = 2,
    Affordability = 3,
    LifestylePlaces = 4
}

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
    int OutputTokens);

public sealed record FinancialCompanionContext(
    FinancialCompanionIntent Intent,
    UserFinancialContextSnapshot Profile,
    IReadOnlyDictionary<string, object?> ToolOutputs,
    IReadOnlyList<string> ToolsUsed);

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
