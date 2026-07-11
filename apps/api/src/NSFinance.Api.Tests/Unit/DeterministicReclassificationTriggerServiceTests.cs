using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class DeterministicReclassificationTriggerServiceTests
{
    [Fact]
    public async Task TriggerAsync_WithConnectionScope_MarksConnectionAndQueuesWork()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        dbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "live",
            Status = BankConnectionStatuses.Synced,
            NeedsHistoricalReclassification = false,
            HistoricalEnrichmentStartedUtc = now.AddMinutes(-3),
            HistoricalEnrichmentCompletedUtc = now.AddMinutes(-1),
            HistoricalEnrichmentCheckpointUtc = now.AddMinutes(-1),
            CreatedUtc = now.AddDays(-1),
            UpdatedUtc = now.AddMinutes(-1)
        });
        await dbContext.SaveChangesAsync();

        var queue = new RecordingQueue();
        var service = new DeterministicReclassificationTriggerService(
            dbContext,
            queue,
            NullLogger<DeterministicReclassificationTriggerService>.Instance);

        var result = await service.TriggerAsync(
            new DeterministicReclassificationTriggerRequest(
                UserId: userId,
                Source: "unit_test",
                ReasonCode: DeterministicReclassificationTriggerReasons.SyncChangesManualRefresh,
                ConnectionIds: [connectionId],
                MarkConnectionsForHistoricalReplay: true,
                QueueConnections: true),
            CancellationToken.None);

        var connection = await dbContext.OpenBankingConnections.SingleAsync(x => x.Id == connectionId);
        Assert.True(connection.NeedsHistoricalReclassification);
        Assert.Null(connection.HistoricalEnrichmentStartedUtc);
        Assert.Null(connection.HistoricalEnrichmentCompletedUtc);
        Assert.Null(connection.HistoricalEnrichmentCheckpointUtc);
        Assert.Equal(1, result.ConnectionsResolved);
        Assert.Equal(1, result.ConnectionsMarked);
        Assert.Equal(1, result.QueueRequestsAttempted);
        Assert.Equal(0, result.QueueFailures);
        Assert.Contains(
            queue.Items,
            item => item.ConnectionId == connectionId && item.Reason == DeterministicReclassificationTriggerReasons.SyncChangesManualRefresh);
    }

    [Fact]
    public async Task TriggerAsync_WithTransactionScope_ResolvesImpactedConnection()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var financialAccountId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "deterministic-trigger-tests@local",
            NormalizedEmail = "deterministic-trigger-tests@local",
            DisplayName = "Deterministic Trigger Tester",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = now,
            UpdatedUtc = now,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-GB",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        });
        dbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "live",
            Status = BankConnectionStatuses.Synced,
            CreatedUtc = now.AddDays(-1),
            UpdatedUtc = now.AddMinutes(-5)
        });
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = financialAccountId,
            UserId = userId,
            Name = "Main Current",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-1)
        });
        dbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            ProviderAccountId = "provider-account-1",
            DisplayName = "Main Current",
            AccountType = "current",
            Currency = "EUR",
            FinancialAccountId = financialAccountId,
            CreatedUtc = now.AddDays(-1),
            UpdatedUtc = now.AddDays(-1)
        });
        dbContext.Transactions.Add(new Transaction
        {
            Id = transactionId,
            FinancialAccountId = financialAccountId,
            Amount = -42m,
            Currency = "EUR",
            Description = "Scope resolver row",
            BookedAtUtc = now.AddHours(-6),
            CreatedUtc = now.AddHours(-6)
        });
        await dbContext.SaveChangesAsync();

        var queue = new RecordingQueue();
        var service = new DeterministicReclassificationTriggerService(
            dbContext,
            queue,
            NullLogger<DeterministicReclassificationTriggerService>.Instance);

        var result = await service.TriggerAsync(
            new DeterministicReclassificationTriggerRequest(
                UserId: userId,
                Source: "unit_test",
                ReasonCode: DeterministicReclassificationTriggerReasons.ProjectedRowRemapOrDedupeCorrection,
                TransactionIds: [transactionId],
                MarkConnectionsForHistoricalReplay: true,
                QueueConnections: true),
            CancellationToken.None);

        Assert.Equal(1, result.ConnectionsResolved);
        Assert.Contains(queue.Items, item => item.ConnectionId == connectionId);
    }

    [Fact]
    public async Task TriggerAsync_RepeatedCall_IsIdempotentForConnectionMarking()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        dbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = userId,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "live",
            Status = BankConnectionStatuses.Synced,
            NeedsHistoricalReclassification = false,
            CreatedUtc = now.AddDays(-2),
            UpdatedUtc = now.AddDays(-1)
        });
        await dbContext.SaveChangesAsync();

        var queue = new RecordingQueue();
        var service = new DeterministicReclassificationTriggerService(
            dbContext,
            queue,
            NullLogger<DeterministicReclassificationTriggerService>.Instance);

        var request = new DeterministicReclassificationTriggerRequest(
            UserId: userId,
            Source: "unit_test",
            ReasonCode: DeterministicReclassificationTriggerReasons.SyncChangesAutoRefresh,
            ConnectionIds: [connectionId],
            MarkConnectionsForHistoricalReplay: true,
            QueueConnections: false);

        var first = await service.TriggerAsync(request, CancellationToken.None);
        var second = await service.TriggerAsync(request, CancellationToken.None);

        Assert.Equal(1, first.ConnectionsMarked);
        Assert.Equal(0, second.ConnectionsMarked);
        Assert.Equal(0, second.QueueRequestsAttempted);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"deterministic-trigger-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private sealed class RecordingQueue : IBankDeterministicEnrichmentQueue
    {
        private readonly List<(Guid UserId, Guid ConnectionId, string Reason)> _items = [];

        public IReadOnlyCollection<(Guid UserId, Guid ConnectionId, string Reason)> Items => _items;

        public ValueTask QueueConnectionAsync(
            Guid userId,
            Guid connectionId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            _items.Add((userId, connectionId, reason));
            return ValueTask.CompletedTask;
        }
    }
}
