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
