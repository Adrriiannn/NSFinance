using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class FinancialCommitmentReadServiceTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_UnifiesProviderRecordsWithExplicitCertaintyAndStableOrder()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        dbContext.BankDirectDebits.Add(new BankDirectDebit
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            LinkedBankAccountId = seeded.LinkedBankAccountId,
            ProviderDirectDebitId = "provider-dd",
            Status = "ACTIVE",
            MandateType = "Variable",
            MerchantName = "Electric Ireland",
            PreviousPaymentDateUtc = UtcNow.UtcDateTime.AddMonths(-1),
            PreviousPaymentAmount = 72m,
            PreviousPaymentCurrency = "eur",
            NextPaymentDateUtc = UtcNow.UtcDateTime.AddDays(2),
            NextPaymentAmount = 78m,
            NextPaymentCurrency = "eur",
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-2),
            UpdatedUtc = UtcNow.UtcDateTime.AddHours(-1)
        });
        dbContext.BankStandingOrders.Add(new BankStandingOrder
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            LinkedBankAccountId = seeded.LinkedBankAccountId,
            ProviderStandingOrderId = "provider-so",
            Status = "enabled",
            Frequency = "Monthly",
            PayeeName = "Rent",
            FirstPaymentDateUtc = UtcNow.UtcDateTime.AddMonths(-6),
            NextPaymentDateUtc = UtcNow.UtcDateTime.AddDays(1),
            FinalPaymentDateUtc = UtcNow.UtcDateTime.AddYears(1),
            NextPaymentAmount = 900m,
            NextPaymentCurrency = "EUR",
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-6),
            UpdatedUtc = UtcNow.UtcDateTime.AddHours(-2)
        });
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, seeded.UserId).ListAsync(10, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.IsTruncated);
        Assert.Equal(UtcNow.UtcDateTime, result.Value.AsOfUtc);
        Assert.Collection(
            result.Value.Items,
            standingOrder =>
            {
                Assert.Equal("standing_order", standingOrder.Kind);
                Assert.Equal("active", standingOrder.Lifecycle);
                Assert.Equal("provider", standingOrder.Source);
                Assert.Equal("confirmed", standingOrder.Confidence);
                Assert.Equal("Rent", standingOrder.Label);
                Assert.Equal("monthly", standingOrder.Cadence);
                Assert.Equal("provider_reported", standingOrder.DateCertainty);
                Assert.Equal("provider_reported", standingOrder.AmountCertainty);
                Assert.False(standingOrder.IsVariableAmount);
                Assert.Equal("fresh", standingOrder.Freshness);
                Assert.Equal("EUR", standingOrder.Currency);
                Assert.Single(standingOrder.Evidence);
            },
            directDebit =>
            {
                Assert.Equal("direct_debit", directDebit.Kind);
                Assert.Equal("Electric Ireland", directDebit.Label);
                Assert.Equal("variable", directDebit.AmountCertainty);
                Assert.True(directDebit.IsVariableAmount);
                Assert.Equal(72m, directDebit.LastObservedAmount);
                Assert.Equal("EUR", directDebit.LastObservedCurrency);
                Assert.Equal(seeded.FinancialAccountId, directDebit.AccountId);
                Assert.Empty(directDebit.Exclusions);
            });
    }

    [Fact]
    public async Task ListAsync_ScopesEverySourceToCurrentUser()
    {
        await using var dbContext = CreateDbContext();
        var owner = await SeedLinkedAccountAsync(dbContext, "EUR");
        var other = await SeedLinkedAccountAsync(dbContext, "USD");
        dbContext.BankDirectDebits.AddRange(
            CreateDirectDebit(owner.LinkedBankAccountId, "Owner bill", UtcNow.UtcDateTime.AddDays(1)),
            CreateDirectDebit(other.LinkedBankAccountId, "Other bill", UtcNow.UtcDateTime.AddDays(1)));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext, owner.UserId).ListAsync(null, CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Owner bill", item.Label);
        Assert.Equal(owner.FinancialAccountId, item.AccountId);
    }

    [Fact]
    public async Task ListAsync_PreservesUnknownValuesAndMarksStaleSource()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        dbContext.BankDirectDebits.Add(new BankDirectDebit
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = seeded.LinkedBankAccountId,
            ProviderDirectDebitId = "unknown-dd",
            Status = "new-provider-state",
            RawPayloadJson = "{}",
            CreatedUtc = UtcNow.UtcDateTime.AddDays(-3),
            UpdatedUtc = UtcNow.UtcDateTime.AddHours(-25)
        });
        await dbContext.SaveChangesAsync();

        var item = Assert.Single((await CreateService(dbContext, seeded.UserId)
            .ListAsync(10, CancellationToken.None)).Value!.Items);

        Assert.Equal("unknown", item.Lifecycle);
        Assert.Equal("unknown", item.DateCertainty);
        Assert.Equal("unknown", item.AmountCertainty);
        Assert.Null(item.NextDateUtc);
        Assert.Null(item.NextAmount);
        Assert.Null(item.Currency);
        Assert.Null(item.IsVariableAmount);
        Assert.Equal("stale", item.Freshness);
        Assert.Contains("provider_status_unrecognized", item.Exclusions);
        Assert.Contains("next_date_unavailable", item.Exclusions);
        Assert.Contains("next_amount_unavailable", item.Exclusions);
        Assert.Contains("stale_provider_source", item.Exclusions);
    }

    [Fact]
    public async Task ListAsync_FinalDateElapsed_UsesExpiredLifecycle()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        dbContext.BankStandingOrders.Add(new BankStandingOrder
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = seeded.LinkedBankAccountId,
            ProviderStandingOrderId = "expired-so",
            Status = "active",
            Frequency = "weekly",
            PayeeName = "Old instruction",
            FinalPaymentDateUtc = UtcNow.UtcDateTime.AddDays(-1),
            NextPaymentAmount = 10m,
            NextPaymentCurrency = "EUR",
            RawPayloadJson = "{}",
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-2),
            UpdatedUtc = UtcNow.UtcDateTime.AddHours(-1)
        });
        await dbContext.SaveChangesAsync();

        var item = Assert.Single((await CreateService(dbContext, seeded.UserId)
            .ListAsync(10, CancellationToken.None)).Value!.Items);

        Assert.Equal("expired", item.Lifecycle);
        Assert.Contains("final_date_elapsed", item.Exclusions);
    }

    [Fact]
    public async Task ListAsync_BoundsOutputAndReportsTruncation()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        for (var index = 0; index < 4; index++)
        {
            dbContext.BankDirectDebits.Add(CreateDirectDebit(
                seeded.LinkedBankAccountId,
                $"Bill {index}",
                UtcNow.UtcDateTime.AddDays(index + 1)));
        }

        await dbContext.SaveChangesAsync();

        var response = (await CreateService(dbContext, seeded.UserId)
            .ListAsync(2, CancellationToken.None)).Value!;

        Assert.True(response.IsTruncated);
        Assert.Equal(2, response.Limit);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(["Bill 0", "Bill 1"], response.Items.Select(item => item.Label));
    }

    [Fact]
    public async Task ListAsync_RecurringTransactionSeries_IsExplicitlyInferredAndNeedsReview()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        AddMonthlySeries(
            dbContext,
            seeded.FinancialAccountId,
            "Gym membership",
            [-49m, -50m, -51m, -50m]);
        await dbContext.SaveChangesAsync();

        var item = Assert.Single((await CreateService(dbContext, seeded.UserId)
            .ListAsync(20, CancellationToken.None)).Value!.Items);

        Assert.Equal("inferred_recurring", item.Kind);
        Assert.Equal("inferred", item.Source);
        Assert.Equal("needs_review", item.Lifecycle);
        Assert.Equal("outflow", item.Direction);
        Assert.True(item.ConfidenceScore >= 50d);
        Assert.Equal("monthly", item.Cadence);
        Assert.Equal("estimated", item.DateCertainty);
        Assert.Equal(UtcNow.UtcDateTime, item.NextDateUtc);
        Assert.Equal(50m, item.LastObservedAmount);
        Assert.Equal("EUR", item.Currency);
        Assert.Contains("requires_user_confirmation", item.Exclusions);
        Assert.Equal(4, item.Evidence.Count);
        Assert.All(item.Evidence, evidence => Assert.Equal("inferred_signal", evidence.Authority));
    }

    [Fact]
    public async Task ListAsync_ProviderRecordAbsorbsMatchingInferenceWithoutOverwritingProviderFacts()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        AddMonthlySeries(
            dbContext,
            seeded.FinancialAccountId,
            "Netflix subscription",
            [-15.99m, -15.99m, -15.99m, -15.99m]);
        dbContext.BankDirectDebits.Add(new BankDirectDebit
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = seeded.LinkedBankAccountId,
            ProviderDirectDebitId = "netflix-dd",
            Status = "active",
            MerchantName = "Netflix",
            NextPaymentDateUtc = UtcNow.UtcDateTime.AddDays(6),
            NextPaymentAmount = 15.99m,
            NextPaymentCurrency = "EUR",
            RawPayloadJson = "{}",
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-6),
            UpdatedUtc = UtcNow.UtcDateTime.AddHours(-1)
        });
        await dbContext.SaveChangesAsync();

        var item = Assert.Single((await CreateService(dbContext, seeded.UserId)
            .ListAsync(20, CancellationToken.None)).Value!.Items);

        Assert.Equal("direct_debit", item.Kind);
        Assert.Equal("provider", item.Source);
        Assert.Equal("confirmed", item.Confidence);
        Assert.Equal(UtcNow.UtcDateTime.AddDays(6), item.NextDateUtc);
        Assert.Contains(item.Evidence, evidence => evidence.Type == "provider_direct_debit");
        Assert.Contains(item.Evidence, evidence => evidence.Type == "transaction_pattern");
    }

    [Fact]
    public async Task ListAsync_InternalTransferSeries_IsExcludedFromInference()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        AddMonthlySeries(
            dbContext,
            seeded.FinancialAccountId,
            "Savings transfer",
            [-100m, -100m, -100m, -100m],
            TransactionTransferKind.LinkedInternal);
        await dbContext.SaveChangesAsync();

        var response = (await CreateService(dbContext, seeded.UserId)
            .ListAsync(20, CancellationToken.None)).Value!;

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task ListAsync_AnalyticsNeutralRelationshipSeries_IsExcludedFromInference()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");
        var transactions = AddMonthlySeries(
            dbContext,
            seeded.FinancialAccountId,
            "Savings movement",
            [-100m, -100m, -100m, -100m]);
        foreach (var transaction in transactions)
        {
            dbContext.TransactionRelationships.Add(new TransactionRelationship
            {
                Id = Guid.NewGuid(),
                RelationshipKey = $"neutral-{transaction.Id:N}",
                RelationshipType = TransactionRelationshipType.SavingsManualDeposit,
                RelationshipStatus = TransactionRelationshipStatus.Active,
                SourceTransactionId = transaction.Id,
                SourceFinancialAccountId = seeded.FinancialAccountId,
                ConfidenceScore = 100,
                ConfidenceTier = "high",
                AnalyticsTreatment = "exclude_income_expense_include_savings_flow",
                CreatedUtc = UtcNow.UtcDateTime,
                UpdatedUtc = UtcNow.UtcDateTime
            });
        }

        await dbContext.SaveChangesAsync();

        var response = (await CreateService(dbContext, seeded.UserId)
            .ListAsync(20, CancellationToken.None)).Value!;

        Assert.Empty(response.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task ListAsync_InvalidLimit_ReturnsStructuredFailure(int limit)
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedAccountAsync(dbContext, "EUR");

        var result = await CreateService(dbContext, seeded.UserId).ListAsync(limit, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("commitment_limit_invalid", result.Error!.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.StatusCode);
    }

    [Fact]
    public void ProviderQueries_TranslateWithOwnershipOrderingAndLimit()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var dbContext = new AppDbContext(options);
        var service = CreateService(dbContext, Guid.NewGuid());

        var directDebitSql = service.BuildDirectDebitQuery(Guid.NewGuid(), 11).ToQueryString();
        var standingOrderSql = service.BuildStandingOrderQuery(Guid.NewGuid(), 11).ToQueryString();
        var inferredSql = service.BuildInferredTransactionQuery(
            Guid.NewGuid(),
            UtcNow.UtcDateTime,
            500).ToQueryString();

        Assert.Contains("BankDirectDebits", directDebitSql, StringComparison.Ordinal);
        Assert.Contains("BankStandingOrders", standingOrderSql, StringComparison.Ordinal);
        Assert.Contains("UserId", directDebitSql, StringComparison.Ordinal);
        Assert.Contains("UserId", standingOrderSql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", directDebitSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", standingOrderSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", directDebitSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", standingOrderSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Transactions", inferredSql, StringComparison.Ordinal);
        Assert.Contains("UserId", inferredSql, StringComparison.Ordinal);
        Assert.Contains("TransferKind", inferredSql, StringComparison.Ordinal);
        Assert.Contains("TransactionRelationships", inferredSql, StringComparison.Ordinal);
        Assert.Contains("AnalyticsTreatment", inferredSql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", inferredSql, StringComparison.OrdinalIgnoreCase);
    }

    private static FinancialCommitmentReadService CreateService(AppDbContext dbContext, Guid userId)
    {
        var normalizationService = new TransactionNormalizationService();
        var inferredService = new InferredFinancialCommitmentService(
            new RecurringPatternService(
                normalizationService,
                NullLogger<RecurringPatternService>.Instance),
            normalizationService);
        return new FinancialCommitmentReadService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new TestTimeProvider(UtcNow),
            inferredService,
            new FinancialCommitmentMergePolicy(normalizationService));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"financial-commitment-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid FinancialAccountId, Guid LinkedBankAccountId)> SeedLinkedAccountAsync(
        AppDbContext dbContext,
        string currency)
    {
        var userId = Guid.NewGuid();
        var financialAccountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var linkedBankAccountId = Guid.NewGuid();
        var email = $"commitments-{userId:N}@local";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Commitment Tester",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = UtcNow.UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-IE",
            PreferredCurrency = currency,
            PlanTier = "standard"
        });
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = financialAccountId,
            UserId = userId,
            Name = "Current account",
            Type = "Current",
            Currency = currency,
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-6)
        });
        dbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = "TrueLayer",
            ProviderEnvironment = "live",
            Status = "connected",
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-1),
            UpdatedUtc = UtcNow.UtcDateTime
        });
        dbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedBankAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = $"provider-{linkedBankAccountId:N}",
            DisplayName = "Current account",
            Currency = currency,
            FinancialAccountId = financialAccountId,
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-1),
            UpdatedUtc = UtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();
        return (userId, financialAccountId, linkedBankAccountId);
    }

    private static BankDirectDebit CreateDirectDebit(
        Guid linkedBankAccountId,
        string merchantName,
        DateTime nextDateUtc)
    {
        return new BankDirectDebit
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedBankAccountId,
            ProviderDirectDebitId = $"provider-{Guid.NewGuid():N}",
            Status = "active",
            MerchantName = merchantName,
            NextPaymentDateUtc = nextDateUtc,
            NextPaymentAmount = 20m,
            NextPaymentCurrency = "EUR",
            RawPayloadJson = "{}",
            CreatedUtc = UtcNow.UtcDateTime.AddMonths(-1),
            UpdatedUtc = UtcNow.UtcDateTime.AddHours(-1)
        };
    }

    private static IReadOnlyList<Transaction> AddMonthlySeries(
        AppDbContext dbContext,
        Guid financialAccountId,
        string description,
        IReadOnlyList<decimal> amounts,
        TransactionTransferKind? transferKind = null)
    {
        var transactions = new List<Transaction>();
        for (var index = 0; index < amounts.Count; index++)
        {
            var bookedAtUtc = UtcNow.UtcDateTime.AddMonths(index - amounts.Count);
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                FinancialAccountId = financialAccountId,
                Amount = amounts[index],
                Currency = "EUR",
                Description = description,
                BookedAtUtc = bookedAtUtc,
                TransferKind = transferKind,
                CreatedUtc = bookedAtUtc
            };
            transactions.Add(transaction);
            dbContext.Transactions.Add(transaction);
        }

        return transactions;
    }

    private sealed class TestCurrentUserProvider(Guid userId) : ICurrentUserProvider
    {
        public Guid UserId => userId;

        public bool TryGetUserId(out Guid resolvedUserId)
        {
            resolvedUserId = userId;
            return true;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = Guid.Empty;
            return false;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
