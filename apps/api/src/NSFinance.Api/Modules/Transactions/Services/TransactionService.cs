using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.TransferPolicy;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using System.Linq.Expressions;

namespace NSFinance.Api.Modules.Transactions.Services;

public sealed class TransactionService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    ExpenseTaxonomyService expenseTaxonomyService)
{
    public async Task<IReadOnlyList<TransactionDto>> GetTransactionsAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var query = dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.FinancialAccount != null && x.FinancialAccount.UserId == currentUserProvider.UserId);

        if (accountId.HasValue)
        {
            query = query.Where(x => x.FinancialAccountId == accountId.Value);
        }

        var transactions = await query
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Select(ToReadModelProjection())
            .ToListAsync(cancellationToken);

        var relationshipsByTransactionId = await GetRelationshipSummariesByTransactionIdAsync(
            transactions.Select(x => x.Id).ToArray(),
            cancellationToken);

        return transactions
            .Select(transaction =>
                MapToDto(
                    transaction,
                    relationshipsByTransactionId.TryGetValue(transaction.Id, out var summary) ? summary : null))
            .ToList();
    }

    public async Task<TransactionDto?> GetTransactionByIdAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.Id == transactionId && x.FinancialAccount != null && x.FinancialAccount.UserId == currentUserProvider.UserId)
            .Select(ToReadModelProjection())
            .SingleOrDefaultAsync(cancellationToken);

        if (transaction is null)
        {
            return null;
        }

        var relationshipsByTransactionId = await GetRelationshipSummariesByTransactionIdAsync(
            [transaction.Id],
            cancellationToken);

        return MapToDto(
            transaction,
            relationshipsByTransactionId.TryGetValue(transaction.Id, out var summary) ? summary : null);
    }

    public async Task<(TransactionDto? Transaction, string? Error)> CreateTransactionAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.FinancialAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.AccountId && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (account is null)
        {
            return (null, "The selected account was not found for the current user.");
        }

        string? categoryName = null;
        if (request.CategoryId.HasValue)
        {
            var category = await dbContext.TransactionCategories
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.CategoryId.Value, cancellationToken);

            if (category is null)
            {
                return (null, "The selected category was not found.");
            }

            categoryName = category.Name;
        }

        var normalizedDirection = request.Direction.Trim();
        var signedAmount = normalizedDirection.Equals("Expense", StringComparison.OrdinalIgnoreCase)
            ? -Math.Abs(request.Amount)
            : Math.Abs(request.Amount);

        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? account.Currency
            : request.Currency.Trim().ToUpperInvariant();

        var bookedAtUtc = request.BookedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var utcNow = DateTime.UtcNow;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = account.Id,
            Amount = signedAmount,
            Currency = currency,
            Description = request.Description.Trim(),
            CategoryId = request.CategoryId,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = utcNow
        };

        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (
            new TransactionDto(
                Id: transaction.Id,
                AccountId: account.Id,
                AccountName: account.Name,
                Description: transaction.Description,
                Amount: transaction.Amount,
                Currency: transaction.Currency,
                CategoryId: transaction.CategoryId,
                CategoryName: categoryName,
                TaxonomyDomainId: null,
                TaxonomyDomainName: null,
                TaxonomyCategoryId: null,
                TaxonomyCategoryName: null,
                TaxonomySubcategoryId: null,
                TaxonomySubcategoryName: null,
                TransferKind: null,
                LinkedTransferTransactionId: null,
                TransferMatchConfidenceScore: null,
                TransferMatchConfidenceTier: null,
                TransferMatchReason: null,
                RelationshipType: null,
                RelationshipStatus: null,
                RelationshipDirection: null,
                RelationshipConfidenceScore: null,
                RelationshipConfidenceTier: null,
                RelationshipAnalyticsTreatment: null,
                RelationshipVirtualDestinationLabel: null,
                RelationshipCounterpartyTransactionId: null,
                DisplaySemantic: "real_transaction",
                TransferPolicyKind: null,
                ReportingBucket: transaction.Amount < 0 ? "spending" : "income",
                IsGloballyNeutralized: false,
                Reason: null,
                Notes: null,
                BookedAtUtc: transaction.BookedAtUtc,
                CreatedUtc: transaction.CreatedUtc,
                MetadataUpdatedUtc: null,
                Direction: transaction.Amount < 0 ? "Expense" : "Income"),
            null);
    }

    public async Task<(TransactionDto? Transaction, string? ErrorCode, string? ErrorMessage)> UpdateTransactionMetadataAsync(
        Guid transactionId,
        UpdateTransactionMetadataRequest request,
        CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Transactions
            .Include(x => x.FinancialAccount)
            .SingleOrDefaultAsync(
                x => x.Id == transactionId
                    && x.FinancialAccount != null
                    && x.FinancialAccount.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (transaction is null)
        {
            return (null, "transaction_not_found", "Transaction not found.");
        }

        if (!request.TaxonomyCategoryId.HasValue)
        {
            return (null, "transaction_category_required", "Category is required.");
        }

        var category = expenseTaxonomyService.GetTransactionAssignableCategory(request.TaxonomyCategoryId.Value);
        if (category is null)
        {
            return (null, "transaction_category_invalid", "Selected category is invalid.");
        }

        var subcategory = request.TaxonomySubcategoryId.HasValue
            ? expenseTaxonomyService.GetTransactionAssignableSubcategory(request.TaxonomySubcategoryId.Value)
            : null;

        if (request.TaxonomySubcategoryId.HasValue && subcategory is null)
        {
            return (null, "transaction_subcategory_invalid", "Selected subcategory is invalid.");
        }

        if (subcategory is not null && subcategory.CategoryId != category.Id)
        {
            return (null, "transaction_subcategory_mismatch", "Selected subcategory does not belong to the selected category.");
        }

        var isTransferCategory = category.DomainId == ExpenseTaxonomyService.TransferDomainId;
        var selectedDomainId = subcategory?.DomainId ?? category.DomainId;
        var selectedCategoryId = category.Id;
        var selectedSubcategoryId = subcategory?.Id;
        var preserveVerifiedLinkedTransfer =
            transaction.TransferKind == TransactionTransferKind.LinkedInternal
            && transaction.LinkedTransferTransactionId.HasValue
            && isTransferCategory;

        if (!preserveVerifiedLinkedTransfer)
        {
            await UnlinkCounterpartAsync(transaction, cancellationToken);
        }

        transaction.Reason = NormalizeOptionalText(request.Reason);
        transaction.Notes = NormalizeOptionalText(request.Notes);
        transaction.TaxonomyDomainId = selectedDomainId;
        transaction.TaxonomyCategoryId = selectedCategoryId;
        transaction.TaxonomySubcategoryId = selectedSubcategoryId;

        var keptVerifiedLink = preserveVerifiedLinkedTransfer
            && await SyncLinkedCounterpartTransferTaxonomyAsync(
                transaction,
                selectedDomainId,
                selectedCategoryId,
                selectedSubcategoryId,
                cancellationToken);

        if (!keptVerifiedLink)
        {
            transaction.TransferKind = isTransferCategory ? TransactionTransferKind.Manual : null;
            transaction.LinkedTransferTransactionId = null;
            transaction.LinkedTransferMatchedUtc = null;
            transaction.TransferMatchConfidenceScore = null;
            transaction.TransferMatchConfidenceTier = null;
            transaction.TransferMatchReason = null;
        }
        transaction.MetadataUpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await GetTransactionByIdAsync(transaction.Id, cancellationToken);
        return (updated, null, null);
    }

    public async Task<bool> AccountExistsForCurrentUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await dbContext.FinancialAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == accountId && x.UserId == currentUserProvider.UserId, cancellationToken);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task UnlinkCounterpartAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        if (!transaction.LinkedTransferTransactionId.HasValue)
        {
            return;
        }

        var counterpart = await dbContext.Transactions
            .Include(x => x.FinancialAccount)
            .SingleOrDefaultAsync(
                x => x.Id == transaction.LinkedTransferTransactionId.Value
                    && x.FinancialAccount != null
                    && x.FinancialAccount.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (counterpart is null)
        {
            return;
        }

        counterpart.LinkedTransferTransactionId = null;
        counterpart.LinkedTransferMatchedUtc = null;
        counterpart.TransferMatchConfidenceScore = null;
        counterpart.TransferMatchConfidenceTier = null;
        counterpart.TransferMatchReason = null;

        if (counterpart.TransferKind == TransactionTransferKind.LinkedInternal)
        {
            counterpart.TransferKind = null;

            if (!counterpart.MetadataUpdatedUtc.HasValue
                && counterpart.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId
                && counterpart.TaxonomyCategoryId == ExpenseTaxonomyService.TransferDefaultCategoryId
                && counterpart.TaxonomySubcategoryId == ExpenseTaxonomyService.TransferDefaultSubcategoryId)
            {
                counterpart.TaxonomyDomainId = null;
                counterpart.TaxonomyCategoryId = null;
                counterpart.TaxonomySubcategoryId = null;
            }
        }
    }

    private async Task<bool> SyncLinkedCounterpartTransferTaxonomyAsync(
        Transaction transaction,
        int selectedDomainId,
        int selectedCategoryId,
        int? selectedSubcategoryId,
        CancellationToken cancellationToken)
    {
        if (!transaction.LinkedTransferTransactionId.HasValue)
        {
            return false;
        }

        var counterpart = await dbContext.Transactions
            .Include(x => x.FinancialAccount)
            .SingleOrDefaultAsync(
                x => x.Id == transaction.LinkedTransferTransactionId.Value
                    && x.FinancialAccount != null
                    && x.FinancialAccount.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (counterpart is null || counterpart.LinkedTransferTransactionId != transaction.Id)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        transaction.TransferKind = TransactionTransferKind.LinkedInternal;
        transaction.LinkedTransferMatchedUtc ??= now;
        transaction.TransferMatchConfidenceScore ??= 0;
        transaction.TransferMatchConfidenceTier ??= "manual_verified";
        transaction.TransferMatchReason ??= "manual_linked_pair";

        counterpart.TransferKind = TransactionTransferKind.LinkedInternal;
        counterpart.LinkedTransferTransactionId = transaction.Id;
        counterpart.LinkedTransferMatchedUtc = now;
        counterpart.TransferMatchConfidenceScore ??= 0;
        counterpart.TransferMatchConfidenceTier ??= "manual_verified";
        counterpart.TransferMatchReason ??= "manual_linked_pair";

        var counterpartTaxonomyChanged =
            counterpart.TaxonomyDomainId != selectedDomainId
            || counterpart.TaxonomyCategoryId != selectedCategoryId
            || counterpart.TaxonomySubcategoryId != selectedSubcategoryId;

        if (counterpartTaxonomyChanged)
        {
            counterpart.TaxonomyDomainId = selectedDomainId;
            counterpart.TaxonomyCategoryId = selectedCategoryId;
            counterpart.TaxonomySubcategoryId = selectedSubcategoryId;
            counterpart.MetadataUpdatedUtc = now;
        }

        return true;
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

    private TransactionDto MapToDto(TransactionReadModel transaction, RelationshipSummary? relationshipSummary)
    {
        var taxonomyDomainName = expenseTaxonomyService.GetDomainName(transaction.TaxonomyDomainId);
        var taxonomyCategoryName = expenseTaxonomyService.GetCategoryName(transaction.TaxonomyCategoryId);
        var taxonomySubcategoryName = expenseTaxonomyService.GetSubcategoryName(transaction.TaxonomySubcategoryId);
        var categoryName = taxonomyCategoryName ?? transaction.LegacyCategoryName;
        var transferPolicy = TransferPolicyEngine.Evaluate(
            transaction.TaxonomyDomainId,
            transaction.TaxonomyCategoryId,
            transaction.TaxonomySubcategoryId,
            transaction.TransferKind,
            transaction.LinkedTransferTransactionId,
            transaction.Amount);

        return new TransactionDto(
            transaction.Id,
            transaction.FinancialAccountId,
            transaction.AccountName,
            transaction.Description,
            transaction.Amount,
            transaction.Currency,
            transaction.LegacyCategoryId,
            categoryName,
            transaction.TaxonomyDomainId,
            taxonomyDomainName,
            transaction.TaxonomyCategoryId,
            taxonomyCategoryName,
            transaction.TaxonomySubcategoryId,
            taxonomySubcategoryName,
            MapTransferKind(transaction.TransferKind),
            transaction.LinkedTransferTransactionId,
            transaction.TransferMatchConfidenceScore,
            transaction.TransferMatchConfidenceTier,
            transaction.TransferMatchReason,
            relationshipSummary is null ? null : MapRelationshipType(relationshipSummary.RelationshipType),
            relationshipSummary is null ? null : MapRelationshipStatus(relationshipSummary.RelationshipStatus),
            relationshipSummary is null ? null : MapRelationshipDirection(relationshipSummary.RelationshipDirection),
            relationshipSummary?.ConfidenceScore,
            relationshipSummary?.ConfidenceTier,
            relationshipSummary?.AnalyticsTreatment,
            relationshipSummary?.VirtualDestinationLabel,
            relationshipSummary?.CounterpartyTransactionId,
            ResolveDisplaySemantic(transaction, relationshipSummary),
            MapTransferPolicyKind(transferPolicy.PolicyKind),
            transferPolicy.ReportingBucket.ToString().ToLowerInvariant(),
            transferPolicy.IsGloballyNeutralized,
            transaction.Reason,
            transaction.Notes,
            transaction.BookedAtUtc,
            transaction.CreatedUtc,
            transaction.MetadataUpdatedUtc,
            transaction.Amount < 0 ? "Expense" : "Income");
    }

    private static string ResolveDisplaySemantic(TransactionReadModel transaction, RelationshipSummary? relationshipSummary)
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

        return transaction.TransferKind switch
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

    private static Expression<Func<Transaction, TransactionReadModel>> ToReadModelProjection()
    {
        return x => new TransactionReadModel(
            x.Id,
            x.FinancialAccountId,
            x.FinancialAccount != null ? x.FinancialAccount.Name : string.Empty,
            x.Description,
            x.Amount,
            x.Currency,
            x.CategoryId,
            x.Category != null ? x.Category.Name : null,
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
            x.MetadataUpdatedUtc);
    }

    private sealed record TransactionReadModel(
        Guid Id,
        Guid FinancialAccountId,
        string AccountName,
        string Description,
        decimal Amount,
        string Currency,
        Guid? LegacyCategoryId,
        string? LegacyCategoryName,
        int? TaxonomyDomainId,
        int? TaxonomyCategoryId,
        int? TaxonomySubcategoryId,
        TransactionTransferKind? TransferKind,
        Guid? LinkedTransferTransactionId,
        int? TransferMatchConfidenceScore,
        string? TransferMatchConfidenceTier,
        string? TransferMatchReason,
        string? Reason,
        string? Notes,
        DateTime BookedAtUtc,
        DateTime CreatedUtc,
        DateTime? MetadataUpdatedUtc);

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
