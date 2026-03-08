using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Modules.Accounts.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Accounts.Services;

public sealed class AccountService(AppDbContext dbContext, ICurrentUserProvider currentUserProvider)
{
    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.FinancialAccounts
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
    }

    public async Task<AccountDto?> GetAccountByIdAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.Id == accountId && x.UserId == currentUserProvider.UserId)
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m,
                x.Transactions.Count,
                x.CreatedUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        var account = new FinancialAccount
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            Name = request.Name.Trim(),
            Type = request.Type.Trim(),
            Currency = request.Currency.Trim().ToUpperInvariant(),
            CreatedUtc = utcNow
        };

        dbContext.FinancialAccounts.Add(account);

        var openingBalance = request.OpeningBalance.GetValueOrDefault();
        if (openingBalance != 0)
        {
            dbContext.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                FinancialAccountId = account.Id,
                Amount = openingBalance,
                Currency = account.Currency,
                Description = "Opening balance",
                BookedAtUtc = utcNow,
                CreatedUtc = utcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AccountDto(
            account.Id,
            account.Name,
            account.Type,
            account.Currency,
            openingBalance,
            openingBalance == 0 ? 0 : 1,
            account.CreatedUtc);
    }
}
