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
                x.TransferMatchConfidenceScore,
                x.TransferMatchConfidenceTier,
                x.TransferMatchReason,
                x.Reason,
                x.Notes,
                x.BookedAtUtc,
                x.CreatedUtc,
                x.MetadataUpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var recentTransactionIds = recentTransactions.Select(x => x.Id).ToArray();
        var relationshipSummariesByTransactionId = await GetRelationshipSummariesByTransactionIdAsync(
            recentTransactionIds,
            cancellationToken);

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
                relationshipSummariesByTransactionId.TryGetValue(x.Id, out var relationshipSummary);

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
                    MapTransferKind(x.TransferKind),
                    x.LinkedTransferTransactionId,
                    x.TransferMatchConfidenceScore,
                    x.TransferMatchConfidenceTier,
                    x.TransferMatchReason,
                    relationshipSummary is null ? null : MapRelationshipType(relationshipSummary.RelationshipType),
                    relationshipSummary is null ? null : MapRelationshipStatus(relationshipSummary.RelationshipStatus),
                    relationshipSummary is null ? null : MapRelationshipDirection(relationshipSummary.RelationshipDirection),
                    relationshipSummary?.ConfidenceScore,
                    relationshipSummary?.ConfidenceTier,
                    relationshipSummary?.AnalyticsTreatment,
                    relationshipSummary?.VirtualDestinationLabel,
                    relationshipSummary?.CounterpartyTransactionId,
                    ResolveDisplaySemantic(x.TransferKind, relationshipSummary),
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

    private static string? MapTransferKind(TransactionTransferKind? transferKind)
    {
        return transferKind switch
        {
            TransactionTransferKind.Manual => "manual_transfer",
            TransactionTransferKind.LinkedInternal => "linked_internal_transfer",
            TransactionTransferKind.SavingsRoundup => "savings_roundup",
            TransactionTransferKind.SavingsManualDeposit => "savings_manual_deposit",
            TransactionTransferKind.SavingsManualWithdrawal => "savings_manual_withdrawal",
            _ => null
        };
    }

    private static string MapRelationshipType(TransactionRelationshipType relationshipType)
    {
        return relationshipType switch
        {
            TransactionRelationshipType.InternalAccountTransfer => "internal_account_transfer",
            TransactionRelationshipType.SavingsRoundup => "savings_roundup",
            TransactionRelationshipType.SavingsManualDeposit => "savings_manual_deposit",
            TransactionRelationshipType.SavingsManualWithdrawal => "savings_manual_withdrawal",
            TransactionRelationshipType.PossibleTransferSuggestion => "possible_transfer_suggestion",
            TransactionRelationshipType.PossibleSavingsSuggestion => "possible_savings_suggestion",
            _ => "possible_transfer_suggestion"
        };
    }

    private static string MapRelationshipStatus(TransactionRelationshipStatus relationshipStatus)
    {
        return relationshipStatus switch
        {
            TransactionRelationshipStatus.Active => "active",
            TransactionRelationshipStatus.Suggested => "suggested",
            TransactionRelationshipStatus.Dismissed => "dismissed",
            _ => "suggested"
        };
    }

    private static string? MapRelationshipDirection(TransactionRelationshipDirection relationshipDirection)
    {
        return relationshipDirection switch
        {
            TransactionRelationshipDirection.OutflowToInflow => "outflow_to_inflow",
            TransactionRelationshipDirection.OutflowToSavings => "outflow_to_savings",
            TransactionRelationshipDirection.InflowFromSavings => "inflow_from_savings",
            _ => null
        };
    }

    private static string ResolveDisplaySemantic(
        TransactionTransferKind? transferKind,
        RelationshipSummary? relationshipSummary)
    {
        if (relationshipSummary is not null)
        {
            return relationshipSummary.RelationshipType switch
            {
                TransactionRelationshipType.SavingsRoundup => "savings_roundup",
                TransactionRelationshipType.SavingsManualDeposit => "savings_manual_move",
                TransactionRelationshipType.SavingsManualWithdrawal => "savings_manual_move",
                TransactionRelationshipType.InternalAccountTransfer => "internal_transfer",
                _ => "real_transaction"
            };
        }

        return transferKind switch
        {
            TransactionTransferKind.LinkedInternal => "internal_transfer",
            TransactionTransferKind.Manual => "internal_transfer",
            TransactionTransferKind.SavingsRoundup => "savings_roundup",
            TransactionTransferKind.SavingsManualDeposit => "savings_manual_move",
            TransactionTransferKind.SavingsManualWithdrawal => "savings_manual_move",
            _ => "real_transaction"
        };
    }

    private async Task<Dictionary<Guid, RelationshipSummary>> GetRelationshipSummariesByTransactionIdAsync(
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return [];
        }

        var relationships = await dbContext.TransactionRelationships
            .AsNoTracking()
            .Where(x =>
                x.RelationshipStatus != TransactionRelationshipStatus.Dismissed
                && (transactionIds.Contains(x.SourceTransactionId)
                    || (x.TargetTransactionId.HasValue && transactionIds.Contains(x.TargetTransactionId.Value))))
            .OrderByDescending(x => x.ConfidenceScore)
            .ThenByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken);

        var summaries = new Dictionary<Guid, RelationshipSummary>();
        foreach (var relationship in relationships)
        {
            TryApplyRelationshipSummary(
                summaries,
                relationship,
                relationship.SourceTransactionId,
                relationship.TargetTransactionId);

            if (relationship.TargetTransactionId.HasValue
                && ShouldApplyRelationshipSummaryToTarget(relationship.RelationshipType))
            {
                TryApplyRelationshipSummary(
                    summaries,
                    relationship,
                    relationship.TargetTransactionId.Value,
                    relationship.SourceTransactionId);
            }
        }

        return summaries;
    }

    private static bool ShouldApplyRelationshipSummaryToTarget(TransactionRelationshipType relationshipType)
    {
        return relationshipType is
            TransactionRelationshipType.InternalAccountTransfer
            or TransactionRelationshipType.PossibleTransferSuggestion;
    }

    private static void TryApplyRelationshipSummary(
        IDictionary<Guid, RelationshipSummary> summaries,
        TransactionRelationship relationship,
        Guid transactionId,
        Guid? counterpartyTransactionId)
    {
        if (summaries.TryGetValue(transactionId, out var current) && current.ConfidenceScore >= relationship.ConfidenceScore)
        {
            return;
        }

        summaries[transactionId] = new RelationshipSummary(
            relationship.RelationshipType,
            relationship.RelationshipStatus,
            relationship.RelationshipDirection,
            relationship.ConfidenceScore,
            relationship.ConfidenceTier,
            relationship.AnalyticsTreatment,
            relationship.VirtualDestinationLabel,
            counterpartyTransactionId);
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

    private sealed record RelationshipSummary(
        TransactionRelationshipType RelationshipType,
        TransactionRelationshipStatus RelationshipStatus,
        TransactionRelationshipDirection RelationshipDirection,
        int ConfidenceScore,
        string ConfidenceTier,
        string? AnalyticsTreatment,
        string? VirtualDestinationLabel,
        Guid? CounterpartyTransactionId);
}
