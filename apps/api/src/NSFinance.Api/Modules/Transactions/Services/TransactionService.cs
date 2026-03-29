using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions.DTOs;
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

        return transactions.Select(MapToDto).ToList();
    }

    public async Task<TransactionDto?> GetTransactionByIdAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.Id == transactionId && x.FinancialAccount != null && x.FinancialAccount.UserId == currentUserProvider.UserId)
            .Select(ToReadModelProjection())
            .SingleOrDefaultAsync(cancellationToken);

        return transaction is null ? null : MapToDto(transaction);
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
                transaction.Id,
                account.Id,
                account.Name,
                transaction.Description,
                transaction.Amount,
                transaction.Currency,
                transaction.CategoryId,
                categoryName,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                transaction.BookedAtUtc,
                transaction.CreatedUtc,
                null,
                transaction.Amount < 0 ? "Expense" : "Income"),
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

        transaction.Reason = NormalizeOptionalText(request.Reason);
        transaction.Notes = NormalizeOptionalText(request.Notes);
        transaction.TaxonomyDomainId = subcategory?.DomainId ?? category.DomainId;
        transaction.TaxonomyCategoryId = category.Id;
        transaction.TaxonomySubcategoryId = subcategory?.Id;
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

    private TransactionDto MapToDto(TransactionReadModel transaction)
    {
        var taxonomyDomainName = expenseTaxonomyService.GetDomainName(transaction.TaxonomyDomainId);
        var taxonomyCategoryName = expenseTaxonomyService.GetCategoryName(transaction.TaxonomyCategoryId);
        var taxonomySubcategoryName = expenseTaxonomyService.GetSubcategoryName(transaction.TaxonomySubcategoryId);
        var categoryName = taxonomyCategoryName ?? transaction.LegacyCategoryName;

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
            transaction.Reason,
            transaction.Notes,
            transaction.BookedAtUtc,
            transaction.CreatedUtc,
            transaction.MetadataUpdatedUtc,
            transaction.Amount < 0 ? "Expense" : "Income");
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
        string? Reason,
        string? Notes,
        DateTime BookedAtUtc,
        DateTime CreatedUtc,
        DateTime? MetadataUpdatedUtc);
}
