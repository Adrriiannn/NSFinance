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

    [Fact]
    public async Task GetTransactionsAsync_NormalizesSettledEndpointStatusToBooked_WhenProviderStatusLooksPending()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "transaction_id": "tx-3",
                          "normalised_provider_transaction_id": "norm-3",
                          "amount": -20.00,
                          "currency": "EUR",
                          "timestamp": "2026-03-30T15:04:00Z",
                          "description": "Lunch charge",
                          "status": "pending"
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
        var transaction = Assert.Single(result.Value!);
        Assert.Equal("settled", transaction.SourceEndpoint);
        Assert.Equal("pending", transaction.ProviderStatus);
        Assert.Equal("booked", transaction.TransactionStatus);
        Assert.Contains("settled_endpoint_overrides_provider_status", transaction.StatusNormalizationReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPendingTransactionsAsync_NormalizesToPending_WhenProviderStatusIsMissing()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/transactions/pending", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "results": [
                            {
                              "transaction_id": "tx-4",
                              "normalised_provider_transaction_id": "norm-4",
                              "amount": -8.00,
                              "currency": "EUR",
                              "timestamp": "2026-03-30T18:30:00Z",
                              "description": "Card hold"
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var service = new TrueLayerDataService(
            new TrueLayerHttpClient(new HttpClient(handler)),
            NullLogger<TrueLayerDataService>.Instance);

        var result = await service.GetPendingTransactionsAsync(
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
        var transaction = Assert.Single(result.Value!);
        Assert.Equal("pending", transaction.SourceEndpoint);
        Assert.Null(transaction.ProviderStatus);
        Assert.Equal("pending", transaction.TransactionStatus);
        Assert.Equal("pending_endpoint_default", transaction.StatusNormalizationReason);
    }

    [Fact]
    public async Task GetTransactionsAsync_CapturesDateOnlyTimestampProvenance()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "transaction_id": "tx-date-only",
                          "amount": 10.00,
                          "currency": "EUR",
                          "timestamp": "2026-04-01",
                          "description": "Date only feed"
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
        var transaction = Assert.Single(result.Value!);
        Assert.Equal("timestamp", transaction.TimestampSource);
        Assert.Equal("date_only_midnight", transaction.TimestampPrecision);
        Assert.Equal("2026-04-01", transaction.ProviderTimestampRaw);
    }

    [Fact]
    public async Task GetTransactionsAsync_CapturesPreciseTimestampAndValueSource()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "transaction_id": "tx-precise",
                          "normalised_provider_transaction_id": "norm-precise",
                          "amount": -12.34,
                          "currency": "EUR",
                          "booked_timestamp": "2026-04-01T09:07:11+00:00",
                          "value_date": "2026-04-01",
                          "description": "Precise feed"
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
        var transaction = Assert.Single(result.Value!);
        Assert.Equal("booked_timestamp", transaction.TimestampSource);
        Assert.Equal("precise_datetime", transaction.TimestampPrecision);
        Assert.Equal("2026-04-01T09:07:11+00:00", transaction.ProviderTimestampRaw);
        Assert.Equal("2026-04-01", transaction.ValueTimestampRaw);
        Assert.NotNull(transaction.ValueAtUtc);
    }

    [Fact]
    public async Task GetTransactionsAsync_PrefersPreciseBookedField_WhenPrimaryTimestampIsDateOnly()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "transaction_id": "tx-aib-like-precise",
                          "normalised_provider_transaction_id": "norm-aib-like-precise",
                          "amount": 1.00,
                          "currency": "EUR",
                          "timestamp": "2026-04-01",
                          "booked_timestamp": "2026-04-01T09:07:00+01:00",
                          "value_date": "2026-04-01",
                          "description": "AIB transfer with precise fallback field"
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
        var transaction = Assert.Single(result.Value!);
        Assert.Equal("booked_timestamp", transaction.TimestampSource);
        Assert.Equal("precise_datetime", transaction.TimestampPrecision);
        Assert.Equal("2026-04-01T09:07:00+01:00", transaction.ProviderTimestampRaw);
        Assert.Equal(new DateTime(2026, 4, 1, 8, 7, 0, DateTimeKind.Utc), transaction.BookedAtUtc);
    }

    [Fact]
    public async Task GetTransactionsAsync_UsesTransactionTimestampField_WhenPresent()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "transaction_id": "tx-transaction-timestamp",
                          "amount": -4.25,
                          "currency": "EUR",
                          "booking_date": "2026-04-01",
                          "transaction_timestamp": "2026-04-01T21:30:15Z",
                          "description": "Provider-specific timestamp field"
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
        var transaction = Assert.Single(result.Value!);
        Assert.Equal("transaction_timestamp", transaction.TimestampSource);
        Assert.Equal("precise_datetime", transaction.TimestampPrecision);
        Assert.Equal("2026-04-01T21:30:15Z", transaction.ProviderTimestampRaw);
        Assert.Equal(new DateTime(2026, 4, 1, 21, 30, 15, DateTimeKind.Utc), transaction.BookedAtUtc);
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
