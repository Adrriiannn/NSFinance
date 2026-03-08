using NSFinTech.Api.Modules.Accounts.DTOs;
using NSFinTech.Api.Modules.Transactions.DTOs;

namespace NSFinTech.Api.Modules.Insights.DTOs;

public sealed record DashboardSummaryDto(
    decimal TotalBalance,
    int AccountCount,
    int TransactionCount,
    decimal RecentOutflow,
    IReadOnlyList<AccountDto> AccountPreview,
    IReadOnlyList<TransactionDto> RecentTransactions);
