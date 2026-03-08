using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Infrastructure.Seeding;

public sealed class DevelopmentDataSeeder(
    ILogger<DevelopmentDataSeeder> logger,
    IPasswordHasher passwordHasher)
{
    public const string DemoUserPassword = "Password123!";

    public async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var demoUserId = FixedCurrentUserProvider.DemoUserId;
        var utcNow = DateTime.UtcNow;

        await EnsureDemoUserAsync(dbContext, demoUserId, utcNow, cancellationToken);
        await EnsureCategoriesAsync(dbContext, utcNow, cancellationToken);
        await EnsureAccountsAndTransactionsAsync(dbContext, demoUserId, utcNow, cancellationToken);
    }

    private async Task EnsureDemoUserAsync(
        AppDbContext dbContext,
        Guid demoUserId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == demoUserId, cancellationToken);
        if (user is not null)
        {
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                user.PasswordHash = passwordHasher.HashPassword(DemoUserPassword);
                user.LastLoginUtc = utcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        dbContext.Users.Add(new User
        {
            Id = demoUserId,
            Email = "demo@nsfintech.local",
            PasswordHash = passwordHasher.HashPassword(DemoUserPassword),
            FirstName = "Aoife",
            LastName = "Murphy",
            CreatedUtc = utcNow,
            LastLoginUtc = utcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureCategoriesAsync(
        AppDbContext dbContext,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var categoriesToSeed = new (string Name, string Type)[]
        {
            ("Grocery", "Expense"),
            ("Rent", "Expense"),
            ("Utilities", "Expense"),
            ("Coffee", "Expense"),
            ("Transfer to Savings", "Expense"),
            ("Salary", "Income")
        };

        var existingNames = await dbContext.TransactionCategories
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        foreach (var (name, type) in categoriesToSeed)
        {
            if (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            dbContext.TransactionCategories.Add(new TransactionCategory
            {
                Id = Guid.NewGuid(),
                Name = name,
                Type = type,
                CreatedUtc = utcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAccountsAndTransactionsAsync(
        AppDbContext dbContext,
        Guid demoUserId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var accounts = await dbContext.FinancialAccounts
            .Where(x => x.UserId == demoUserId)
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            accounts =
            [
                new FinancialAccount
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222221"),
                    UserId = demoUserId,
                    Name = "Main Current",
                    Type = "Current",
                    Currency = "EUR",
                    CreatedUtc = utcNow.AddDays(-60)
                },
                new FinancialAccount
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    UserId = demoUserId,
                    Name = "Savings Vault",
                    Type = "Savings",
                    Currency = "EUR",
                    CreatedUtc = utcNow.AddDays(-45)
                },
                new FinancialAccount
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222223"),
                    UserId = demoUserId,
                    Name = "Cash Wallet",
                    Type = "Cash",
                    Currency = "EUR",
                    CreatedUtc = utcNow.AddDays(-30)
                }
            ];

            dbContext.FinancialAccounts.AddRange(accounts);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var accountIds = accounts.Select(x => x.Id).ToArray();
        var hasTransactions = await dbContext.Transactions
            .AnyAsync(x => accountIds.Contains(x.FinancialAccountId), cancellationToken);

        if (hasTransactions)
        {
            return;
        }

        var categories = await dbContext.TransactionCategories
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        Guid Category(string name) => categories[name];
        Guid Account(string name) => accounts.Single(x => x.Name == name).Id;

        var transactions = new List<Transaction>
        {
            NewTx(Account("Main Current"), 2400m, "Salary - NS Retail", Category("Salary"), utcNow.AddDays(-27)),
            NewTx(Account("Main Current"), -1250m, "Rent - Dublin Apartment", Category("Rent"), utcNow.AddDays(-25)),
            NewTx(Account("Main Current"), -86.40m, "Grocery - SuperValu", Category("Grocery"), utcNow.AddDays(-23)),
            NewTx(Account("Main Current"), -54.20m, "Utilities - Electricity", Category("Utilities"), utcNow.AddDays(-20)),
            NewTx(Account("Main Current"), -4.50m, "Coffee - Morning Flat White", Category("Coffee"), utcNow.AddDays(-18)),
            NewTx(Account("Main Current"), -300m, "Transfer to Savings", Category("Transfer to Savings"), utcNow.AddDays(-16)),
            NewTx(Account("Savings Vault"), 300m, "Transfer from Main Current", Category("Salary"), utcNow.AddDays(-16)),
            NewTx(Account("Main Current"), -42.15m, "Grocery - Tesco", Category("Grocery"), utcNow.AddDays(-12)),
            NewTx(Account("Cash Wallet"), 120m, "Cash Withdrawal", null, utcNow.AddDays(-11)),
            NewTx(Account("Cash Wallet"), -22m, "Lunch with friends", null, utcNow.AddDays(-8)),
            NewTx(Account("Main Current"), -6.10m, "Coffee - Late afternoon", Category("Coffee"), utcNow.AddDays(-5)),
            NewTx(Account("Main Current"), -61.30m, "Utilities - Internet", Category("Utilities"), utcNow.AddDays(-2))
        };

        dbContext.Transactions.AddRange(transactions);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded demo financial accounts and transactions for local development.");
    }

    private static Transaction NewTx(
        Guid accountId,
        decimal amount,
        string description,
        Guid? categoryId,
        DateTime bookedAtUtc)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = "EUR",
            Description = description,
            CategoryId = categoryId,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        };
    }
}
