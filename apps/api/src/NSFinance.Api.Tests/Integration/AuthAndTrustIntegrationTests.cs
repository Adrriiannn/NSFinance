using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Google.Apis.Auth;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;
using NSFinance.Api.Modules.Policies.DTOs;
using NSFinance.Api.Modules.Policies.Services;
using NSFinance.Api.Modules.Support.DTOs;
using NSFinance.Api.Modules.Support.Services;
using NSFinance.Api.Modules.Users.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Integration;

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
    public async Task RegistrationFlow_DefaultsProfileBioToEmpty()
    {
        await using var harness = new TestHarness();

        var result = await harness.AuthService.RegisterAsync(
            new RegisterRequest(
                "register.empty-bio@test.local",
                "ValidPassword123",
                "Empty Bio User",
                "UTC",
                "en-US",
                "EUR",
                new DeviceContextDto("device-b", "Phone", "ios", "18", "1.0.0")),
            CancellationToken.None);

        Assert.True(result.Succeeded);

        var user = await harness.DbContext.Users.SingleAsync(x => x.NormalizedEmail == "register.empty-bio@test.local");
        Assert.Null(user.ProfileSubtitle);
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
    public async Task GoogleLogin_ValidToken_CreatesUserProviderAndSession()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-valid-token",
            CreateGooglePayload("sub-google-1", "google.new.user@test.local", emailVerified: true, "Google User"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-valid-token", new DeviceContextDto(null, "Android device", "android", "14", "1.0.0")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);

        var users = await harness.DbContext.Users.AsNoTracking().ToListAsync();
        Assert.Single(users);
        Assert.Equal("google.new.user@test.local", users[0].NormalizedEmail);
        Assert.True(users[0].EmailVerified);

        var providerLinks = await harness.DbContext.UserAuthProviders
            .AsNoTracking()
            .Where(x => x.ProviderType == "google_oidc")
            .ToListAsync();
        Assert.Single(providerLinks);
        Assert.Equal("sub-google-1", providerLinks[0].ProviderSubject);

        var session = await harness.DbContext.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == result.Value!.SessionId);
        Assert.NotNull(session);

        var auditEvents = await harness.DbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.EventName.StartsWith("google_", StringComparison.Ordinal))
            .Select(x => x.EventName)
            .ToListAsync();
        Assert.Contains("google_user_created", auditEvents);
        Assert.Contains("google_provider_link_created", auditEvents);
        Assert.Contains("google_login_success", auditEvents);
    }

    [Fact]
    public async Task GoogleLogin_ExistingLinkedUser_LogsInWithoutDuplicates()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-token-first",
            CreateGooglePayload("sub-google-2", "existing.linked@test.local", emailVerified: true, "Linked User"));

        var first = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-token-first", null),
            CancellationToken.None);
        Assert.True(first.Succeeded);

        harness.ConfigureGoogleToken(
            "google-token-second",
            CreateGooglePayload("sub-google-2", "email.changed@test.local", emailVerified: true, "Linked User"));

        var second = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-token-second", null),
            CancellationToken.None);
        Assert.True(second.Succeeded);

        var users = await harness.DbContext.Users.AsNoTracking().ToListAsync();
        Assert.Single(users);

        var providers = await harness.DbContext.UserAuthProviders
            .AsNoTracking()
            .Where(x => x.ProviderType == "google_oidc")
            .ToListAsync();
        Assert.Single(providers);
    }

    [Fact]
    public async Task GoogleLogin_ExistingLocalUserWithVerifiedEmail_LinksProvider()
    {
        await using var harness = new TestHarness();
        var local = await harness.RegisterAsync("local.link@test.local", "ValidPassword123");

        harness.ConfigureGoogleToken(
            "google-link-token",
            CreateGooglePayload("sub-google-3", "local.link@test.local", emailVerified: true, "Local Linked"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-link-token", null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(local.User.Id, result.Value!.User.Id);

        var provider = await harness.DbContext.UserAuthProviders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == local.User.Id && x.ProviderType == "google_oidc");
        Assert.NotNull(provider);
        Assert.Equal("sub-google-3", provider!.ProviderSubject);
    }

    [Fact]
    public async Task GoogleLogin_InvalidToken_IsRejected()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleTokenFailure("google-invalid-token", new InvalidJwtException("invalid token"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-invalid-token", null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_id_token_invalid", result.Error?.Code);
    }

    [Fact]
    public async Task GoogleLogin_UnverifiedEmail_IsRejected()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-unverified-token",
            CreateGooglePayload("sub-google-4", "unverified@test.local", emailVerified: false, "Unverified User"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-unverified-token", null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_email_not_verified", result.Error?.Code);

        var users = await harness.DbContext.Users.AsNoTracking().ToListAsync();
        Assert.Empty(users);
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
            new UpdateUserProfileRequest(
                "account.flow@test.local",
                "Updated Integration User",
                "Updated User",
                "updated-user",
                null,
                "Focused on: tracking subscriptions",
                "Europe/London",
                "en-GB",
                "GBP",
                "completed",
                true,
                false,
                null,
                null,
                "Ireland",
                ["Track subscriptions"],
                "employed",
                "stable",
                "saving"),
            CancellationToken.None);
        Assert.True(profileUpdate.Succeeded);
        Assert.Equal("Updated User", profileUpdate.Value!.DisplayName);

        var acceptPolicy = await harness.PolicyService.AcceptPolicyAsync(
            new AcceptPolicyRequest("terms_of_service", "1.0.0", "integration_test", "mobile", "1.0.0"),
            CancellationToken.None);
        Assert.True(acceptPolicy.Succeeded);

        var deletionCodeRequest = await harness.AuthService.RequestAccountDeletionCodeAsync(CancellationToken.None);
        Assert.True(deletionCodeRequest.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(deletionCodeRequest.Value?.DebugToken));

        var deleteRequest = await harness.SupportService.CreateDeletionRequestAsync(
            new CreateDeletionRequestRequest(
                deletionCodeRequest.Value!.DebugToken!,
                "integration test deletion"),
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
    public async Task ExportRequest_CreatesReadyPackage_AndSupportsDownload()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("export.flow@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var connectionId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        harness.DbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = register.User.Id,
            ProviderName = "TrueLayer",
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Test Bank",
            Status = "connected",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = "provider-account-1",
            DisplayName = "Transaction Account 1",
            Currency = "GBP",
            RawPayloadJson = "{}",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        harness.DbContext.BankBalanceSnapshots.Add(new BankBalanceSnapshot
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            Current = 50.00m,
            Available = 50.00m,
            Currency = "GBP",
            CapturedUtc = now,
            RawPayloadJson = "{}"
        });
        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = register.User.Id,
            Name = "Transaction Account 1",
            Type = "transaction",
            Currency = "GBP",
            CreatedUtc = now
        });
        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = -9.99m,
            Currency = "GBP",
            Description = "Coffee Shop",
            BookedAtUtc = now,
            CreatedUtc = now
        });

        await harness.DbContext.SaveChangesAsync();

        var result = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("Integration export request"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("ready", result.Value!.Status);

        var stored = await harness.DbContext.ExportRequests.AsNoTracking().SingleAsync(x => x.Id == result.Value.Id);
        Assert.False(string.IsNullOrWhiteSpace(stored.ArtifactReference));
        Assert.True(File.Exists(stored.ArtifactReference));

        var downloadResult = await harness.SupportService.DownloadExportRequestAsync(result.Value.Id, CancellationToken.None);
        Assert.True(downloadResult.Succeeded);
        Assert.NotNull(downloadResult.Value);
        Assert.NotEmpty(downloadResult.Value!.Bytes);
    }

    [Fact]
    public async Task ExportRequest_ReusesSingleRecord_AndRemovesOlderOnes()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("export-single@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var first = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("first export"),
            CancellationToken.None);
        Assert.True(first.Succeeded);

        await Task.Delay(5);

        var second = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("second export"),
            CancellationToken.None);
        Assert.True(second.Succeeded);

        var requestsInDb = await harness.DbContext.ExportRequests
            .AsNoTracking()
            .Where(x => x.UserId == register.User.Id)
            .ToListAsync();

        Assert.Single(requestsInDb);
        Assert.Equal(second.Value!.Id, requestsInDb[0].Id);

        var listResult = await harness.SupportService.GetMyExportRequestsAsync(CancellationToken.None);
        Assert.True(listResult.Succeeded);
        Assert.Single(listResult.Value!);
        Assert.Equal(second.Value!.Id, listResult.Value![0].Id);
    }

    [Fact]
    public async Task ExportRequest_ExpiresAfterRetentionWindow_AndDownloadIsRejected()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("export-expiry@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var created = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("expiry test"),
            CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Value);

        var request = await harness.DbContext.ExportRequests.SingleAsync(x => x.Id == created.Value!.Id);
        Assert.False(string.IsNullOrWhiteSpace(request.ArtifactReference));
        Assert.True(File.Exists(request.ArtifactReference));

        request.RequestedUtc = DateTime.UtcNow.AddMinutes(-16);
        request.UpdatedUtc = request.RequestedUtc;
        await harness.DbContext.SaveChangesAsync();

        var downloadResult = await harness.SupportService.DownloadExportRequestAsync(request.Id, CancellationToken.None);
        Assert.False(downloadResult.Succeeded);
        Assert.Equal("export_expired", downloadResult.Error?.Code);
        Assert.Equal(StatusCodes.Status410Gone, downloadResult.Error?.StatusCode);

        var reloaded = await harness.DbContext.ExportRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        Assert.Equal("expired", reloaded.Status);
        Assert.Null(reloaded.ArtifactReference);
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

    private static GoogleJsonWebSignature.Payload CreateGooglePayload(
        string subject,
        string email,
        bool emailVerified,
        string name)
    {
        return new GoogleJsonWebSignature.Payload
        {
            Subject = subject,
            Email = email,
            EmailVerified = emailVerified,
            Name = name,
            GivenName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            FamilyName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault(),
            Picture = "https://example.com/avatar.png"
        };
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        public AppDbContext DbContext { get; }
        public MutableCurrentUserProvider CurrentUserProvider { get; }
        public TokenSecretService TokenSecretService { get; }
        public SessionService SessionService { get; }
        public StubGoogleIdTokenVerifier GoogleIdTokenVerifier { get; }
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
                Issuer = "NSFinance.Api",
                Audience = "NSFinance.Mobile",
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
            GoogleIdTokenVerifier = new StubGoogleIdTokenVerifier();

            var jwtOptions = Options.Create(_jwtOptions);
            var requestContext = new TestRequestContextAccessor();
            var auditService = new AuditService(DbContext, requestContext, NullLogger<AuditService>.Instance);
            var googleAuthService = new GoogleAuthService(
                Options.Create(new GoogleAuthOptions
                {
                    ClientId = "google-client-id"
                }),
                GoogleIdTokenVerifier,
                NullLogger<GoogleAuthService>.Instance);

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
                googleAuthService,
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
            SupportService = new SupportService(
                DbContext,
                CurrentUserProvider,
                auditService,
                requestContext,
                TokenSecretService,
                NullLogger<SupportService>.Instance);
        }

        public void ConfigureGoogleToken(string idToken, GoogleJsonWebSignature.Payload payload)
        {
            GoogleIdTokenVerifier.ConfigureSuccess(idToken, payload);
        }

        public void ConfigureGoogleTokenFailure(string idToken, Exception exception)
        {
            GoogleIdTokenVerifier.ConfigureFailure(idToken, exception);
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
                ContentMarkdown = $"# {policyType} {version}",
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

    private sealed class StubGoogleIdTokenVerifier : IGoogleIdTokenVerifier
    {
        private readonly Dictionary<string, GoogleJsonWebSignature.Payload> _successPayloads = [];
        private readonly Dictionary<string, Exception> _failures = [];

        public void ConfigureSuccess(string idToken, GoogleJsonWebSignature.Payload payload)
        {
            _failures.Remove(idToken);
            _successPayloads[idToken] = payload;
        }

        public void ConfigureFailure(string idToken, Exception exception)
        {
            _successPayloads.Remove(idToken);
            _failures[idToken] = exception;
        }

        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, string audience, CancellationToken cancellationToken)
        {
            if (_failures.TryGetValue(idToken, out var failure))
            {
                throw failure;
            }

            if (_successPayloads.TryGetValue(idToken, out var payload))
            {
                return Task.FromResult(payload);
            }

            throw new InvalidJwtException("Token not configured in test verifier.");
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
        public string ApplicationName { get; set; } = "NSFinance.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
