using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Infrastructure.RequestContext;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Auth.DTOs;
using NSFinTech.Api.Modules.Auth.Services;
using NSFinTech.Api.Modules.Auth.Validators;
using NSFinTech.Api.Modules.Policies.DTOs;
using NSFinTech.Api.Modules.Policies.Services;
using NSFinTech.Api.Modules.Support.DTOs;
using NSFinTech.Api.Modules.Support.Services;
using NSFinTech.Api.Modules.Users.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Tests.Integration;

public class AuthAndTrustIntegrationTests
{
    [Fact]
    public async Task RegistrationFlow_CreatesUserAndSession()
    {
        await using var harness = new TestHarness();

        var result = await harness.AuthService.RegisterAsync(
            new RegisterRequest(
                "register.flow@test.local",
                "ValidPassword123",
                "Integration User",
                "UTC",
                "en-US",
                "EUR",
                new DeviceContextDto("device-a", "Phone", "ios", "18", "1.0.0")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.SessionId);
        Assert.Single(await harness.DbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task LoginFlow_HandlesValidAndInvalidCredentials()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("login.flow@test.local", "ValidPassword123");

        var validLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("login.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.True(validLogin.Succeeded);

        var invalidLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("login.flow@test.local", "WrongPassword123", null),
            CancellationToken.None);
        Assert.False(invalidLogin.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, invalidLogin.Error?.StatusCode);
    }

    [Fact]
    public async Task PasswordResetFlow_Works_EndToEnd()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("reset.flow@test.local", "ValidPassword123");

        var requestReset = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("reset.flow@test.local"),
            CancellationToken.None);
        Assert.True(requestReset.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(requestReset.Value?.DebugToken));

        var reset = await harness.AuthService.ResetPasswordAsync(
            new ResetPasswordRequest(requestReset.Value!.DebugToken!, "NewValidPassword123"),
            CancellationToken.None);
        Assert.True(reset.Succeeded);

        var oldPasswordLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("reset.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.False(oldPasswordLogin.Succeeded);

        var newPasswordLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("reset.flow@test.local", "NewValidPassword123", null),
            CancellationToken.None);
        Assert.True(newPasswordLogin.Succeeded);
    }

    [Fact]
    public async Task LogoutCurrentAndAllSessions_RevokeSessions()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("logout.flow@test.local", "ValidPassword123");
        var secondLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("logout.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.True(secondLogin.Succeeded);

        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);
        var logoutCurrent = await harness.AuthService.LogoutCurrentSessionAsync(CancellationToken.None);
        Assert.True(logoutCurrent.Succeeded);

        harness.CurrentUserProvider.Set(register.User.Id, secondLogin.Value!.SessionId);
        var logoutAll = await harness.AuthService.LogoutAllAsync(CancellationToken.None);
        Assert.True(logoutAll.Succeeded);

        var sessions = await harness.DbContext.Sessions
            .Where(x => x.UserId == register.User.Id)
            .ToListAsync();

