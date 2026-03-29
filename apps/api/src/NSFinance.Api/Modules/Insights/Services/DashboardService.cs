using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Insights.DTOs;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.TransferPolicy;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

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

        var recentOutflowCandidates = await transactionQuery
            .Where(x => x.BookedAtUtc >= thirtyDaysAgo && x.Amount < 0)
            .Select(x => new
            {
                x.Amount,
                x.TaxonomyDomainId,
                x.TaxonomyCategoryId,
                x.TaxonomySubcategoryId,
                x.TransferKind,
                x.LinkedTransferTransactionId
            })
            .ToListAsync(cancellationToken);

        var recentOutflow = recentOutflowCandidates
            .Where(x =>
                TransferPolicyEngine.Evaluate(
                    x.TaxonomyDomainId,
                    x.TaxonomyCategoryId,
                    x.TaxonomySubcategoryId,
                    x.TransferKind,
                    x.LinkedTransferTransactionId,
                    x.Amount).CountsTowardExpense)
            .Sum(x => Math.Abs(x.Amount));

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
                x.TransferKind,
                x.LinkedTransferTransactionId,
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
                var transferPolicy = TransferPolicyEngine.Evaluate(
                    x.TaxonomyDomainId,
                    x.TaxonomyCategoryId,
                    x.TaxonomySubcategoryId,
                    x.TransferKind,
                    x.LinkedTransferTransactionId,
                    x.Amount);

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
                    x.TransferKind == TransactionTransferKind.Manual
                        ? "manual_transfer"
                        : x.TransferKind == TransactionTransferKind.LinkedInternal
                            ? "linked_internal_transfer"
                            : null,
                    x.LinkedTransferTransactionId,
                    MapTransferPolicyKind(transferPolicy.PolicyKind),
                    transferPolicy.ReportingBucket.ToString().ToLowerInvariant(),
                    transferPolicy.IsGloballyNeutralized,
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

    private static string MapTransferPolicyKind(TransferPolicyKind policyKind)
    {
        return policyKind switch
        {
            TransferPolicyKind.None => "none",
            TransferPolicyKind.InternalTransferGeneric => "internal_transfer_generic",
            TransferPolicyKind.BankAccountTransfer => "bank_account_transfer",
            TransferPolicyKind.SavingsTransfer => "savings_transfer",
            TransferPolicyKind.InvestmentTransfer => "investment_transfer",
            TransferPolicyKind.WalletTransfer => "wallet_transfer",
            TransferPolicyKind.CreditCardPaymentTransfer => "credit_card_payment_transfer",
            TransferPolicyKind.LoanAccountTransfer => "loan_account_transfer",
            TransferPolicyKind.DebtConsolidationTransfer => "debt_consolidation_transfer",
            TransferPolicyKind.CashMovementGeneric => "cash_movement_generic",
            TransferPolicyKind.CashWithdrawal => "cash_withdrawal",
            TransferPolicyKind.CashDeposit => "cash_deposit",
            TransferPolicyKind.AtmWithdrawalTransfer => "atm_withdrawal_transfer",
            TransferPolicyKind.LiabilityTransferGeneric => "liability_transfer_generic",
            TransferPolicyKind.BrokerageFundingTransfer => "brokerage_funding_transfer",
            TransferPolicyKind.CurrencyTransfer => "currency_transfer",
            TransferPolicyKind.OtherInternalMoneyMovement => "other_internal_money_movement",
            TransferPolicyKind.OtherTransferGeneric => "other_transfer_generic",
            _ => "none"
        };
    }
}
