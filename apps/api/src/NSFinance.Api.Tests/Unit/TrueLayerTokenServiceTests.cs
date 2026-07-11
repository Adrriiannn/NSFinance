using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Tests.Unit;

public class TrueLayerTokenServiceTests
{
    [Fact]
    public async Task ExchangeAuthorizationCode_MapsSuccessResponse()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "access_token":"access-token-1",
                      "refresh_token":"refresh-token-1",
                      "expires_in":1200,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            }));

        var requestCapture = new RequestCapture();
        var service = new TrueLayerTokenService(
            new TrueLayerHttpClient(new HttpClient(new CaptureHttpMessageHandler(handler, requestCapture))),
            NullLogger<TrueLayerTokenService>.Instance);

        var result = await service.ExchangeAuthorizationCodeAsync(
            new TrueLayerResolvedConfiguration(
                "client",
                "secret",
                "https://api.finance.nsireland.ie/api/banking/truelayer/callback",
                "live",
                "https://auth.truelayer.com",
                "https://api.truelayer.com"),
            "code-1",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("access-token-1", result.Value!.AccessToken);
        Assert.Equal("refresh-token-1", result.Value.RefreshToken);
        Assert.Equal("accounts balance transactions offline_access", result.Value.Scope);
        Assert.True(result.Value.AccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(15));
        Assert.NotNull(requestCapture.RequestUri);
        Assert.Equal("https://auth.truelayer.com/connect/token", requestCapture.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", requestCapture.ContentType);
        Assert.Contains("grant_type=authorization_code", requestCapture.Body, StringComparison.Ordinal);
        Assert.Contains("client_id=client", requestCapture.Body, StringComparison.Ordinal);
        Assert.Contains("client_secret=secret", requestCapture.Body, StringComparison.Ordinal);
        Assert.Contains("code=code-1", requestCapture.Body, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=https%3A%2F%2Fapi.finance.nsireland.ie%2Fapi%2Fbanking%2Ftruelayer%2Fcallback", requestCapture.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_MapsInvalidGrantError()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """
                    {
                      "error":"invalid_grant",
                      "error_description":"Authorization code is invalid"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            }));

        var service = new TrueLayerTokenService(
            new TrueLayerHttpClient(new HttpClient(handler)),
            NullLogger<TrueLayerTokenService>.Instance);

        var result = await service.ExchangeAuthorizationCodeAsync(
            new TrueLayerResolvedConfiguration(
                "client",
                "secret",
                "https://api.finance.nsireland.ie/api/banking/truelayer/callback",
                "live",
                "https://auth.truelayer.com",
                "https://api.truelayer.com"),
            "code-1",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("truelayer_authorization_code_invalid", result.Error?.Code);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_LogsNonSuccessStatusAndSafeBody()
    {
        var logger = new ListLogger<TrueLayerTokenService>();
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """
                    {
                      "error":"invalid_request",
                      "error_description":"redirect_uri mismatch for this client",
                      "access_token":"should-not-appear"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            }));

        var service = new TrueLayerTokenService(
            new TrueLayerHttpClient(new HttpClient(handler)),
            logger);

        var result = await service.ExchangeAuthorizationCodeAsync(
            new TrueLayerResolvedConfiguration(
                "client",
                "secret",
                "https://api.finance.nsireland.ie/api/banking/truelayer/callback",
                "live",
                "https://auth.truelayer.com",
                "https://api.truelayer.com"),
            "very-secret-auth-code",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("truelayer_redirect_uri_mismatch", result.Error?.Code);

        var joinedLogs = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("status=400", joinedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redirect_uri mismatch", joinedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("very-secret-auth-code", joinedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-appear", joinedLogs, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", joinedLogs, StringComparison.Ordinal);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class CaptureHttpMessageHandler(
        HttpMessageHandler inner,
        RequestCapture capture)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture.RequestUri = request.RequestUri;
            capture.ContentType = request.Content?.Headers.ContentType?.MediaType;
            capture.Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class RequestCapture
    {
        public Uri? RequestUri { get; set; }
        public string? ContentType { get; set; }
        public string Body { get; set; } = string.Empty;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
