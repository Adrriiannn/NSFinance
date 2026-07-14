using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Transactions.DTOs;

namespace NSFinance.Api.Modules.Insights.DTOs;

public sealed record DashboardSummaryDto(
    decimal TotalBalance,
    int AccountCount,
    int TransactionCount,
    decimal RecentOutflow,
    IReadOnlyList<AccountDto> AccountPreview,
    IReadOnlyList<TransactionDto> RecentTransactions,
    PortfolioBalanceDto? PortfolioBalance = null);
