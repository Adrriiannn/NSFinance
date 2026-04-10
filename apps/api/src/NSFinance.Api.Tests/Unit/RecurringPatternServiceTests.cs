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
    public async Task EvaluateAsync_FirstOccurrenceWithNoHistory_ReturnsNone()
    {
        var service = CreateService();
        var candidate = BuildTransaction(-15.99m, "NETFLIX.COM", ReferenceUtc, Guid.NewGuid());

        var result = await service.EvaluateAsync(candidate, [], new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringConfidenceTier.None, result.ConfidenceTier);
        Assert.Contains(RecurringPatternReasonCodes.MinimumPriorMatchesNotMet, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_StableMonthlySpotify_ReturnsStrongRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-120), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Monthly, result.Cadence);
        Assert.Equal(RecurringIntervalStabilityTier.Strong, result.IntervalStabilityTier);
        Assert.Equal(RecurringAmountStabilityTier.Exact, result.AmountStabilityTier);
        Assert.True(result.CadenceConfidence >= 0.8d);
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
    public async Task EvaluateAsync_YearlyInsuranceRenewal_WithDrift_ReturnsRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-320.00m, "Acme Insurance Renewal", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-300.00m, "Acme Insurance Renewal", ReferenceUtc.AddDays(-362), accountId),
            BuildTransaction(-290.00m, "Acme Insurance Renewal", ReferenceUtc.AddDays(-729), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Yearly, result.Cadence);
        Assert.True(result.CadenceConfidence >= 0.55d);
    }

    [Fact]
    public async Task EvaluateAsync_UtilityAmountDateDrift_ReturnsRecurringWithDriftTier()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-71.33m, "City Water Bill", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-66.11m, "City Water Bill", ReferenceUtc.AddDays(-28), accountId),
            BuildTransaction(-72.44m, "City Water Bill", ReferenceUtc.AddDays(-61), accountId),
            BuildTransaction(-69.92m, "City Water Bill", ReferenceUtc.AddDays(-91), accountId),
            BuildTransaction(-64.78m, "City Water Bill", ReferenceUtc.AddDays(-121), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Equal(RecurringCadence.Monthly, result.Cadence);
        Assert.NotEqual(RecurringAmountStabilityTier.Chaotic, result.AmountStabilityTier);
    }

    [Fact]
    public async Task EvaluateAsync_TescoRepeatedSpend_IsBlockedAsRepeatedUsage()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-74.21m, "Tesco Grocery", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-35.20m, "Tesco Grocery", ReferenceUtc.AddDays(-2), accountId),
            BuildTransaction(-92.11m, "Tesco Grocery", ReferenceUtc.AddDays(-9), accountId),
            BuildTransaction(-19.44m, "Tesco Grocery", ReferenceUtc.AddDays(-15), accountId),
            BuildTransaction(-81.35m, "Tesco Grocery", ReferenceUtc.AddDays(-20), accountId),
            BuildTransaction(-45.01m, "Tesco Grocery", ReferenceUtc.AddDays(-27), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringConfidenceTier.None, result.ConfidenceTier);
        Assert.True(result.IsRepeatedUsagePattern);
        Assert.Contains(RecurringPatternReasonCodes.BlockedByRepeatedUsage, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_LoanWithSkippedCycle_PreservesRecurrence()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-250.00m, "Loan Repayment", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-250.00m, "Loan Repayment", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-250.00m, "Loan Repayment", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(-250.00m, "Loan Repayment", ReferenceUtc.AddDays(-120), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.True(result.HasSkippedCycle);
        Assert.Equal(RecurringCadence.Monthly, result.Cadence);
    }

    [Fact]
    public async Task EvaluateAsync_RefundRowAgainstRecurringOutflow_DoesNotBecomeRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var refundCandidate = BuildTransaction(12.99m, "Spotify Premium Refund", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-90), accountId)
        };

        var result = await service.EvaluateAsync(refundCandidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringConfidenceTier.None, result.ConfidenceTier);
        Assert.Contains(RecurringPatternReasonCodes.MinimumPriorMatchesNotMet, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_OutflowSeriesWithOneRefund_StillRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-12.99m, "Spotify Premium", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(12.99m, "Spotify Premium Refund", ReferenceUtc.AddDays(-5), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Contains(RecurringPatternReasonCodes.OppositeDirectionReversalObserved, result.ReasonCodes);
        Assert.False(result.HasDirectionConflict);
    }

    [Fact]
    public async Task EvaluateAsync_MixedUseAmazon_PrimeSeriesIsIsolated()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var primeOne = BuildTransaction(-8.99m, "AMAZON*PRIME", ReferenceUtc.AddDays(-30), accountId);
        var primeTwo = BuildTransaction(-8.99m, "AMAZON*PRIME", ReferenceUtc.AddDays(-60), accountId);
        var primeThree = BuildTransaction(-8.99m, "AMAZON*PRIME", ReferenceUtc.AddDays(-90), accountId);
        var shoppingA = BuildTransaction(-44.11m, "AMAZON EU ORDER 1001", ReferenceUtc.AddDays(-3), accountId);
        var shoppingB = BuildTransaction(-19.07m, "AMAZON EU ORDER 1002", ReferenceUtc.AddDays(-11), accountId);
        var shoppingC = BuildTransaction(-77.22m, "AMAZON EU ORDER 1003", ReferenceUtc.AddDays(-18), accountId);
        var candidate = BuildTransaction(-8.99m, "AMAZON*PRIME", ReferenceUtc, accountId);

        var history = new[] { primeOne, primeTwo, primeThree, shoppingA, shoppingB, shoppingC };
        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.Contains(primeOne.Id, result.MatchedTransactionIds);
        Assert.Contains(primeTwo.Id, result.MatchedTransactionIds);
        Assert.Contains(primeThree.Id, result.MatchedTransactionIds);
        Assert.DoesNotContain(shoppingA.Id, result.MatchedTransactionIds);
        Assert.DoesNotContain(shoppingB.Id, result.MatchedTransactionIds);
        Assert.DoesNotContain(shoppingC.Id, result.MatchedTransactionIds);
        Assert.Contains(RecurringPatternReasonCodes.MixedUseSignatureIsolated, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_ModerateAmountUpgrade_PreservesRecurrenceWithShiftFlag()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-21.99m, "Streaming Plus", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-12.99m, "Streaming Plus", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-12.99m, "Streaming Plus", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-12.99m, "Streaming Plus", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(-12.99m, "Streaming Plus", ReferenceUtc.AddDays(-120), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.True(result.AmountChangeDetected);
        Assert.Equal(RecurringAmountStabilityTier.Shifted, result.AmountStabilityTier);
        Assert.False(result.MajorAmountShiftDetected);
    }

    [Fact]
    public async Task EvaluateAsync_MajorAmountUpgrade_PreservesRecurrenceWithMajorShiftFlag()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-200.00m, "Pro Suite Subscription", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-20.00m, "Pro Suite Subscription", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-20.00m, "Pro Suite Subscription", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-20.00m, "Pro Suite Subscription", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(-20.00m, "Pro Suite Subscription", ReferenceUtc.AddDays(-120), accountId),
            BuildTransaction(-20.00m, "Pro Suite Subscription", ReferenceUtc.AddDays(-150), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.True(result.IsRecurring);
        Assert.True(result.AmountChangeDetected);
        Assert.True(result.MajorAmountShiftDetected);
        Assert.Equal(RecurringAmountStabilityTier.MajorShift, result.AmountStabilityTier);
    }

    [Fact]
    public async Task EvaluateAsync_ChaoticAmountHistory_RemainsNotRecurring()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-48.00m, "Service X", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-9.00m, "Service X", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-175.00m, "Service X", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-200.00m, "Service X", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(-12.00m, "Service X", ReferenceUtc.AddDays(-120), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.Equal(RecurringAmountStabilityTier.Chaotic, result.AmountStabilityTier);
        Assert.Contains(RecurringPatternReasonCodes.BlockedByHighAmountVariance, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleOppositeDirectionConflicts_BlocksRecurrence()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-49.99m, "Cloud Storage Pro", ReferenceUtc, accountId);
        var history = new[]
        {
            BuildTransaction(-49.99m, "Cloud Storage Pro", ReferenceUtc.AddDays(-30), accountId),
            BuildTransaction(-49.99m, "Cloud Storage Pro", ReferenceUtc.AddDays(-60), accountId),
            BuildTransaction(-49.99m, "Cloud Storage Pro", ReferenceUtc.AddDays(-90), accountId),
            BuildTransaction(49.99m, "Cloud Storage Pro payout", ReferenceUtc.AddDays(-15), accountId),
            BuildTransaction(49.99m, "Cloud Storage Pro payout", ReferenceUtc.AddDays(-45), accountId)
        };

        var result = await service.EvaluateAsync(candidate, history, new RecurringPatternOptions(), CancellationToken.None);

        Assert.False(result.IsRecurring);
        Assert.True(result.HasDirectionConflict);
        Assert.True(result.DirectionConflictCount >= 2);
        Assert.Contains(RecurringPatternReasonCodes.BlockedByMixedDirection, result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_MonthlyDateDrift_StillRecurring()
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
    public async Task EvaluateAsync_LargeHistorySet_RemainsStableAndReturnsResult()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var candidate = BuildTransaction(-8.99m, "Streaming Max", ReferenceUtc, accountId);
        var history = new List<Transaction>(10000);

        for (var i = 1; i <= 10000; i++)
        {
            var amount = i % 80 == 0 ? -8.99m : -Math.Round(6m + (i % 45), 2);
            var description = i % 80 == 0 ? "Streaming Max" : $"Random merchant {i}";
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
