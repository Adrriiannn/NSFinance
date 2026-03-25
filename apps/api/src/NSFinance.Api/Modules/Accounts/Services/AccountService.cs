using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Accounts.Services;

public sealed class AccountService(AppDbContext dbContext, ICurrentUserProvider currentUserProvider)
{
    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.UserId == currentUserProvider.UserId)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                CurrentBalance = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id)
                    .Select(linked => dbContext.BankBalanceSnapshots
                        .Where(balance => balance.LinkedBankAccountId == linked.Id)
                        .OrderByDescending(balance => balance.CapturedUtc)
                        .Select(balance => (decimal?)(balance.Current ?? balance.Available))
                        .FirstOrDefault())
                    .FirstOrDefault() ?? (x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m),
                TransactionCount = x.Transactions.Count,
                x.CreatedUtc,
                Provider = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id && linked.Connection != null)
                    .OrderByDescending(linked => linked.UpdatedUtc)
                    .Select(linked => new
                    {
                        linked.Connection!.ProviderId,
                        linked.Connection.ProviderDisplayName,
                        ProviderIconUrl = linked.Connection.ProviderIconUri,
                        ProviderLogoUrl = linked.Connection.ProviderLogoUri,
                        linked.Connection.ProviderBrandBgColor
                    })
                    .FirstOrDefault()
            })
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.CurrentBalance,
                x.TransactionCount,
                x.CreatedUtc,
                x.Provider != null ? x.Provider.ProviderId : null,
                x.Provider != null ? x.Provider.ProviderDisplayName : null,
                x.Provider != null ? x.Provider.ProviderIconUrl : null,
                x.Provider != null ? x.Provider.ProviderLogoUrl : null,
                x.Provider != null ? x.Provider.ProviderBrandBgColor : null,
                x.Provider != null
                    && (x.Provider.ProviderIconUrl != null
                        || x.Provider.ProviderLogoUrl != null
                        || x.Provider.ProviderDisplayName != null)))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountDto?> GetAccountByIdAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.Id == accountId && x.UserId == currentUserProvider.UserId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                CurrentBalance = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id)
                    .Select(linked => dbContext.BankBalanceSnapshots
                        .Where(balance => balance.LinkedBankAccountId == linked.Id)
                        .OrderByDescending(balance => balance.CapturedUtc)
                        .Select(balance => (decimal?)(balance.Current ?? balance.Available))
                        .FirstOrDefault())
                    .FirstOrDefault() ?? (x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m),
                TransactionCount = x.Transactions.Count,
                x.CreatedUtc,
                Provider = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id && linked.Connection != null)
                    .OrderByDescending(linked => linked.UpdatedUtc)
                    .Select(linked => new
                    {
                        linked.Connection!.ProviderId,
                        linked.Connection.ProviderDisplayName,
                        ProviderIconUrl = linked.Connection.ProviderIconUri,
                        ProviderLogoUrl = linked.Connection.ProviderLogoUri,
                        linked.Connection.ProviderBrandBgColor
                    })
                    .FirstOrDefault()
            })
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.CurrentBalance,
                x.TransactionCount,
                x.CreatedUtc,
                x.Provider != null ? x.Provider.ProviderId : null,
                x.Provider != null ? x.Provider.ProviderDisplayName : null,
                x.Provider != null ? x.Provider.ProviderIconUrl : null,
                x.Provider != null ? x.Provider.ProviderLogoUrl : null,
                x.Provider != null ? x.Provider.ProviderBrandBgColor : null,
                x.Provider != null
                    && (x.Provider.ProviderIconUrl != null
                        || x.Provider.ProviderLogoUrl != null
                        || x.Provider.ProviderDisplayName != null)))
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
            account.CreatedUtc,
            null,
            null,
            null,
            null,
            null,
            false);
    }

    public async Task<AccountDto?> UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.FinancialAccounts
            .SingleOrDefaultAsync(
                x => x.Id == accountId && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        account.Name = request.Name.Trim();
        account.Type = request.Type.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);

        var currentBalance = await dbContext.Transactions
            .Where(x => x.FinancialAccountId == account.Id)
            .Select(x => (decimal?)x.Amount)
            .SumAsync(cancellationToken) ?? 0m;

        var transactionCount = await dbContext.Transactions
            .CountAsync(x => x.FinancialAccountId == account.Id, cancellationToken);

        return new AccountDto(
            account.Id,
            account.Name,
            account.Type,
            account.Currency,
            currentBalance,
            transactionCount,
            account.CreatedUtc,
            null,
            null,
            null,
            null,
            null,
            false);
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await dbContext.FinancialAccounts
            .SingleOrDefaultAsync(
                x => x.Id == accountId && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (account is null)
        {
            return false;
        }

        dbContext.FinancialAccounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
