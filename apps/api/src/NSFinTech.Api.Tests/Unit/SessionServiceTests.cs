using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinTech.Api.Infrastructure.RequestContext;
using NSFinTech.Api.Modules.Auth.DTOs;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Tests.Unit;

public class SessionServiceTests
{
    [Fact]
    public async Task CreateAndRefresh_RotatesRefreshToken()
    {
        await using var dbContext = CreateDbContext();
        var user = SeedUser(dbContext);
        var service = CreateSessionService(dbContext);

        var issued = await service.CreateSessionAsync(user, new DeviceContextDto("device-a", "Test Device", "ios", "18", "1.0.0"), CancellationToken.None);
        var refreshed = await service.RefreshAsync(issued.RefreshToken, null, CancellationToken.None);

        Assert.True(refreshed.Succeeded);
        Assert.NotNull(refreshed.Value);
        Assert.NotEqual(issued.RefreshToken, refreshed.Value!.RefreshToken);

        var tokens = await dbContext.SessionRefreshTokens.OrderBy(x => x.CreatedUtc).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].UsedUtc);
        Assert.Null(tokens[1].UsedUtc);
    }

    [Fact]
    public async Task Refresh_Fails_WhenSessionRevoked()
    {
        await using var dbContext = CreateDbContext();
        var user = SeedUser(dbContext);
        var service = CreateSessionService(dbContext);

        var issued = await service.CreateSessionAsync(user, null, CancellationToken.None);
        await service.RevokeSessionAsync(user.Id, issued.SessionId, "test_revoke", CancellationToken.None);

        var refreshed = await service.RefreshAsync(issued.RefreshToken, null, CancellationToken.None);
        Assert.False(refreshed.Succeeded);
        Assert.True(new[] { "session_revoked", "refresh_token_reused" }.Contains(refreshed.Error?.Code));
    }

    private static SessionService CreateSessionService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "NSFinTech.Api",
            Audience = "NSFinTech.Mobile",
            SigningKey = "Unit_Test_Jwt_Signing_Key_Change_Me_123456789",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30
        });

        return new SessionService(
            dbContext,
            new JwtTokenService(jwtOptions),
            new TokenSecretService(),
            jwtOptions,
            new TestRequestContextAccessor(),
            NullLogger<SessionService>.Instance);
    }

    private static User SeedUser(AppDbContext dbContext)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PrimaryEmail = "unit@test.local",
            NormalizedEmail = "unit@test.local",
            DisplayName = "Unit User",
            Status = "active",
            OnboardingStatus = "completed",
            Role = "user",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-US",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        };

        dbContext.Users.Add(user);
        dbContext.SaveChanges();
        return user;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"session-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "test-correlation-id";
        public string SourceChannel => "api";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "tests";
        public string? Platform => "ios";
        public string? AppVersion => "1.0.0";
    }
}
