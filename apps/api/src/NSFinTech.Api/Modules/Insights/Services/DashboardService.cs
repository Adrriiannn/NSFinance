using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Modules.Accounts.DTOs;
using NSFinTech.Api.Modules.Insights.DTOs;
using NSFinTech.Api.Modules.Transactions.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;

namespace NSFinTech.Api.Modules.Insights.Services;

public sealed class DashboardService(AppDbContext dbContext, ICurrentUserProvider currentUserProvider)
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
                x.CreatedUtc))
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
            .Select(x => new TransactionDto(
                x.Id,
                x.FinancialAccountId,
                x.FinancialAccount != null ? x.FinancialAccount.Name : string.Empty,
                x.Description,
                x.Amount,
                x.Currency,
                x.CategoryId,
                x.Category != null ? x.Category.Name : null,
                x.BookedAtUtc,
                x.CreatedUtc,
                x.Amount < 0 ? "Expense" : "Income"))
            .ToListAsync(cancellationToken);

        return new DashboardSummaryDto(
            TotalBalance: accounts.Sum(x => x.CurrentBalance),
            AccountCount: accounts.Count,
            TransactionCount: transactionCount,
            RecentOutflow: recentOutflow,
            AccountPreview: accounts.Take(3).ToList(),
            RecentTransactions: recentTransactions);
    }
}
