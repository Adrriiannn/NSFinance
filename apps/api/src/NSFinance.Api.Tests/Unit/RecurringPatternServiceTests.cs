using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class RecurringPatternServiceTests
{
    private static readonly DateTime ReferenceUtc = new(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EvaluateAsync_MonthlySubscriptionPattern_ReturnsStrongMonthlyRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-9.99m, "Netflix Subscription", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-9.99m, "Netflix Subscription", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-9.99m, "Netflix Subscription", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-9.99m, "Netflix Subscription", ReferenceUtc.AddDays(-90), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Monthly, result.Cadence);
        Assert.Equal(RecurringConfidenceTier.Strong, result.ConfidenceTier);
        Assert.Contains(RecurringPatternReasonCodes.MonthlyIntervalCluster, result.ReasonCodes);
        Assert.Contains(RecurringPatternReasonCodes.MerchantExactMatch, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_WeeklyPattern_ReturnsRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-14.50m, "Gym Membership", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-14.50m, "Gym Membership", ReferenceUtc.AddDays(-7), accountId),
            BuildTransaction(-14.50m, "Gym Membership", ReferenceUtc.AddDays(-14), accountId),
            BuildTransaction(-14.50m, "Gym Membership", ReferenceUtc.AddDays(-21), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Weekly, result.Cadence);
        Assert.Contains(result.ConfidenceTier, new[] { RecurringConfidenceTier.Probable, RecurringConfidenceTier.Strong });
    }

    [Fact]
    public async Task EvaluateAsync_YearlyPattern_ReturnsRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-320.00m, "Home Insurance Renewal", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-318.00m, "Home Insurance Renewal", ReferenceUtc.AddDays(-365), accountId),
            BuildTransaction(-315.00m, "Home Insurance Renewal", ReferenceUtc.AddDays(-730), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Yearly, result.Cadence);
        Assert.Contains(RecurringPatternReasonCodes.YearlyIntervalCluster, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_GroceryPattern_IsBlockedAsDiscretionary()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-73.22m, "Tesco Grocery", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-69.85m, "Tesco Grocery", ReferenceUtc.AddDays(-7), accountId),
            BuildTransaction(-74.01m, "Tesco Grocery", ReferenceUtc.AddDays(-14), accountId),
            BuildTransaction(-71.60m, "Tesco Grocery", ReferenceUtc.AddDays(-21), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringConfidenceTier.None, result.ConfidenceTier);
        Assert.Contains(RecurringPatternReasonCodes.BlockedByDiscretionaryMerchant, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_RandomIntervals_RemainsNotRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-18.20m, "Online Service Charge", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-18.20m, "Online Service Charge", ReferenceUtc.AddDays(-3), accountId),
            BuildTransaction(-18.20m, "Online Service Charge", ReferenceUtc.AddDays(-19), accountId),
            BuildTransaction(-18.20m, "Online Service Charge", ReferenceUtc.AddDays(-57), accountId),
            BuildTransaction(-18.20m, "Online Service Charge", ReferenceUtc.AddDays(-92), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringConfidenceTier.None, result.ConfidenceTier);
        Assert.Contains(RecurringPatternReasonCodes.BlockedByHighIntervalVariance, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_MixedDirections_IsBlocked()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-42m, "Cloud Storage Pro", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-42m, "Cloud Storage Pro", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-42m, "Cloud Storage Pro", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(42m, "Cloud Storage Pro", ReferenceUtc.AddDays(-90), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringConfidenceTier.None, result.ConfidenceTier);
        Assert.Contains(RecurringPatternReasonCodes.BlockedByMixedDirection, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_MonthlyWithDateDrift_StillDetectsRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-11.99m, "Music Streaming", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-11.99m, "Music Streaming", ReferenceUtc.AddDays(-29), accountId),
            BuildTransaction(-11.99m, "Music Streaming", ReferenceUtc.AddDays(-61), accountId),
            BuildTransaction(-11.99m, "Music Streaming", ReferenceUtc.AddDays(-91), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Monthly, result.Cadence);
    }

    [Fact]
    public async Task EvaluateAsync_AmountDriftWithinTolerance_StillDetectsRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-20.00m, "Developer SaaS", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-19.20m, "Developer SaaS", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-20.40m, "Developer SaaS", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-19.85m, "Developer SaaS", ReferenceUtc.AddDays(-90), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Contains(RecurringPatternReasonCodes.AmountWithinTolerance, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_MissingOneCycle_StillDetectsRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-30m, "Professional Tooling", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-30m, "Professional Tooling", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-30m, "Professional Tooling", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(-30m, "Professional Tooling", ReferenceUtc.AddDays(-120), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Monthly, result.Cadence);
        Assert.Contains(RecurringPatternReasonCodes.MissingCycleGap, result.ReasonCodes);
    }

    [Theory]
    [InlineData("Restaurant Card Spend")]
    [InlineData("Amazon Shopping")]
    [InlineData("Supermarket Grocery")]
    public async Task EvaluateAsync_DiscretionaryMerchantRegression_DoesNotFalsePositive(string description)
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-25.00m, description, ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-24.50m, description, ReferenceUtc.AddDays(-7), accountId),
            BuildTransaction(-26.30m, description, ReferenceUtc.AddDays(-14), accountId),
            BuildTransaction(-25.15m, description, ReferenceUtc.AddDays(-21), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringConfidenceTier.None, result.ConfidenceTier);
    }

    [Fact]
    public async Task EvaluateAsync_TwoPriorMatches_CanStillReturnRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-5.49m, "Cloud Backup", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-5.49m, "Cloud Backup", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-5.49m, "Cloud Backup", ReferenceUtc.AddDays(-60), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.True(result.OccurrenceCount >= 3);
    }

    [Fact]
    public async Task EvaluateAsync_LargeHistorySet_RemainsStableAndReturnsResult()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-8.99m, "Streaming Max", ReferenceUtc, accountId);
        var history = new List<Transaction>(10000);

        for (var i = 1; i <= 10000; i++)
        {
            var amount = i % 100 == 0 ? -8.99m : -Math.Round(5m + (i % 30), 2);
            var description = i % 100 == 0 ? "Streaming Max" : $"Random merchant {i}";
            history.Add(BuildTransaction(amount, description, ReferenceUtc.AddDays(-i), accountId));
        }

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.OccurrenceCount >= 1);
    }

    [Fact]
    public async Task DeterministicPipeline_InvokesRecurringService_ForTargetTransactions()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "recurring@test.local",
            NormalizedEmail = "recurring@test.local",
            DisplayName = "Recurring Test User",
            FullName = "Recurring Test User",
            CreatedUtc = ReferenceUtc,
            UpdatedUtc = ReferenceUtc
        });
        dbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = "TrueLayer",
            Status = "connected",
            CreatedUtc = ReferenceUtc,
            UpdatedUtc = ReferenceUtc
        });
        dbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            ProviderAccountId = "provider-account-1",
            DisplayName = "Main",
            Currency = "EUR",
            FinancialAccountId = accountId,
            CreatedUtc = ReferenceUtc,
            UpdatedUtc = ReferenceUtc
        });

        var candidate = BuildTransaction(-9.99m, "Streaming Max", ReferenceUtc, accountId);
        var history = BuildTransaction(-9.99m, "Streaming Max", ReferenceUtc.AddDays(-30), accountId);
        dbContext.Transactions.AddRange(candidate, history);
        dbContext.NormalizedBankTransactions.AddRange(
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                ProjectedTransactionId = candidate.Id,
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                LastNormalizedUtc = ReferenceUtc
            },
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                ProjectedTransactionId = history.Id,
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                LastNormalizedUtc = ReferenceUtc
            });
        await dbContext.SaveChangesAsync();

        var normalization = new TransactionNormalizationService();
        var spyRecurringService = new SpyRecurringPatternService();
        var persistence = new DeterministicClassificationPersistenceService(
            dbContext,
            normalization,
            new TransactionFeatureExtractor(
                normalization,
                new ProviderCapabilityRegistry(),
                new NarrativeSignalExtractor()),
            spyRecurringService,
            new TransferPairingEngine(),
            new SavingsRoutingPolicy(),
            new SavingsTransferClassifier(),
            new DeterministicClassificationRetryPlanner(),
            new DeterministicCategorizationMetrics(),
            NullLogger<DeterministicClassificationPersistenceService>.Instance);

        await persistence.EvaluateTransactionsAsync(
            userId,
            new[] { candidate.Id },
            contextStartUtc: ReferenceUtc.AddMonths(-6),
            contextEndUtc: ReferenceUtc.AddDays(1),
            now: ReferenceUtc.AddMinutes(1),
            cancellationToken: CancellationToken.None);

        Assert.True(spyRecurringService.CallCount >= 1);
    }

    private static RecurringPatternService CreateService()
    {
        return new RecurringPatternService(
            new TransactionNormalizationService(),
            NullLogger<RecurringPatternService>.Instance);
    }

    private static Transaction BuildTransaction(decimal amount, string description, DateTime bookedAtUtc, Guid accountId)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = amount,
            Currency = "EUR",
            Description = description,
            BookedAtUtc = bookedAtUtc,
            CreatedUtc = bookedAtUtc
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"recurring-pattern-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private sealed class SpyRecurringPatternService : IRecurringPatternService
    {
        public int CallCount { get; private set; }

        public Task<RecurringPatternResult> EvaluateAsync(
            Transaction candidate,
            IReadOnlyList<Transaction> historicalTransactions,
            RecurringPatternOptions options,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(RecurringPatternResult.None());
        }
    }
}
