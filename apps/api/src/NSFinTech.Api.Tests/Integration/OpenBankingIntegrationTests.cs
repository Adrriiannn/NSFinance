using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinTech.Api.Infrastructure.RequestContext;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Banking.DTOs;
using NSFinTech.Api.Modules.Banking.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Tests.Integration;

public class OpenBankingIntegrationTests
{
    [Fact]
    public async Task CallbackFlow_SuccessfullyIngestsAccountsBalancesAndTransactions()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.success@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, CancellationToken.None);
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
        var start = await harness.AuthService.StartLinkAsync(user.Id, CancellationToken.None);
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
    public async Task StartLink_InvalidConfiguration_ReturnsActionableError()
    {
        var options = ValidSandboxOptions();
        options.ClientSecret = string.Empty;

        await using var harness = new OpenBankingTestHarness(
            options: options,
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.invalid-config@test.local");
        var result = await harness.AuthService.StartLinkAsync(user.Id, CancellationToken.None);

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
        var start = await harness.AuthService.StartLinkAsync(user.Id, CancellationToken.None);
        Assert.True(start.Succeeded);

        var liveOptions = ValidSandboxOptions();
        liveOptions.Environment = "live";
        liveOptions.AuthBaseUrl = "https://auth.truelayer.com";
        liveOptions.ApiBaseUrl = "https://api.truelayer.com";

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

    private static TrueLayerOptions ValidSandboxOptions() => new()
    {
        ClientId = "sandbox-client",
        ClientSecret = "sandbox-secret",
        RedirectUri = "https://api.nsfintech.local/api/banking/truelayer/callback",
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
            var connectionService = new BankConnectionService(DbContext, _auditService);
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
                _auditService,
                NullLogger<TrueLayerAuthService>.Instance);
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
