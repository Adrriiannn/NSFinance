namespace NSFinance.Api.Modules.AI.Services;

public interface IUserFinancialSummaryService
{
    Task<UserFinancialSummary> GetSummaryAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ISpendingAnalysisService
{
    Task<SpendingAnalysisResult> AnalyzeAsync(Guid userId, int lookbackDays, CancellationToken cancellationToken);
}

public interface IRecurringObligationsService
{
    Task<RecurringObligationsResult> GetRecurringAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IBudgetStatusService
{
    Task<BudgetStatusResult> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ITransactionQueryService
{
    Task<TransactionQueryResult> QueryAsync(Guid userId, string query, int maxRows, CancellationToken cancellationToken);
}

public interface IUserFinancialContextProfileService
{
    Task<UserFinancialContextSnapshot> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IPlacesSearchService
{
    Task<PlaceSearchResult> SearchAsync(string query, string country, CancellationToken cancellationToken);
}

public interface IPlaceDetailsService
{
    Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken);
}

public interface IReviewInsightsService
{
    Task<ReviewInsightsResult> GetInsightsAsync(string placeId, CancellationToken cancellationToken);
}

public sealed record UserFinancialSummary(
    decimal IncomeLast30Days,
    decimal SpendLast30Days,
    decimal NetLast30Days,
    string Currency);

public sealed record SpendingAnalysisResult(
    IReadOnlyDictionary<int, decimal> SpendByDomain,
    decimal AverageDailySpend,
    decimal LargestExpense);

public sealed record RecurringObligationsResult(
    IReadOnlyList<RecurringObligationItem> Items,
    decimal EstimatedMonthlyTotal);

public sealed record RecurringObligationItem(
    string Name,
    decimal Amount,
    string Currency,
    int FrequencyDays);

public sealed record BudgetStatusResult(
    bool HasBudgetPlan,
    decimal? MonthlyBudget,
    decimal MonthToDateSpend,
    decimal? RemainingBudget);

public sealed record TransactionQueryResult(
    IReadOnlyList<TransactionQueryItem> Items);

public sealed record TransactionQueryItem(
    DateTime BookedAtUtc,
    decimal Amount,
    string Currency,
    string Description,
    int? DomainCode,
    int? CategoryCode);

public sealed record PlaceSearchResult(
    IReadOnlyList<PlaceSearchItem> Items);

public sealed record PlaceSearchItem(
    string PlaceId,
    string Name,
    string? Category,
    string? PriceLevel);

public sealed record PlaceDetailsResult(
    string PlaceId,
    string Name,
    string? Address,
    string? Website,
    string? PriceLevel);

public sealed record ReviewInsightsResult(
    string PlaceId,
    string Summary,
    double? AverageRating);