        Assert.Equal(2, sessions.Count);
        Assert.NotNull(sessions.Single(x => x.Id == register.SessionId).RevokedUtc);
        Assert.Null(sessions.Single(x => x.Id == secondLogin.Value!.SessionId).RevokedUtc);
    }

    [Fact]
    public async Task ProfileUpdate_PolicyAcceptance_AndDeletionRequest_AreRecorded()
    {
        await using var harness = new TestHarness();
        harness.SeedPolicy("terms_of_service", "1.0.0");
        var register = await harness.RegisterAsync("account.flow@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var profileUpdate = await harness.UserService.UpdateProfileAsync(
            new UpdateUserProfileRequest("Updated User", "Europe/London", "en-GB", "GBP", "completed", true),
            CancellationToken.None);
        Assert.True(profileUpdate.Succeeded);
        Assert.Equal("Updated User", profileUpdate.Value!.DisplayName);

        var acceptPolicy = await harness.PolicyService.AcceptPolicyAsync(
            new AcceptPolicyRequest("terms_of_service", "1.0.0", "integration_test", "mobile", "1.0.0"),
            CancellationToken.None);
        Assert.True(acceptPolicy.Succeeded);

        var deleteRequest = await harness.SupportService.CreateDeletionRequestAsync(
            new CreateDeletionRequestRequest("integration test deletion"),
            CancellationToken.None);
        Assert.True(deleteRequest.Succeeded);

        Assert.Single(await harness.DbContext.PolicyAcceptances.ToListAsync());
        Assert.Single(await harness.DbContext.DeletionRequests.ToListAsync());
    }

    [Fact]
    public async Task SecurityNegativeCases_AreHandled()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("negative.flow@test.local", "ValidPassword123");

        var requestReset = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("negative.flow@test.local"),
            CancellationToken.None);
        Assert.True(requestReset.Succeeded);

        var tokenHash = harness.TokenSecretService.HashToken(requestReset.Value!.DebugToken!);
        var tokenEntity = await harness.DbContext.EmailActionTokens.SingleAsync(x => x.TokenHash == tokenHash);
        tokenEntity.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        await harness.DbContext.SaveChangesAsync();

        var expiredReset = await harness.AuthService.ResetPasswordAsync(
            new ResetPasswordRequest(requestReset.Value.DebugToken!, "AnotherValidPassword123"),
            CancellationToken.None);
        Assert.False(expiredReset.Succeeded);
        Assert.Equal("reset_token_invalid", expiredReset.Error?.Code);

        var secondResetRequest = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("negative.flow@test.local"),
            CancellationToken.None);
        var secondToken = secondResetRequest.Value!.DebugToken!;

        var firstUse = await harness.AuthService.ResetPasswordAsync(
            new ResetPasswordRequest(secondToken, "AnotherValidPassword123"),
            CancellationToken.None);
        Assert.True(firstUse.Succeeded);

        var reused = await harness.AuthService.ResetPasswordAsync(
            new ResetPasswordRequest(secondToken, "ThirdValidPassword123"),
            CancellationToken.None);
        Assert.False(reused.Succeeded);
        Assert.Equal("reset_token_reused", reused.Error?.Code);

        var revokedRefresh = await harness.SessionService.RefreshAsync(register.RefreshToken, null, CancellationToken.None);
        Assert.False(revokedRefresh.Succeeded);

        harness.CurrentUserProvider.Clear();
        var unauthorizedProfile = await harness.UserService.GetProfileAsync(CancellationToken.None);
        Assert.False(unauthorizedProfile.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedProfile.Error?.StatusCode);

        var malformedPayloadErrors = LoginRequestValidator.Validate(new LoginRequest("", "", null));
        Assert.NotEmpty(malformedPayloadErrors);
    }

    [Fact]
    public async Task AbuseProtection_TriggersLockoutAfterRepeatedFailures()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("abuse.flow@test.local", "ValidPassword123");

        for (var i = 0; i < 6; i++)
        {
            await harness.AuthService.LoginAsync(
                new LoginRequest("abuse.flow@test.local", "WrongPassword123", null),
                CancellationToken.None);
        }

        var lockedResult = await harness.AuthService.LoginAsync(
            new LoginRequest("abuse.flow@test.local", "WrongPassword123", null),
            CancellationToken.None);

        Assert.False(lockedResult.Succeeded);
        Assert.Equal(StatusCodes.Status429TooManyRequests, lockedResult.Error?.StatusCode);
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        public AppDbContext DbContext { get; }
        public MutableCurrentUserProvider CurrentUserProvider { get; }
        public TokenSecretService TokenSecretService { get; }
        public SessionService SessionService { get; }
        public AuthService AuthService { get; }
        public UserService UserService { get; }
        public PolicyService PolicyService { get; }
        public SupportService SupportService { get; }

        private readonly JwtOptions _jwtOptions;

        public TestHarness()
        {
            DbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"integration-tests-{Guid.NewGuid():N}")
                .Options);

            _jwtOptions = new JwtOptions
            {
                Issuer = "NSFinTech.Api",
                Audience = "NSFinTech.Mobile",
                SigningKey = "Integration_Testing_Jwt_Signing_Key_Change_Me_123456789",
                AccessTokenMinutes = 15,
                RefreshTokenDays = 30,
                PasswordResetTokenMinutes = 30,
                EmailVerificationTokenMinutes = 60,
                MaxFailedLoginAttempts = 6,
                FailedLoginWindowMinutes = 15,
                LoginLockoutMinutes = 10
            };

            CurrentUserProvider = new MutableCurrentUserProvider();
            TokenSecretService = new TokenSecretService();

            var jwtOptions = Options.Create(_jwtOptions);
            var requestContext = new TestRequestContextAccessor();
            var auditService = new AuditService(DbContext, requestContext, NullLogger<AuditService>.Instance);

            SessionService = new SessionService(
                DbContext,
                new JwtTokenService(jwtOptions),
                TokenSecretService,
                jwtOptions,
                requestContext,
                NullLogger<SessionService>.Instance);

            AuthService = new AuthService(
                DbContext,
                new Pbkdf2PasswordHasher(),
                SessionService,
                TokenSecretService,
                CurrentUserProvider,
                new AuthAbuseService(DbContext, jwtOptions),
                auditService,
                requestContext,
                new FakeHostEnvironment(),
                jwtOptions,
                NullLogger<AuthService>.Instance);

            UserService = new UserService(DbContext, CurrentUserProvider, auditService);
            PolicyService = new PolicyService(DbContext, CurrentUserProvider, auditService);
            SupportService = new SupportService(DbContext, CurrentUserProvider, auditService);
        }

        public async Task<(AuthTokenResponse Value, UserProfileDto User, Guid SessionId, string RefreshToken)> RegisterAsync(string email, string password)
        {
            var result = await AuthService.RegisterAsync(
                new RegisterRequest(email, password, "Integration User", "UTC", "en-US", "EUR", null),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);

            return (result.Value!, result.Value!.User, result.Value.SessionId, result.Value.RefreshToken);
        }

        public void SeedPolicy(string policyType, string version)
        {
            var document = new PolicyDocument
            {
                Id = Guid.NewGuid(),
                PolicyType = policyType,
                Name = policyType,
                CreatedUtc = DateTime.UtcNow
            };
            DbContext.PolicyDocuments.Add(document);
            DbContext.PolicyVersions.Add(new PolicyVersion
            {
                Id = Guid.NewGuid(),
                PolicyDocumentId = document.Id,
                Version = version,
                EffectiveUtc = DateTime.UtcNow,
                ContentReference = $"legal/{policyType}/{version}",
                IsActive = true,
                CreatedUtc = DateTime.UtcNow
            });
            DbContext.SaveChanges();
        }

        public ValueTask DisposeAsync()
        {
            return DbContext.DisposeAsync();
        }
    }

    private sealed class MutableCurrentUserProvider : ICurrentUserProvider
    {
        private Guid? _userId;
        private Guid? _sessionId;

        public Guid UserId => _userId ?? throw new InvalidOperationException("No active user.");

        public void Set(Guid userId, Guid sessionId)
        {
            _userId = userId;
            _sessionId = sessionId;
        }

        public void Clear()
        {
            _userId = null;
            _sessionId = null;
        }

        public bool TryGetUserId(out Guid userId)
        {
            userId = _userId ?? Guid.Empty;
            return _userId.HasValue;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = _sessionId ?? Guid.Empty;
            return _sessionId.HasValue;
        }
    }

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "integration-correlation-id";
        public string SourceChannel => "api";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "integration-tests";
        public string? Platform => "ios";
        public string? AppVersion => "1.0.0";
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "NSFinTech.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
