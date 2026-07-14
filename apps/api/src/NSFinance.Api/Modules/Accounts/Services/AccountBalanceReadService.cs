using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Accounts.Services;

public sealed class AccountBalanceReadService
{
    private static readonly TimeSpan ProviderFreshnessWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan FutureTimestampTolerance = TimeSpan.FromMinutes(5);

    private readonly AppDbContext dbContext;
    private readonly ICurrentUserProvider currentUserProvider;
    private readonly TimeProvider timeProvider;

    public AccountBalanceReadService(
        AppDbContext dbContext,
        ICurrentUserProvider currentUserProvider,
        TimeProvider timeProvider)
    {
        this.dbContext = dbContext;
        this.currentUserProvider = currentUserProvider;
        this.timeProvider = timeProvider;
    }

    public async Task<IReadOnlyDictionary<Guid, AccountBalanceDto>> GetBalancesAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, AccountBalanceDto>();
        }

        var rows = await BuildBalanceQuery(accountIds.Distinct().ToArray())
            .ToListAsync(cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        return rows.ToDictionary(row => row.AccountId, row => MapBalance(row, utcNow));
    }

    public async Task<IReadOnlyList<AccountDto>> AttachBalancesAsync(
        IReadOnlyList<AccountDto> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 0)
        {
            return accounts;
        }

        var balances = await GetBalancesAsync(accounts.Select(x => x.Id).ToArray(), cancellationToken);
        return accounts
            .Select(account => account with
            {
                Balance = balances.GetValueOrDefault(account.Id)
            })
            .ToList();
    }

    public static PortfolioBalanceDto BuildPortfolioBalance(IEnumerable<AccountBalanceDto> balances)
    {
        var materialized = balances.ToList();
        var included = materialized
            .Where(x => x.Source != "unavailable" && x.Current.HasValue)
            .ToList();
        var byCurrency = included
            .GroupBy(x => x.Currency.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new CurrencyBalanceTotalDto(
                group.Key,
                group.Sum(x => x.Current!.Value),
                group.Count(),
                "current"))
            .ToList();

        return new PortfolioBalanceDto(
            byCurrency,
            included.Count,
            materialized.Count - included.Count,
            byCurrency.Count > 1);
    }

    internal IQueryable<AccountBalanceSourceRow> BuildBalanceQuery(Guid[] accountIds)
    {
        return dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(account =>
                account.UserId == currentUserProvider.UserId
                && accountIds.Contains(account.Id))
            .Select(account => new AccountBalanceSourceRow
            {
                AccountId = account.Id,
                AccountCurrency = account.Currency,
                IsLinked = dbContext.LinkedBankAccounts.Any(linked => linked.FinancialAccountId == account.Id),
                Snapshot = dbContext.BankBalanceSnapshots
                    .Where(snapshot =>
                        snapshot.LinkedBankAccount != null
                        && snapshot.LinkedBankAccount.FinancialAccountId == account.Id)
                    .OrderByDescending(snapshot => snapshot.CapturedUtc)
                    .ThenByDescending(snapshot => snapshot.Id)
                    .Select(snapshot => new AccountBalanceSnapshotSourceRow
                    {
                        Id = snapshot.Id,
                        Current = snapshot.Current,
                        Available = snapshot.Available,
                        Overdraft = snapshot.Overdraft,
                        Currency = snapshot.Currency,
                        CapturedUtc = snapshot.CapturedUtc
                    })
                    .FirstOrDefault(),
                ManualLedgerBalance = account.Transactions
                    .Where(transaction => transaction.Currency == account.Currency)
                    .Select(transaction => (decimal?)transaction.Amount)
                    .Sum() ?? 0m,
                HasOtherCurrencyTransactions = account.Transactions.Any(
                    transaction => transaction.Currency != account.Currency),
                ManualAsOfUtc = account.Transactions
                    .Where(transaction => transaction.Currency == account.Currency)
                    .OrderByDescending(transaction => transaction.CreatedUtc)
                    .ThenByDescending(transaction => transaction.Id)
                    .Select(transaction => (DateTime?)transaction.CreatedUtc)
                    .FirstOrDefault() ?? account.CreatedUtc
            });
    }

    private static AccountBalanceDto MapBalance(AccountBalanceSourceRow row, DateTime utcNow)
    {
        var accountCurrency = NormalizeCurrency(row.AccountCurrency);
        if (row.Snapshot is not null)
        {
            var snapshotCurrency = NormalizeCurrency(row.Snapshot.Currency);
            var exclusions = new List<string>();
            if (!string.Equals(accountCurrency, snapshotCurrency, StringComparison.Ordinal))
            {
                exclusions.Add("account_currency_mismatch");
            }

            if (!row.Snapshot.Current.HasValue)
            {
                exclusions.Add("current_balance_unavailable");
            }

            if (!row.Snapshot.Available.HasValue)
            {
                exclusions.Add("available_balance_unavailable");
            }

            var freshness = ResolveProviderFreshness(row.Snapshot.CapturedUtc, utcNow, exclusions);
            return new AccountBalanceDto(
                row.Snapshot.Current,
                row.Snapshot.Available,
                row.Snapshot.Overdraft,
                snapshotCurrency,
                "provider_snapshot",
                EnsureUtc(row.Snapshot.CapturedUtc),
                freshness,
                exclusions);
        }

        if (row.IsLinked)
        {
            return new AccountBalanceDto(
                null,
                null,
                null,
                accountCurrency,
                "unavailable",
                null,
                "unknown",
                ["provider_snapshot_missing"]);
        }

        IReadOnlyList<string> manualExclusions = row.HasOtherCurrencyTransactions
            ? ["non_account_currency_transactions_excluded"]
            : [];
        return new AccountBalanceDto(
            row.ManualLedgerBalance,
            null,
            null,
            accountCurrency,
            "manual_ledger",
            EnsureUtc(row.ManualAsOfUtc),
            "current",
            manualExclusions);
    }

    private static string ResolveProviderFreshness(
        DateTime capturedUtc,
        DateTime utcNow,
        ICollection<string> exclusions)
    {
        var normalizedCapturedUtc = EnsureUtc(capturedUtc);
        if (normalizedCapturedUtc > utcNow.Add(FutureTimestampTolerance))
        {
            exclusions.Add("future_capture_timestamp");
            return "unknown";
        }

        return utcNow - normalizedCapturedUtc <= ProviderFreshnessWindow
            ? "fresh"
            : "stale";
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string NormalizeCurrency(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}

internal sealed class AccountBalanceSourceRow
{
    public Guid AccountId { get; init; }
    public string AccountCurrency { get; init; } = "EUR";
    public bool IsLinked { get; init; }
    public AccountBalanceSnapshotSourceRow? Snapshot { get; init; }
    public decimal ManualLedgerBalance { get; init; }
    public bool HasOtherCurrencyTransactions { get; init; }
    public DateTime ManualAsOfUtc { get; init; }
}

internal sealed class AccountBalanceSnapshotSourceRow
{
    public Guid Id { get; init; }
    public decimal? Current { get; init; }
    public decimal? Available { get; init; }
    public decimal? Overdraft { get; init; }
    public string Currency { get; init; } = "EUR";
    public DateTime CapturedUtc { get; init; }
}
