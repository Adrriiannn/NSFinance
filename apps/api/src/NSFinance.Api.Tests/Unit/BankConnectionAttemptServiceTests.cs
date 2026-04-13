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
        var service = CreateService(dbContext);
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

        await Task.Delay(2);

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
    public async Task CreateAttemptAsync_RapidRepeatedLaunches_LeaveSingleActiveOwner()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connectionA = await SeedConnectionAsync(dbContext, userId);
        var connectionB = await SeedConnectionAsync(dbContext, userId);
        var connectionC = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();
        const string launchUri = "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new&returnTo=/(tabs)/accounts";

        var first = await service.CreateAttemptAsync(
            userId,
            connectionA.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-rapid-a",
            launchUri,
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);
        await Task.Delay(2);
        var second = await service.CreateAttemptAsync(
            userId,
            connectionB.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-rapid-b",
            launchUri,
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);
        await Task.Delay(2);
        var third = await service.CreateAttemptAsync(
            userId,
            connectionC.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-rapid-c",
            launchUri,
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);

        var attempts = await dbContext.BankConnectionAttempts
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();

        Assert.Equal(3, attempts.Count);
        Assert.Equal(BankConnectionAttemptStatuses.AwaitingCallback, attempts.Single(x => x.Id == third.Id).Status);
        Assert.All(
            attempts.Where(x => x.Id != third.Id),
            attempt => Assert.Equal(BankConnectionAttemptStatuses.Superseded, attempt.Status));
        Assert.NotNull(attempts.Single(x => x.Id == first.Id).SupersededByAttemptId);
        Assert.NotNull(attempts.Single(x => x.Id == second.Id).SupersededByAttemptId);
    }

    [Fact]
    public async Task ConfirmAppReturnHandledAsync_MarksCompletedWhenConnectionAlreadyConnected()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
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
    public async Task ConfirmAppReturnHandledAsync_SupersededAttempt_RemainsSuperseded()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connectionA = await SeedConnectionAsync(dbContext, userId, BankConnectionStatuses.ConnectedPendingSync);
        var connectionB = await SeedConnectionAsync(dbContext, userId, BankConnectionStatuses.ConnectedPendingSync);
        await dbContext.SaveChangesAsync();
        const string launchUri = "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new&returnTo=/(tabs)/accounts";

        var first = await service.CreateAttemptAsync(
            userId,
            connectionA.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-confirm-sup-a",
            launchUri,
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);
        await Task.Delay(2);
        var second = await service.CreateAttemptAsync(
            userId,
            connectionB.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-confirm-sup-b",
            launchUri,
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);

        var firstStatus = await service.ConfirmAppReturnHandledAsync(userId, first.Id, CancellationToken.None);
        var secondStatus = await service.ConfirmAppReturnHandledAsync(userId, second.Id, CancellationToken.None);

        Assert.NotNull(firstStatus);
        Assert.Equal(BankConnectionAttemptStatuses.Superseded, firstStatus!.Status);
        Assert.NotNull(secondStatus);
        Assert.Equal(BankConnectionAttemptStatuses.Completed, secondStatus!.Status);
    }

    [Fact]
    public async Task FindByCallbackStateAsync_ExpiresAbandonedAttempt()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        await service.CreateAttemptAsync(
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
    public async Task SweepLifecycleAsync_ExpiresStaleProcessingAttemptsProactively()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        dbContext.BankConnectionAttempts.Add(new BankConnectionAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectionId = connection.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            Status = BankConnectionAttemptStatuses.Processing,
            CallbackState = "state-processing-stale",
            PublicToken = "public-token-processing-stale",
            CreatedUtc = now.AddHours(-4),
            UpdatedUtc = now.AddHours(-3),
            ExpiresUtc = now.AddMinutes(-10),
            AppReturnConfirmedUtc = now.AddHours(-3),
            TransitionVersion = 0
        });
        await dbContext.SaveChangesAsync();

        var sweep = await service.SweepLifecycleAsync(
            batchSize: 16,
            staleProcessingExpiryAge: TimeSpan.FromMinutes(30),
            cancellationToken: CancellationToken.None);

        var refreshed = await dbContext.BankConnectionAttempts
            .SingleAsync(x => x.CallbackState == "state-processing-stale");
        Assert.Equal(BankConnectionAttemptStatuses.Expired, refreshed.Status);
        Assert.True(sweep.ExpiredCount >= 1);
    }

    [Fact]
    public async Task SweepLifecycleAsync_DoesNotExpireFreshActiveAttempt()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var freshAttempt = await service.CreateAttemptAsync(
            userId,
            connection.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-fresh-not-expired",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);

        var sweep = await service.SweepLifecycleAsync(
            batchSize: 16,
            staleProcessingExpiryAge: TimeSpan.FromMinutes(15),
            cancellationToken: CancellationToken.None);

        var refreshed = await dbContext.BankConnectionAttempts.SingleAsync(x => x.Id == freshAttempt.Id);
        Assert.Equal(BankConnectionAttemptStatuses.AwaitingCallback, refreshed.Status);
        Assert.Equal(0, sweep.ExpiredCount);
    }

    [Fact]
    public async Task SweepLifecycleAsync_SupersedesDuplicateActiveAttemptsInSameScope()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var winnerId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        dbContext.BankConnectionAttempts.AddRange(
            new BankConnectionAttempt
            {
                Id = winnerId,
                UserId = userId,
                ConnectionId = connection.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                Status = BankConnectionAttemptStatuses.CallbackReceived,
                LaunchOriginPath = "/(tabs)/accounts",
                CallbackState = "state-duplicate-winner",
                PublicToken = "public-token-duplicate-winner",
                CreatedUtc = now.AddMinutes(-1),
                UpdatedUtc = now.AddMinutes(-1),
                ExpiresUtc = now.AddMinutes(10),
                TransitionVersion = 0
            },
            new BankConnectionAttempt
            {
                Id = staleId,
                UserId = userId,
                ConnectionId = connection.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                Status = BankConnectionAttemptStatuses.AwaitingCallback,
                LaunchOriginPath = "/(tabs)/accounts",
                CallbackState = "state-duplicate-stale",
                PublicToken = "public-token-duplicate-stale",
                CreatedUtc = now.AddMinutes(-4),
                UpdatedUtc = now.AddMinutes(-4),
                ExpiresUtc = now.AddMinutes(8),
                TransitionVersion = 0
            });
        await dbContext.SaveChangesAsync();

        var sweep = await service.SweepLifecycleAsync(
            batchSize: 16,
            staleProcessingExpiryAge: TimeSpan.FromMinutes(60),
            cancellationToken: CancellationToken.None);

        var stale = await dbContext.BankConnectionAttempts.SingleAsync(x => x.Id == staleId);
        Assert.Equal(BankConnectionAttemptStatuses.Superseded, stale.Status);
        Assert.Equal(winnerId, stale.SupersededByAttemptId);
        Assert.True(sweep.SupersededCount >= 1);
    }

    [Fact]
    public async Task MarkAppReturnInitiatedAsync_DoesNotRegressTerminalAttempt()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var attempt = await service.CreateAttemptAsync(
            userId,
            connection.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-terminal-no-regress",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);
        await service.MarkCompletedAsync(attempt, CancellationToken.None);
        var versionAfterComplete = attempt.TransitionVersion;

        await service.MarkAppReturnInitiatedAsync(attempt, CancellationToken.None);

        Assert.Equal(BankConnectionAttemptStatuses.Completed, attempt.Status);
        Assert.Equal(versionAfterComplete, attempt.TransitionVersion);
    }

    [Fact]
    public async Task MarkCallbackReceivedAsync_InvalidTransitionFromProcessing_IsBlocked()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var attempt = await service.CreateAttemptAsync(
            userId,
            connection.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-invalid-transition",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);
        attempt.Status = BankConnectionAttemptStatuses.Processing;
        attempt.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        var versionBefore = attempt.TransitionVersion;

        await service.MarkCallbackReceivedAsync(attempt, CancellationToken.None);

        Assert.Equal(BankConnectionAttemptStatuses.Processing, attempt.Status);
        Assert.Equal(versionBefore, attempt.TransitionVersion);
    }

    [Fact]
    public async Task GetPublicStatusAsync_RequiresValidToken()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
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

    [Fact]
    public async Task TransitionVersion_IncrementsAcrossStateTransitions()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var userId = Guid.NewGuid();
        var connection = await SeedConnectionAsync(dbContext, userId);
        await dbContext.SaveChangesAsync();

        var attempt = await service.CreateAttemptAsync(
            userId,
            connection.Id,
            BankingProviders.TrueLayer,
            "sandbox",
            "state-version",
            "exp://127.0.0.1/--/(tabs)/accounts/connect-bank?intent=new",
            DateTime.UtcNow.AddMinutes(15),
            reconnectRequested: false,
            CancellationToken.None);
        var initialVersion = attempt.TransitionVersion;

        await service.MarkCallbackReceivedAsync(attempt, CancellationToken.None);
        await service.MarkAppReturnInitiatedAsync(attempt, CancellationToken.None);
        await service.MarkCompletedAsync(attempt, CancellationToken.None);

        Assert.Equal(initialVersion + 3, attempt.TransitionVersion);
    }

    private static BankConnectionAttemptService CreateService(AppDbContext dbContext)
    {
        return new BankConnectionAttemptService(
            dbContext,
            NullLogger<BankConnectionAttemptService>.Instance);
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
