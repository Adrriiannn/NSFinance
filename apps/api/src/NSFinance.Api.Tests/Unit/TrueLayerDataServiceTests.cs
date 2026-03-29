using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Tests.Unit;

public class TrueLayerDataServiceTests
{
    [Fact]
    public async Task GetTransactionsAsync_IncludesFromAndToQueryWhenWindowProvided()
    {
        var capture = new RequestCapture();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capture.RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "results": [] }""", Encoding.UTF8, "application/json")
            });
        });

        var service = new TrueLayerDataService(
            new TrueLayerHttpClient(new HttpClient(handler)),
            NullLogger<TrueLayerDataService>.Instance);

        var fromUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await service.GetTransactionsAsync(
            new TrueLayerResolvedConfiguration(
                "client",
                "secret",
                "http://localhost:5080/api/banking/truelayer/callback",
                "sandbox",
                "https://auth.truelayer-sandbox.com",
                "https://api.truelayer-sandbox.com"),
            "access-token",
            "acc-001",
            fromUtc,
            toUtc,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(capture.RequestUri);
        Assert.Contains("from=2020-01-01T00%3A00%3A00Z", capture.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-01-01T00%3A00%3A00Z", capture.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTransactionsAsync_PrefersMerchantNameForDisplayDescription()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "transaction_id": "tx-1",
                          "amount": -14.99,
                          "currency": "EUR",
                          "timestamp": "2026-03-28T11:45:00Z",
                          "description": "Microsoft#g1491234",
                          "merchant_name": "Microsoft"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            }));

        var service = new TrueLayerDataService(
            new TrueLayerHttpClient(new HttpClient(handler)),
            NullLogger<TrueLayerDataService>.Instance);

        var result = await service.GetTransactionsAsync(
            new TrueLayerResolvedConfiguration(
                "client",
                "secret",
                "http://localhost:5080/api/banking/truelayer/callback",
                "sandbox",
                "https://auth.truelayer-sandbox.com",
                "https://api.truelayer-sandbox.com"),
            "access-token",
            "acc-001",
            null,
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!);
        Assert.Equal("Microsoft", result.Value![0].Description);
    }

    [Fact]
    public async Task GetTransactionsAsync_TrimsHashedSuffixFromDescriptionWhenMerchantNameMissing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "transaction_id": "tx-2",
                          "amount": -8.99,
                          "currency": "EUR",
                          "timestamp": "2026-03-28T09:00:00Z",
                          "description": "YouTube#A12345"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            }));

        var service = new TrueLayerDataService(
            new TrueLayerHttpClient(new HttpClient(handler)),
            NullLogger<TrueLayerDataService>.Instance);

        var result = await service.GetTransactionsAsync(
            new TrueLayerResolvedConfiguration(
                "client",
                "secret",
                "http://localhost:5080/api/banking/truelayer/callback",
                "sandbox",
                "https://auth.truelayer-sandbox.com",
                "https://api.truelayer-sandbox.com"),
            "access-token",
            "acc-001",
            null,
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!);
        Assert.Equal("YouTube", result.Value![0].Description);
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

    private sealed class RequestCapture
    {
        public Uri? RequestUri { get; set; }
    }
}
