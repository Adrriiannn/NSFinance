using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using System.Linq.Expressions;

namespace NSFinance.Api.Modules.Transactions.Services;

public sealed class TransactionService(AppDbContext dbContext, ICurrentUserProvider currentUserProvider)
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

        return await query
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Select(ToDtoProjection())
            .ToListAsync(cancellationToken);
    }

    public async Task<TransactionDto?> GetTransactionByIdAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        return await dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.Id == transactionId && x.FinancialAccount != null && x.FinancialAccount.UserId == currentUserProvider.UserId)
            .Select(ToDtoProjection())
            .SingleOrDefaultAsync(cancellationToken);
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
                transaction.BookedAtUtc,
                transaction.CreatedUtc,
                transaction.Amount < 0 ? "Expense" : "Income"),
            null);
    }

    public async Task<bool> AccountExistsForCurrentUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await dbContext.FinancialAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == accountId && x.UserId == currentUserProvider.UserId, cancellationToken);
    }

    private static Expression<Func<Transaction, TransactionDto>> ToDtoProjection()
    {
        return x => new TransactionDto(
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
            x.Amount < 0 ? "Expense" : "Income");
    }
}
