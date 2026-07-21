using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Insights.DTOs;
using NSFinance.Api.Modules.Transactions.TransferPolicy;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Insights.Services;

// The category bars behind the Insights surface (INS-001): per-month spend by
// taxonomy category, computed under the same transfer-policy honesty as the
// periods series. Only expense-bucket outflows count; the uncategorized
// remainder is reported, never hidden.
public sealed class InsightCategoryBreakdownService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    ExpenseTaxonomyService expenseTaxonomyService)
{
    private sealed record OutflowRow(
        decimal Amount,
        string Currency,
        int Year,
        int Month,
        int? TaxonomyDomainId,
        int? TaxonomyCategoryId);

    public async Task<InsightCategoryBreakdownDto> GetMonthlyBreakdownAsync(
        int? monthsRequested,
        CancellationToken cancellationToken)
    {
        var months = Math.Clamp(
            monthsRequested ?? InsightPeriodsService.DefaultMonths,
            1,
            InsightPeriodsService.MaxMonths);
        var nowUtc = DateTime.UtcNow;
        var windowStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(-(months - 1));

        var candidates = await dbContext.Transactions
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccount != null
                && x.FinancialAccount.UserId == currentUserProvider.UserId
                && x.AnalyticsTreatment == TransactionAnalyticsTreatments.Ordinary
                && x.Amount < 0
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

        var countedOutflows = candidates
            .Where(x => TransferPolicyEngine.Evaluate(
                    x.TaxonomyDomainId,
                    x.TaxonomyCategoryId,
                    x.TaxonomySubcategoryId,
                    x.TransferKind,
                    x.LinkedTransferTransactionId,
                    x.Amount,
                    x.DeterministicClassificationStatus,
                    x.DeterministicRelationshipType,
                    x.DeterministicLinkedTransactionId)
                .CountsTowardExpense)
            .Select(x => new OutflowRow(
                x.Amount,
                x.Currency,
                x.BookedAtUtc.Year,
                x.BookedAtUtc.Month,
                x.TaxonomyDomainId,
                x.TaxonomyCategoryId))
            .ToList();

        var groups = countedOutflows
            .GroupBy(x => x.Currency, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(currencyGroup => new InsightCategoryCurrencyGroupDto(
                currencyGroup.Key,
                BuildPeriodSeries(currencyGroup.ToList(), windowStart, months, nowUtc)))
            .ToList();

        return new InsightCategoryBreakdownDto(nowUtc, months, groups);
    }

    private List<InsightCategoryPeriodDto> BuildPeriodSeries(
        IReadOnlyList<OutflowRow> rows,
        DateTime windowStart,
        int months,
        DateTime nowUtc)
    {
        var series = new List<InsightCategoryPeriodDto>(months);

        for (var offset = 0; offset < months; offset += 1)
        {
            var periodStart = windowStart.AddMonths(offset);
            var isCurrentMonth = periodStart.Year == nowUtc.Year && periodStart.Month == nowUtc.Month;
            var periodRows = rows
                .Where(x => x.Year == periodStart.Year && x.Month == periodStart.Month)
                .ToList();

            var categorized = periodRows
                .Where(x => x.TaxonomyCategoryId is not null)
                .GroupBy(x => x.TaxonomyCategoryId!.Value)
                .Select(categoryGroup =>
                {
                    var domainId = categoryGroup.First().TaxonomyDomainId ?? 0;
                    return new InsightCategorySpendDto(
                        domainId,
                        expenseTaxonomyService.GetDomainName(domainId) ?? "Unknown",
                        categoryGroup.Key,
                        expenseTaxonomyService.GetCategoryName(categoryGroup.Key) ?? "Unknown",
                        categoryGroup.Sum(x => Math.Abs(x.Amount)),
                        categoryGroup.Count());
                })
                .OrderByDescending(x => x.Spend)
                .ThenBy(x => x.CategoryName, StringComparer.Ordinal)
                .ToList();

            var categorizedSpend = categorized.Sum(x => x.Spend);
            var uncategorizedRows = periodRows.Where(x => x.TaxonomyCategoryId is null).ToList();
            var uncategorizedSpend = uncategorizedRows.Sum(x => Math.Abs(x.Amount));

            series.Add(new InsightCategoryPeriodDto(
                periodStart.Year,
                periodStart.Month,
                categorizedSpend + uncategorizedSpend,
                categorizedSpend,
                uncategorizedSpend,
                uncategorizedRows.Count,
                isCurrentMonth,
                categorized));
        }

        return series;
    }
}
