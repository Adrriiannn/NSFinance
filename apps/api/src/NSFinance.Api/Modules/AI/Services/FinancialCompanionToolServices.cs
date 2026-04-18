using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class UserFinancialSummaryService(AppDbContext dbContext) : IUserFinancialSummaryService
{
    public async Task<UserFinancialSummary> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var windowStartUtc = DateTime.UtcNow.AddDays(-30);
        var rows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.FinancialAccount != null && x.FinancialAccount.UserId == userId)
            .Where(x => x.BookedAtUtc >= windowStartUtc)
            .Select(x => new { x.Amount, x.Currency })
            .ToListAsync(cancellationToken);

        var currency = rows.Select(x => x.Currency).FirstOrDefault() ?? "EUR";
        var income = rows.Where(x => x.Amount > 0m).Sum(x => x.Amount);
        var spend = Math.Abs(rows.Where(x => x.Amount < 0m).Sum(x => x.Amount));
        return new UserFinancialSummary(
            IncomeLast30Days: income,
            SpendLast30Days: spend,
            NetLast30Days: income - spend,
            Currency: currency);
    }
}

public sealed class SpendingAnalysisService(AppDbContext dbContext) : ISpendingAnalysisService
{
    public async Task<SpendingAnalysisResult> AnalyzeAsync(Guid userId, int lookbackDays, CancellationToken cancellationToken)
    {
        var windowStartUtc = DateTime.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var rows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.FinancialAccount != null && x.FinancialAccount.UserId == userId)
            .Where(x => x.BookedAtUtc >= windowStartUtc && x.Amount < 0m)
            .Select(x => new { x.Amount, x.TaxonomyDomainId })
            .ToListAsync(cancellationToken);

        var spendByDomain = rows
            .GroupBy(x => x.TaxonomyDomainId ?? 0)
            .ToDictionary(
                x => x.Key,
                x => Math.Abs(x.Sum(y => y.Amount)));
        var spendTotal = spendByDomain.Values.Sum();
        var averageDailySpend = spendTotal / Math.Max(1, lookbackDays);
        var largestExpense = rows.Count == 0 ? 0m : Math.Abs(rows.Min(x => x.Amount));
        return new SpendingAnalysisResult(
            SpendByDomain: spendByDomain,
            AverageDailySpend: averageDailySpend,
            LargestExpense: largestExpense);
    }
}

public sealed class RecurringObligationsService(AppDbContext dbContext) : IRecurringObligationsService
{
    public async Task<RecurringObligationsResult> GetRecurringAsync(Guid userId, CancellationToken cancellationToken)
    {
        var windowStartUtc = DateTime.UtcNow.AddDays(-120);
        var rows = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.FinancialAccount != null && x.FinancialAccount.UserId == userId)
            .Where(x => x.BookedAtUtc >= windowStartUtc && x.Amount < 0m)
            .Select(x => new { x.BookedAtUtc, x.Amount, x.Description, x.Currency })
            .ToListAsync(cancellationToken);

        var groups = rows
            .GroupBy(x => NormalizeDescriptor(x.Description))
            .Where(x => x.Count() >= 2)
            .Select(x =>
            {
                var ordered = x.OrderBy(y => y.BookedAtUtc).ToList();
                var averageAmount = Math.Abs(x.Average(y => y.Amount));
                var avgFrequency = ComputeAverageFrequencyDays(ordered.Select(y => y.BookedAtUtc).ToList());
                return new RecurringObligationItem(
                    Name: ordered.Last().Description,
                    Amount: decimal.Round(averageAmount, 2, MidpointRounding.AwayFromZero),
                    Currency: ordered.Last().Currency,
                    FrequencyDays: avgFrequency);
            })
            .OrderByDescending(x => x.Amount)
            .Take(12)
            .ToList();

        var monthlyTotal = groups.Sum(item =>
        {
            var normalizedFrequency = Math.Max(1, item.FrequencyDays);
            return item.Amount * (30m / normalizedFrequency);
        });

        return new RecurringObligationsResult(groups, decimal.Round(monthlyTotal, 2, MidpointRounding.AwayFromZero));
    }

    private static int ComputeAverageFrequencyDays(IReadOnlyList<DateTime> orderedTimestamps)
    {
        if (orderedTimestamps.Count < 2)
        {
            return 30;
        }

        var dayDiffs = new List<double>(orderedTimestamps.Count - 1);
        for (var i = 1; i < orderedTimestamps.Count; i++)
        {
            dayDiffs.Add(Math.Max(1d, (orderedTimestamps[i] - orderedTimestamps[i - 1]).TotalDays));
        }

        var avg = dayDiffs.Average();
        return Math.Max(1, (int)Math.Round(avg, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeDescriptor(string descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return "_empty";
        }

        var cleaned = descriptor.Trim().ToLowerInvariant();
        var chars = cleaned.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray();
        return new string(chars);
    }
}

public sealed class BudgetStatusService(AppDbContext dbContext) : IBudgetStatusService
{
    public async Task<BudgetStatusResult> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var currentMonthStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthToDateSpend = Math.Abs(await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.FinancialAccount != null && x.FinancialAccount.UserId == userId)
            .Where(x => x.BookedAtUtc >= currentMonthStartUtc && x.Amount < 0m)
            .SumAsync(x => x.Amount, cancellationToken));

        var activePlan = await dbContext.ExpensePlans
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Where(x => x.Status == "active" || x.ActivatedAtUtc.HasValue)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activePlan is null)
        {
            return new BudgetStatusResult(
                HasBudgetPlan: false,
                MonthlyBudget: null,
                MonthToDateSpend: monthToDateSpend,
                RemainingBudget: null);
        }

        var monthlyBudget = Math.Abs(activePlan.ExpectedSpendTotal);
        return new BudgetStatusResult(
            HasBudgetPlan: true,
            MonthlyBudget: monthlyBudget,
            MonthToDateSpend: monthToDateSpend,
            RemainingBudget: monthlyBudget - monthToDateSpend);
    }
}

public sealed class TransactionQueryService(AppDbContext dbContext) : ITransactionQueryService
{
    public async Task<TransactionQueryResult> QueryAsync(
        Guid userId,
        string query,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var tokens = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2)
            .Take(8)
            .ToArray();

        var baseQuery = dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.FinancialAccount != null && x.FinancialAccount.UserId == userId);
        foreach (var token in tokens)
        {
            var local = token;
            baseQuery = baseQuery.Where(x => x.Description.Contains(local));
        }

        var rows = await baseQuery
            .OrderByDescending(x => x.BookedAtUtc)
            .Take(Math.Clamp(maxRows, 1, 100))
            .Select(x => new TransactionQueryItem(
                x.BookedAtUtc,
                x.Amount,
                x.Currency,
                x.Description,
                x.TaxonomyDomainId,
                x.TaxonomyCategoryId))
            .ToListAsync(cancellationToken);
        return new TransactionQueryResult(rows);
    }
}

public sealed class NullPlacesSearchService : IPlacesSearchService
{
    public Task<PlaceSearchResult> SearchAsync(string query, string country, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PlaceSearchResult([]));
    }
}

public sealed class NullPlaceDetailsService : IPlaceDetailsService
{
    public Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PlaceDetailsResult(placeId, string.Empty, null, null, null));
    }
}

public sealed class NullReviewInsightsService : IReviewInsightsService
{
    public Task<ReviewInsightsResult> GetInsightsAsync(string placeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ReviewInsightsResult(placeId, string.Empty, null));
    }
}
