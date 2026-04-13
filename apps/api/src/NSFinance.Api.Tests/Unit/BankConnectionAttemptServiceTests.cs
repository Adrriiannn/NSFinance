using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class BankConnectionAttemptServiceTests
{
    [Fact]
    public async Task CreateAttemptAsync_SupersedesPriorActiveAttemptInSameLaunchContext()
    {
        await using var dbContext = CreateDbContext();
        var service = new BankConnectionAttemptService(
            dbContext,
            NullLogger<BankConnectionAttemptService>.Instance);
        var userId = Guid.NewGuid();
        var connectionA = await SeedConnectionAsync(dbContext, userId);
        var connectionB = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var first = await service.CreateAttemptAsync(
            userId,
            connectionA.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-first",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new&returnTo=/(tabs)/accounts",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);

        var second = await service.CreateAttemptAsync(
            userId,
            connectionB.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-second",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new&returnTo=/(tabs)/accounts",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);

        var refreshedFirst = await dbContext.BankConnectionAttempts.SingleAsync(x => x.Id == first.Id);
        var refreshedSecond = await dbContext.BankConnectionAttempts.SingleAsync(x => x.Id == second.Id);

        Assert.Equal(BankConnectionAttemptStatuses.Superseded, refreshedFirst.Status);
        Assert.Equal(second.Id, refreshedFirst.SupersededByAttemptId);
        Assert.Equal(BankConnectionAttemptStatuses.AwaitingCallback, refreshedSecond.Status);
    }

    [Fact]
    public async Task ConfirmAppReturnHandledAsync_MarksCompletedWhenConnectionAlreadyConnected()
    {
        await using var dbContext = CreateDbContext();
        var service = new BankConnectionAttemptService(
            dbContext,
            NullLogger<BankConnectionAttemptService>.Instance);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId, BankConnectionStatuses.ConnectedPendingSync);
        await dbContext.SaveChangesAsync();

        var attempt = await service.CreateAttemptAsync(
            userId,
            connection.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-confirm",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);
        await service.MarkCallbackReceivedAsync(attempt, CancellationToken.None);
        await service.MarkAppReturnInitiatedAsync(attempt, CancellationToken.None);

        var status = await service.ConfirmAppReturnHandledAsync(userId, attempt.Id, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(BankConnectionAttemptStatuses.Completed, status!.Status);
        Assert.True(status.SafeToClose);
        Assert.NotNull(status.CompletedUtc);
    }

    [Fact]
    public async Task FindByCallbackStateAsync_ExpiresAbandonedAttempt()
    {
        await using var dbContext = CreateDbContext();
        var service = new BankConnectionAttemptService(
            dbContext,
            NullLogger<BankConnectionAttemptService>.Instance);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var attempt = await service.CreateAttemptAsync(
            userId,
            connection.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-expire",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new",
            DateTime.UtcNow.AddMinutes(-1),
            reconnectRequested: false,
            CancellationToken.None);

        var resolved = await service.FindByCallbackStateAsync("state-expire", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(BankConnectionAttemptStatuses.Expired, resolved!.Status);
    }

    [Fact]
    public async Task GetPublicStatusAsync_RequiresValidToken()
    {
        await using var dbContext = CreateDbContext();
        var service = new BankConnectionAttemptService(
            dbContext,
            NullLogger<BankConnectionAttemptService>.Instance);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var attempt = await service.CreateAttemptAsync(
            userId,
            connection.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-public",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);

        var validStatus = await service.GetPublicStatusAsync(attempt.Id, attempt.PublicToken, CancellationToken.None);
        var invalidStatus = await service.GetPublicStatusAsync(attempt.Id, "wrong-token", CancellationToken.None);

        Assert.NotNull(validStatus);
        Assert.Null(invalidStatus);
    }

    private static async Task<OpenBankingConnection> SeedConnectionAsync(
        AppDbContext dbContext,
        Guid userId,
        string status = BankConnectionStatuses.ConnectionStarted)
    {
        var now = DateTime.UtcNow;
        if (!dbContext.Users.Local.Any(x => x.Id == userId)
            && !await dbContext.Users.AnyAsync(x => x.Id == userId))
        {
            dbContext.Users.Add(new User
            {
                Id = userId,
                PrimaryEmail = $"{userId:N}@attempt-tests.local",
                NormalizedEmail = $"{userId:N}@attempt-tests.local",
                DisplayName = "Attempt Tester",
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
        }

        var connection = new OpenBankingConnection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            Status = status,
            AuthStateNonce = Guid.NewGuid().ToString("N"),
            AuthStateExpiresUtc = now.AddMinutes(15),
            CreatedUtc = now.AddMinutes(-2),
            UpdatedUtc = now.AddMinutes(-2)
        };

        dbContext.OpenBankingConnections.Add(connection);
        return connection;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"bank-connection-attempt-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }
}
