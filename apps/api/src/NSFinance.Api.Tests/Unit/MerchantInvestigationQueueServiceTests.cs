using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class MerchantInvestigationQueueServiceTests
{
    [Fact]
    public async Task EvaluateAsync_PrioritizesHigherValueMerchant()
    {
        await using var dbContext = CreateDbContext();
        var nowUtc = DateTime.UtcNow;
        var low = new UnresolvedMerchant
        {
            Id = Guid.NewGuid(),
            RawDescriptor = "LOW VALUE SHOP",
            NormalizedDescriptor = "low value shop",
            FirstSeenUtc = nowUtc.AddDays(-2),
            LastSeenUtc = nowUtc.AddDays(-1),
            OccurrenceCount = 2,
            Status = UnresolvedMerchantStatus.New,
            TotalObservedSpendAbs = 12m
        };
        var high = new UnresolvedMerchant
        {
            Id = Guid.NewGuid(),
            RawDescriptor = "HIGH VALUE RENT PAYMENT",
            NormalizedDescriptor = "high value rent payment",
            FirstSeenUtc = nowUtc.AddDays(-8),
            LastSeenUtc = nowUtc.AddHours(-4),
            OccurrenceCount = 9,
            Status = UnresolvedMerchantStatus.New,
            TotalObservedSpendAbs = 1200m
        };
        dbContext.UnresolvedMerchants.AddRange(low, high);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var lowEval = await service.EvaluateAsync(
            new MerchantInvestigationQueueEvaluationRequest(
                ResolutionRequest: BuildRequest(low.RawDescriptor, -12m),
                UnresolvedMerchant: low,
                TriggerMode: DomainTriggerMode.D2),
            CancellationToken.None);
        var highEval = await service.EvaluateAsync(
            new MerchantInvestigationQueueEvaluationRequest(
                ResolutionRequest: BuildRequest(high.RawDescriptor, -600m),
                UnresolvedMerchant: high,
                TriggerMode: DomainTriggerMode.D2),
            CancellationToken.None);

        Assert.True(highEval.PriorityScore > lowEval.PriorityScore);
        Assert.True(highEval.QueuePosition <= lowEval.QueuePosition);
    }

    [Fact]
    public async Task TryAcquireLockAsync_BlocksConcurrentInvestigation()
    {
        await using var dbContext = CreateDbContext();
        var unresolved = new UnresolvedMerchant
        {
            Id = Guid.NewGuid(),
            RawDescriptor = "LOCK TEST",
            NormalizedDescriptor = "lock test",
            FirstSeenUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow,
            OccurrenceCount = 2,
            Status = UnresolvedMerchantStatus.New
        };
        dbContext.UnresolvedMerchants.Add(unresolved);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var first = await service.TryAcquireLockAsync(unresolved.Id, CancellationToken.None);
        var second = await service.TryAcquireLockAsync(unresolved.Id, CancellationToken.None);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.NotNull(first.LockId);
    }

    [Fact]
    public async Task ReleaseLockAsync_FailedInvestigation_IncrementsRetryCounter()
    {
        await using var dbContext = CreateDbContext();
        var unresolved = new UnresolvedMerchant
        {
            Id = Guid.NewGuid(),
            RawDescriptor = "RETRY TEST",
            NormalizedDescriptor = "retry test",
            FirstSeenUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow,
            OccurrenceCount = 3,
            Status = UnresolvedMerchantStatus.New
        };
        dbContext.UnresolvedMerchants.Add(unresolved);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var lockResult = await service.TryAcquireLockAsync(unresolved.Id, CancellationToken.None);
        Assert.True(lockResult.Acquired);
        await service.ReleaseLockAsync(unresolved.Id, lockResult.LockId!.Value, markFailed: true, CancellationToken.None);

        var refreshed = await dbContext.UnresolvedMerchants.SingleAsync(x => x.Id == unresolved.Id);
        Assert.False(refreshed.InvestigationInProgress);
        Assert.Equal(1, refreshed.QueueRetryCount);
        Assert.Null(refreshed.InvestigationLockId);
    }

    private static MerchantInvestigationQueueService CreateService(AppDbContext dbContext)
    {
        return new MerchantInvestigationQueueService(
            dbContext,
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true,
                QueueTopMerchantsPerRun = 12,
                QueueTopMerchantsPerConnectionPerRun = 8
            }));
    }

    private static MerchantResolutionRequest BuildRequest(string descriptor, decimal amount)
    {
        return new MerchantResolutionRequest(
            RawDescriptor: descriptor,
            UserId: Guid.NewGuid(),
            ConnectionId: Guid.NewGuid(),
            SyncRunId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            NormalizedTransactionId: Guid.NewGuid(),
            TaxonomyDomainId: 130,
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: null,
            DeterministicTerminal: false,
            DeterministicResultCode: "not_terminal",
            ManualOverridePresent: false,
            Amount: amount,
            DescriptorMerchantLike: true,
            TriggerSource: "unit_test",
            RunState: new MerchantResolutionRunState(Guid.NewGuid()));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"merchant-investigation-queue-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }
}
