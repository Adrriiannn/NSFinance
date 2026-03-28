using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Insights.DTOs;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Insights.Services;

public sealed class DashboardService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    ExpenseTaxonomyService expenseTaxonomyService)
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.UserId == currentUserProvider.UserId)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m,
                x.Transactions.Count,
                x.CreatedUtc,
                null,
                null,
                null,
                null,
                null,
                false))
            .ToListAsync(cancellationToken);

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var transactionQuery = dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.FinancialAccount != null && x.FinancialAccount.UserId == currentUserProvider.UserId);

        var transactionCount = await transactionQuery.CountAsync(cancellationToken);

        var recentOutflow = await transactionQuery
            .Where(x => x.BookedAtUtc >= thirtyDaysAgo && x.Amount < 0)
            .SumAsync(x => Math.Abs(x.Amount), cancellationToken);

        var recentTransactions = await transactionQuery
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Take(5)
            .Select(x => new
            {
                x.Id,
                x.FinancialAccountId,
                AccountName = x.FinancialAccount != null ? x.FinancialAccount.Name : string.Empty,
                x.Description,
                x.Amount,
                x.Currency,
                x.CategoryId,
                LegacyCategoryName = x.Category != null ? x.Category.Name : null,
                x.TaxonomyDomainId,
                x.TaxonomyCategoryId,
                x.TaxonomySubcategoryId,
                x.Reason,
                x.Notes,
                x.BookedAtUtc,
                x.CreatedUtc,
                x.MetadataUpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var recentTransactionDtos = recentTransactions
            .Select(x =>
            {
                var taxonomyDomainName = expenseTaxonomyService.GetDomainName(x.TaxonomyDomainId);
                var taxonomyCategoryName = expenseTaxonomyService.GetCategoryName(x.TaxonomyCategoryId);
                var taxonomySubcategoryName = expenseTaxonomyService.GetSubcategoryName(x.TaxonomySubcategoryId);
                var categoryName = taxonomyCategoryName ?? x.LegacyCategoryName;

                return new TransactionDto(
                    x.Id,
                    x.FinancialAccountId,
                    x.AccountName,
                    x.Description,
                    x.Amount,
                    x.Currency,
                    x.CategoryId,
                    categoryName,
                    x.TaxonomyDomainId,
                    taxonomyDomainName,
                    x.TaxonomyCategoryId,
                    taxonomyCategoryName,
                    x.TaxonomySubcategoryId,
                    taxonomySubcategoryName,
                    x.Reason,
                    x.Notes,
                    x.BookedAtUtc,
                    x.CreatedUtc,
                    x.MetadataUpdatedUtc,
                    x.Amount < 0 ? "Expense" : "Income");
            })
            .ToList();

        return new DashboardSummaryDto(
            TotalBalance: accounts.Sum(x => x.CurrentBalance),
            AccountCount: accounts.Count,
            TransactionCount: transactionCount,
            RecentOutflow: recentOutflow,
            AccountPreview: accounts.Take(3).ToList(),
            RecentTransactions: recentTransactionDtos);
    }
}
