using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Insights.DTOs;
using NSFinance.Api.Modules.Transactions.TransferPolicy;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Insights.Services;

public sealed class InsightPeriodsService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider)
{
    public const int DefaultMonths = 6;
    public const int MaxMonths = 24;

    public async Task<InsightPeriodsDto> GetMonthlyPeriodsAsync(
        int? monthsRequested,
        CancellationToken cancellationToken)
    {
        var months = Math.Clamp(monthsRequested ?? DefaultMonths, 1, MaxMonths);
        var nowUtc = DateTime.UtcNow;
        var windowStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(-(months - 1));

        var candidates = await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccount != null
                && x.FinancialAccount.UserId == currentUserProvider.UserId
                && x.AnalyticsTreatment == TransactionAnalyticsTreatments.Ordinary
                && x.BookedAtUtc >= windowStart)
            .Select(x => new
            {
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                x.TaxonomyDomainId,
                x.TaxonomyCategoryId,
                x.TaxonomySubcategoryId,
                x.TransferKind,
                x.LinkedTransferTransactionId,
                x.DeterministicClassificationStatus,
                x.DeterministicRelationshipType,
                x.DeterministicLinkedTransactionId
            })
            .ToListAsync(cancellationToken);

        var groups = candidates
            .Select(x => new
            {
                x.Currency,
                x.BookedAtUtc.Year,
                x.BookedAtUtc.Month,
                x.Amount,
                Policy = TransferPolicyEngine.Evaluate(
                    x.TaxonomyDomainId,
                    x.TaxonomyCategoryId,
                    x.TaxonomySubcategoryId,
                    x.TransferKind,
                    x.LinkedTransferTransactionId,
                    x.Amount,
                    x.DeterministicClassificationStatus,
                    x.DeterministicRelationshipType,
                    x.DeterministicLinkedTransactionId)
            })
            .Where(x =>
                (x.Amount > 0 && x.Policy.CountsTowardIncome)
                || (x.Amount < 0 && x.Policy.CountsTowardExpense))
            .GroupBy(x => x.Currency, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(currencyGroup => new InsightPeriodCurrencyGroupDto(
                currencyGroup.Key,
                BuildPeriodSeries(currencyGroup
                    .GroupBy(x => (x.Year, x.Month))
                    .ToDictionary(
                        periodGroup => periodGroup.Key,
                        periodGroup => (
                            Income: periodGroup.Where(x => x.Amount > 0).Sum(x => x.Amount),
                            Spend: periodGroup.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount)),
                            Count: periodGroup.Count())),
                    windowStart,
                    months,
                    nowUtc)))
            .ToList();

        return new InsightPeriodsDto(nowUtc, months, groups);
    }

    private static List<InsightPeriodDto> BuildPeriodSeries(
        IReadOnlyDictionary<(int Year, int Month), (decimal Income, decimal Spend, int Count)> byPeriod,
        DateTime windowStart,
        int months,
        DateTime nowUtc)
    {
        var series = new List<InsightPeriodDto>(months);

        for (var offset = 0; offset < months; offset += 1)
        {
            var periodStart = windowStart.AddMonths(offset);
            var key = (periodStart.Year, periodStart.Month);
            var isCurrentMonth = periodStart.Year == nowUtc.Year && periodStart.Month == nowUtc.Month;
            var totals = byPeriod.TryGetValue(key, out var found)
                ? found
                : (Income: 0m, Spend: 0m, Count: 0);

            series.Add(new InsightPeriodDto(
                periodStart.Year,
                periodStart.Month,
                totals.Income,
                totals.Spend,
                totals.Income - totals.Spend,
                totals.Count,
                isCurrentMonth));
        }

        return series;
    }
}
