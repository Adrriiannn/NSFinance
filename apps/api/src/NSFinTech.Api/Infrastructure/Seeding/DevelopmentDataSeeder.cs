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
    private const string ProviderTypeLocalPassword = "local_password";
    public const string DemoUserPassword = "Password123!";

    public async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var demoUserId = FixedCurrentUserProvider.DemoUserId;
        var utcNow = DateTime.UtcNow;

        await EnsureDemoUserAsync(dbContext, demoUserId, utcNow, cancellationToken);
        await EnsureCategoriesAsync(dbContext, utcNow, cancellationToken);
        await EnsureAccountsAndTransactionsAsync(dbContext, demoUserId, utcNow, cancellationToken);
    }

    public async Task SeedPolicyDataAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var policies = new (string PolicyType, string Name, string Version, string ContentRef)[]
        {
            ("terms_of_service", "Terms of Service", "1.0.0", "legal/terms/v1"),
            ("privacy_policy", "Privacy Policy", "1.0.0", "legal/privacy/v1"),
            ("ai_limitations_notice", "AI Limitations Notice", "1.0.0", "legal/ai-limitations/v1"),
            ("open_banking_consent_placeholder", "Open Banking Consent (Placeholder)", "0.1.0", "legal/open-banking-consent/placeholder"),
            ("marketing_communications", "Marketing Communications Consent", "1.0.0", "legal/marketing-consent/v1")
        };

        foreach (var (policyType, name, version, contentRef) in policies)
        {
            var document = await dbContext.PolicyDocuments
                .SingleOrDefaultAsync(x => x.PolicyType == policyType, cancellationToken);

            if (document is null)
            {
                document = new PolicyDocument
                {
                    Id = Guid.NewGuid(),
                    PolicyType = policyType,
                    Name = name,
                    CreatedUtc = now
                };
                dbContext.PolicyDocuments.Add(document);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var existingVersion = await dbContext.PolicyVersions
                .SingleOrDefaultAsync(
                    x => x.PolicyDocumentId == document.Id && x.Version == version,
                    cancellationToken);

            if (existingVersion is not null)
            {
                continue;
            }

            dbContext.PolicyVersions.Add(new PolicyVersion
            {
                Id = Guid.NewGuid(),
                PolicyDocumentId = document.Id,
                Version = version,
                EffectiveUtc = now,
                ContentReference = contentRef,
                IsActive = true,
                CreatedUtc = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDemoUserAsync(
        AppDbContext dbContext,
        Guid demoUserId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == demoUserId, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = demoUserId,
                PrimaryEmail = "demo@nsfintech.local",
                NormalizedEmail = "demo@nsfintech.local",
                DisplayName = "Aoife Murphy",
                Status = "active",
                OnboardingStatus = "completed",
                Role = "user",
                CreatedUtc = utcNow,
                UpdatedUtc = utcNow,
                LastLoginUtc = utcNow,
                EmailVerified = true,
                Timezone = "Europe/Dublin",
                Locale = "en-IE",
                PreferredCurrency = "EUR",
                PlanTier = "standard",
                BiometricUnlockEnabled = false
            };

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var passwordCredential = await dbContext.PasswordCredentials
            .SingleOrDefaultAsync(x => x.UserId == demoUserId, cancellationToken);

        if (passwordCredential is null)
        {
            dbContext.PasswordCredentials.Add(new PasswordCredential
            {
                Id = Guid.NewGuid(),
                UserId = demoUserId,
                PasswordHash = passwordHasher.HashPassword(DemoUserPassword),
                HashAlgorithm = "pbkdf2-sha256",
                CreatedUtc = utcNow,
                UpdatedUtc = utcNow,
                RequiresRehash = false
            });
        }

        var localProvider = await dbContext.UserAuthProviders
            .SingleOrDefaultAsync(x => x.UserId == demoUserId && x.ProviderType == ProviderTypeLocalPassword, cancellationToken);

        if (localProvider is null)
        {
            dbContext.UserAuthProviders.Add(new UserAuthProvider
            {
                Id = Guid.NewGuid(),
                UserId = demoUserId,
                ProviderType = ProviderTypeLocalPassword,
                LinkedAtUtc = utcNow,
                LastUsedAtUtc = utcNow,
                IsActive = true
            });
        }

        var preference = await dbContext.UserPreferences
            .SingleOrDefaultAsync(x => x.UserId == demoUserId, cancellationToken);

        if (preference is null)
        {
            dbContext.UserPreferences.Add(new UserPreference
            {
                UserId = demoUserId,
                UpdatedUtc = utcNow
            });
        }

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
