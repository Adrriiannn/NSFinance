using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Integration;

public class OpenBankingIntegrationTests
{
    [Fact]
    public async Task CallbackFlow_SuccessfullyIngestsAccountsBalancesAndTransactions()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.success@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.Equal(BankConnectionStatuses.Synced, connection.Status);
        Assert.NotNull(connection.Token);
        Assert.False(string.IsNullOrWhiteSpace(connection.Token!.EncryptedRefreshToken));
        Assert.NotEqual("refresh-token-1", connection.Token!.EncryptedRefreshToken);

        Assert.Single(await harness.DbContext.LinkedBankAccounts.ToListAsync());
        Assert.Single(await harness.DbContext.BankBalanceSnapshots.ToListAsync());
        Assert.Equal(2, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Single(await harness.DbContext.FinancialAccounts.ToListAsync());
        Assert.Equal(2, await harness.DbContext.Transactions.CountAsync());
    }

    [Fact]
    public async Task CallbackFlow_InvalidAuthorizationCode_MarksReauthRequired()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: InvalidCodeHandler());

        var user = await harness.CreateUserAsync("bank.invalid-code@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("reused-code", state, null, null),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("truelayer_authorization_code_invalid", outcome.Code);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.Equal(BankConnectionStatuses.ReauthRequired, connection.Status);
    }


    [Fact]
    public async Task CallbackFlow_PreservesCustomAppReturnUri_ForEnvironmentAwareReturn()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.return-uri@test.local");
        const string appReturnUri = "exp://192.168.0.11:8081/--/modals/add-account";

        var start = await harness.AuthService.StartLinkAsync(user.Id, appReturnUri, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.AppReturnUri);
        Assert.StartsWith("exp://192.168.0.11:8081/--/modals/add-account", outcome.AppReturnUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallbackFlow_PreservesCurrentAppReturnUri_ForEnvironmentAwareReturn()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.return-uri-current@test.local");
        const string appReturnUri = "exp://192.168.0.11:8081/--/(tabs)/accounts/connect-bank?intent=new";

        var start = await harness.AuthService.StartLinkAsync(user.Id, appReturnUri, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.AppReturnUri);
        Assert.StartsWith(
            "exp://192.168.0.11:8081/--/(tabs)/accounts/connect-bank",
            outcome.AppReturnUri,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartLink_InvalidConfiguration_ReturnsActionableError()
    {
        var options = ValidSandboxOptions();
        options.ClientSecret = string.Empty;

        await using var harness = new OpenBankingTestHarness(
            options: options,
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.invalid-config@test.local");
        var result = await harness.AuthService.StartLinkAsync(user.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("truelayer_not_configured", result.Error?.Code);
    }

    [Fact]
    public async Task CallbackFlow_EnvironmentMismatch_IsRejectedAndLoggedAsFailed()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.env-mismatch@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var liveOptions = ValidSandboxOptions();
        liveOptions.Environment = "live";
        liveOptions.AuthBaseUrl = "https://auth.truelayer.com";
        liveOptions.ApiBaseUrl = "https://api.truelayer.com";
        liveOptions.RedirectUri = "https://api.finance.nsireland.ie/api/banking/truelayer/callback";

        var liveAuthService = harness.BuildAuthService(
            liveOptions);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await liveAuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("truelayer_environment_mismatch", outcome.Code);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.Equal(BankConnectionStatuses.Failed, connection.Status);
        Assert.Equal("truelayer_environment_mismatch", connection.LastErrorCode);
    }


    [Fact]
    public async Task ListUserVisibleConnectionsAsync_ShowsOnlyActiveAndAttentionConnectionsWithoutHistoryNoise()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.visible@test.local");
        var now = DateTime.UtcNow;

        harness.DbContext.OpenBankingConnections.AddRange(
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Mock Bank Plc",
                ProviderConnectionReference = "provider-connection-1",
                Status = BankConnectionStatuses.Failed,
                CreatedUtc = now.AddMinutes(-40),
                UpdatedUtc = now.AddMinutes(-40)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Mock Bank Plc",
                ProviderConnectionReference = "provider-connection-1",
                Status = BankConnectionStatuses.Synced,
                CreatedUtc = now.AddMinutes(-20),
                UpdatedUtc = now.AddMinutes(-5)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Needs Attention Bank",
                ProviderConnectionReference = "provider-connection-2",
                Status = BankConnectionStatuses.ReauthRequired,
                CreatedUtc = now.AddMinutes(-10),
                UpdatedUtc = now.AddMinutes(-4)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Pending Bank",
                ProviderConnectionReference = "provider-connection-3",
                Status = BankConnectionStatuses.ConsentInProgress,
                CreatedUtc = now.AddMinutes(-6),
                UpdatedUtc = now.AddMinutes(-6)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Already Active Bank",
                ProviderConnectionReference = "provider-connection-4",
                Status = BankConnectionStatuses.Expired,
                CreatedUtc = now.AddMinutes(-30),
                UpdatedUtc = now.AddMinutes(-30)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Already Active Bank",
                ProviderConnectionReference = "provider-connection-4",
                Status = BankConnectionStatuses.ConnectedPendingSync,
                CreatedUtc = now.AddMinutes(-8),
                UpdatedUtc = now.AddMinutes(-2)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Revoked Bank",
                ProviderConnectionReference = "provider-connection-5",
                Status = BankConnectionStatuses.Revoked,
                CreatedUtc = now.AddMinutes(-50),
                UpdatedUtc = now.AddMinutes(-50)
            });

        await harness.DbContext.SaveChangesAsync();

        var overview = await harness.CreateConnectionService()
            .ListUserVisibleConnectionsAsync(user.Id, CancellationToken.None);

        Assert.Equal(2, overview.ActiveConnections.Count);
        Assert.Contains(overview.ActiveConnections, x => x.ProviderDisplayName == "Mock Bank Plc" && x.Status == BankConnectionStatuses.Synced);
        Assert.Contains(overview.ActiveConnections, x => x.ProviderDisplayName == "Already Active Bank" && x.Status == BankConnectionStatuses.ConnectedPendingSync);

        Assert.Single(overview.AttentionConnections);
        Assert.Equal("Needs Attention Bank", overview.AttentionConnections[0].ProviderDisplayName);
        Assert.Equal(BankConnectionStatuses.ReauthRequired, overview.AttentionConnections[0].Status);

        Assert.DoesNotContain(overview.ActiveConnections, x => x.Status == BankConnectionStatuses.Failed || x.Status == BankConnectionStatuses.ConsentInProgress || x.Status == BankConnectionStatuses.Revoked);
        Assert.DoesNotContain(overview.AttentionConnections, x => x.ProviderDisplayName == "Already Active Bank");
    }

    [Fact]
    public async Task DisconnectAsync_RemovesImportedDataAndHidesConnectionFromUserVisibleList()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.disconnect@test.local");
        var now = DateTime.UtcNow;
        var connectionId = Guid.NewGuid();
        var financialAccountId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();

        harness.DbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = user.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Disconnect Bank",
            ProviderConnectionReference = "provider-disconnect-1",
            Status = BankConnectionStatuses.Synced,
            CreatedUtc = now.AddMinutes(-15),
            UpdatedUtc = now.AddMinutes(-1),
            Token = new BankConnectionToken
            {
                Id = Guid.NewGuid(),
                ConnectionId = connectionId,
                EncryptedRefreshToken = "ciphertext",
                AccessTokenExpiresUtc = now.AddHours(1),
                TokenObtainedUtc = now.AddMinutes(-5),
                IsRevoked = false
            }
        });

        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = financialAccountId,
            UserId = user.Id,
            Name = "Disconnect Projection",
            Type = "Current",
            Currency = "GBP",
            CreatedUtc = now.AddMinutes(-15)
        });

        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = "provider-account-1",
            DisplayName = "Disconnect Account",
            Currency = "GBP",
            CurrentConnectionHealth = "healthy",
            RawPayloadJson = "{}",
            FinancialAccountId = financialAccountId,
            CreatedUtc = now.AddMinutes(-15),
            UpdatedUtc = now.AddMinutes(-1)
        });

        harness.DbContext.BankBalanceSnapshots.Add(new BankBalanceSnapshot
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            Available = 10,
            Current = 10,
            Overdraft = 0,
            Currency = "GBP",
            CapturedUtc = now.AddMinutes(-1)
        });

        harness.DbContext.RawBankTransactions.Add(new RawBankTransaction
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            ProviderTransactionId = "raw-1",
            DedupeKey = "norm-1",
            Amount = -12.34m,
            Currency = "GBP",
            BookedAtUtc = now.AddDays(-1),
            Description = "Coffee",
            TransactionType = "DEBIT",
            TransactionStatus = "booked",
            RawPayloadJson = "{}",
            ImportedUtc = now.AddMinutes(-1)
        });

        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = financialAccountId,
            Amount = -12.34m,
            Currency = "GBP",
            Description = "Coffee",
            BookedAtUtc = now.AddDays(-1),
            CreatedUtc = now.AddMinutes(-1)
        });

        await harness.DbContext.SaveChangesAsync();

        var service = harness.CreateConnectionService();
        var result = await service.DisconnectAsync(user.Id, connectionId, CancellationToken.None);

        Assert.True(result.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleAsync(x => x.Id == connectionId);
        Assert.Equal(BankConnectionStatuses.Revoked, connection.Status);
        Assert.NotNull(connection.Token);
        Assert.True(connection.Token!.IsRevoked);
        Assert.Null(connection.Token.EncryptedRefreshToken);

        Assert.Empty(await harness.DbContext.LinkedBankAccounts.ToListAsync());
        Assert.Empty(await harness.DbContext.BankBalanceSnapshots.ToListAsync());
        Assert.Empty(await harness.DbContext.RawBankTransactions.ToListAsync());
        Assert.Empty(await harness.DbContext.FinancialAccounts.ToListAsync());
        Assert.Empty(await harness.DbContext.Transactions.ToListAsync());

        var overview = await service.ListUserVisibleConnectionsAsync(user.Id, CancellationToken.None);
        Assert.Empty(overview.ActiveConnections);
        Assert.Empty(overview.AttentionConnections);
    }

    private static TrueLayerOptions ValidSandboxOptions() => new()
    {
        ClientId = "sandbox-client",
        ClientSecret = "sandbox-secret",
        RedirectUri = "http://localhost:5080/api/banking/truelayer/callback",
        Environment = "sandbox",
        AuthBaseUrl = "https://auth.truelayer-sandbox.com",
        ApiBaseUrl = "https://api.truelayer-sandbox.com"
    };

    private static HttpMessageHandler SuccessfulFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-1",
                      "refresh_token":"refresh-token-1",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-001",
                          "display_name": "Sandbox Main Account",
                          "currency": "GBP",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "mock-bank",
                            "display_name": "Mock Bank Plc"
                          },
                          "account_number": {
                            "sort_code": "010101",
                            "account_number": "12345678"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 2400.50,
                          "current": 2450.50,
                          "overdraft": -100.00,
                          "currency": "GBP",
                          "update_timestamp": "2026-03-09T02:10:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-001",
                          "normalised_provider_transaction_id":"norm-001",
                          "amount":-25.20,
                          "currency":"GBP",
                          "timestamp":"2026-03-08T12:00:00Z",
                          "description":"Coffee Shop",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        },
                        {
                          "transaction_id":"tx-002",
                          "normalised_provider_transaction_id":"norm-002",
                          "amount":1500.00,
                          "currency":"GBP",
                          "timestamp":"2026-03-07T09:30:00Z",
                          "description":"Salary",
                          "transaction_type":"CREDIT",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler InvalidCodeHandler()
    {
        return new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(
                    HttpStatusCode.BadRequest,
                    """
                    {
                      "error":"invalid_grant",
                      "error_description":"Authorization code is expired or already used"
                    }
                    """));
            }

            return Task.FromResult(Json(HttpStatusCode.NotFound, """{ "error": "not_found" }"""));
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string GetQueryValue(string url, string key)
    {
        var uri = new Uri(url);
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);

        return query[key];
    }

    private sealed class OpenBankingTestHarness : IAsyncDisposable
    {
        private readonly IRequestContextAccessor _requestContext = new TestRequestContextAccessor();
        private readonly IAuditService _auditService;
        private readonly HttpMessageHandler _httpHandler;

        public AppDbContext DbContext { get; }
        public TrueLayerAuthService AuthService { get; private set; }

        public OpenBankingTestHarness(TrueLayerOptions options, HttpMessageHandler httpHandler)
        {
            _httpHandler = httpHandler;
            DbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"open-banking-tests-{Guid.NewGuid():N}")
                .Options);

            _auditService = new AuditService(DbContext, _requestContext, NullLogger<AuditService>.Instance);
            AuthService = BuildAuthService(options);
        }

        public TrueLayerAuthService BuildAuthService(TrueLayerOptions options)
        {
            var configurationService = new TrueLayerConfigurationService(Options.Create(options));
            var httpClient = new TrueLayerHttpClient(new HttpClient(_httpHandler));
            var tokenService = new TrueLayerTokenService(httpClient, NullLogger<TrueLayerTokenService>.Instance);
            var dataService = new TrueLayerDataService(httpClient, NullLogger<TrueLayerDataService>.Instance);
            var connectionService = CreateConnectionService();
            var syncService = new BankSyncService(
                DbContext,
                connectionService,
                configurationService,
                tokenService,
                dataService,
                new TestSecretProtector(),
                _auditService,
                NullLogger<BankSyncService>.Instance);

            return new TrueLayerAuthService(
                configurationService,
                connectionService,
                tokenService,
                syncService,
                new TestTrueLayerSyncQueue(syncService),
                _auditService,
                NullLogger<TrueLayerAuthService>.Instance);
        }

        public BankConnectionService CreateConnectionService()
        {
            return new BankConnectionService(DbContext, _auditService, NullLogger<BankConnectionService>.Instance);
        }

        public async Task<User> CreateUserAsync(string email)
        {
            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                PrimaryEmail = email,
                NormalizedEmail = email.ToLowerInvariant(),
                DisplayName = "Banking Test User",
                Status = "active",
                OnboardingStatus = "profile_created",
                Role = "user",
                CreatedUtc = now,
                UpdatedUtc = now,
                LastLoginUtc = now,
                EmailVerified = true,
                IsDisabled = false,
                IsSuspended = false,
                DeletionRequested = false,
                Timezone = "UTC",
                Locale = "en-GB",
                PreferredCurrency = "GBP",
                PlanTier = "standard",
                BiometricUnlockEnabled = false
            };

            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "banking-tests-correlation";
        public string SourceChannel => "api";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "test-agent";
        public string? Platform => "ios";
        public string? AppVersion => "1.0.0";
    }

    private sealed class TestTrueLayerSyncQueue(BankSyncService syncService) : ITrueLayerSyncQueue
    {
        public async ValueTask QueueInitialSyncAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default)
        {
            await syncService.SyncConnectionAsync(userId, connectionId, cancellationToken);
        }
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        }

        public string Unprotect(string ciphertext)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
        }
    }
}

