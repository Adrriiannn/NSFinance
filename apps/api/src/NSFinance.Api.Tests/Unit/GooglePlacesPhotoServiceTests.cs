using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Endpoints;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class GooglePlacesPhotoServiceTests
{
    [Fact]
    public void BuildAppPhotoUrl_NullName_ReturnsNull()
    {
        using var httpClient = CreateHttpClient(new StubHttpMessageHandler());
        var sut = CreateSut(httpClient);

        Assert.Null(sut.BuildAppPhotoUrl(null));
        Assert.Null(sut.BuildAppPhotoUrl(""));
    }

    [Theory]
    [InlineData("https://example.com/image.jpg")]
    [InlineData("places/abc")]
    [InlineData("places/abc/notphotos/def")]
    [InlineData("places/../photos/def")]
    public void BuildAppPhotoUrl_InvalidName_ReturnsNull(string name)
    {
        using var httpClient = CreateHttpClient(new StubHttpMessageHandler());
        var sut = CreateSut(httpClient);

        Assert.Null(sut.BuildAppPhotoUrl(name));
    }

    [Fact]
    public void BuildAppPhotoUrl_ValidPhotoName_ReturnsEncodedAppEndpointUrl()
    {
        using var httpClient = CreateHttpClient(new StubHttpMessageHandler());
        var sut = CreateSut(httpClient);

        var url = sut.BuildAppPhotoUrl("places/ChIJ123/photos/AUac456");

        Assert.NotNull(url);
        Assert.StartsWith("/api/ai/places/photos", url, StringComparison.Ordinal);
        Assert.Contains("name=places%2FChIJ123%2Fphotos%2FAUac456", url, StringComparison.Ordinal);
        Assert.Contains("maxWidthPx=900", url, StringComparison.Ordinal);
        Assert.Contains("maxHeightPx=520", url, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAppPhotoUrl_ClampsDimensions()
    {
        using var httpClient = CreateHttpClient(new StubHttpMessageHandler());
        var sut = CreateSut(httpClient);

        var url = sut.BuildAppPhotoUrl("places/ChIJ123/photos/AUac456", maxWidthPx: 9999, maxHeightPx: -20);

        Assert.NotNull(url);
        Assert.Contains("maxWidthPx=4800", url, StringComparison.Ordinal);
        Assert.Contains("maxHeightPx=1", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvePhotoAsync_GoogleReturnsPhotoUri_SucceedsWithRedirectUri()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/places/ChIJ123/photos/AUac456/media", request.RequestUri?.AbsolutePath);
            Assert.Contains("maxWidthPx=900", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("maxHeightPx=520", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("skipHttpRedirect=true", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("test-api-key", request.Headers.GetValues("X-Goog-Api-Key").Single());

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"name":"places/ChIJ123/photos/AUac456","photoUri":"https://lh3.googleusercontent.com/photo"}""",
                        Encoding.UTF8,
                        "application/json")
                });
        });
        using var httpClient = CreateHttpClient(handler);
        var sut = CreateSut(httpClient);

        var result = await sut.ResolvePhotoAsync(
            new GooglePlacesPhotoMediaRequest(
                PhotoResourceName: "places/ChIJ123/photos/AUac456",
                MaxWidthPx: 900,
                MaxHeightPx: 520,
                SkipHttpRedirect: true),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("https://lh3.googleusercontent.com/photo", result.RedirectUri);
    }

    [Fact]
    public async Task ResolvePhotoAsync_InvalidName_ReturnsInvalidPhotoName()
    {
        using var httpClient = CreateHttpClient(new StubHttpMessageHandler());
        var sut = CreateSut(httpClient);

        var result = await sut.ResolvePhotoAsync(
            new GooglePlacesPhotoMediaRequest("https://evil.example/photo", null, null, true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_photo_name", result.ErrorCode);
    }

    [Fact]
    public async Task ResolvePhotoAsync_PlacesDisabled_ReturnsServiceFailure()
    {
        using var httpClient = CreateHttpClient(new StubHttpMessageHandler());
        var sut = CreateSut(httpClient, enabled: false);

        var result = await sut.ResolvePhotoAsync(
            new GooglePlacesPhotoMediaRequest("places/ChIJ123/photos/AUac456", null, null, true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("places_disabled", result.ErrorCode);
    }

    [Fact]
    public async Task ResolvePhotoAsync_GoogleProviderFailure_ReturnsProviderError()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"status":"INVALID_ARGUMENT"}}""", Encoding.UTF8, "application/json")
            }));
        using var httpClient = CreateHttpClient(handler);
        var sut = CreateSut(httpClient);

        var result = await sut.ResolvePhotoAsync(
            new GooglePlacesPhotoMediaRequest("places/ChIJ123/photos/AUac456", null, null, true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_argument", result.ErrorCode);
    }

    [Fact]
    public async Task ResolvePhotoAsync_Timeout_ReturnsTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = CreateHttpClient(handler);
        var sut = CreateSut(httpClient, timeoutSeconds: 1);

        var result = await sut.ResolvePhotoAsync(
            new GooglePlacesPhotoMediaRequest("places/ChIJ123/photos/AUac456", null, null, true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal("places_photo_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task GetPlacePhoto_InvalidName_Returns400()
    {
        var context = CreateHttpContext();
        var result = await GetPlacePhotoEndpoint.HandleAsync(
            "https://evil.example/photo",
            null,
            null,
            new FakePhotoService("invalid_photo_name"),
            context,
            CancellationToken.None);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task GetPlacePhoto_GoogleReturnsPhotoUri_Redirects()
    {
        var context = CreateHttpContext();
        var result = await GetPlacePhotoEndpoint.HandleAsync(
            "places/ChIJ123/photos/AUac456",
            900,
            520,
            new FakePhotoService(null, "https://lh3.googleusercontent.com/photo"),
            context,
            CancellationToken.None);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("https://lh3.googleusercontent.com/photo", context.Response.Headers.Location);
        Assert.Equal("private, max-age=1800", context.Response.Headers.CacheControl);
    }

    [Theory]
    [InlineData("places_disabled", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("places_photo_provider_error", StatusCodes.Status502BadGateway)]
    [InlineData("places_photo_timeout", StatusCodes.Status504GatewayTimeout)]
    public async Task GetPlacePhoto_ServiceFailure_ReturnsExpectedStatus(string errorCode, int expectedStatus)
    {
        var context = CreateHttpContext();
        var result = await GetPlacePhotoEndpoint.HandleAsync(
            "places/ChIJ123/photos/AUac456",
            null,
            null,
            new FakePhotoService(errorCode),
            context,
            CancellationToken.None);

        await result.ExecuteAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    private static GooglePlacesPhotoService CreateSut(
        HttpClient httpClient,
        bool enabled = true,
        int timeoutSeconds = 8)
    {
        return new GooglePlacesPhotoService(
            httpClient,
            Options.Create(new GooglePlacesOptions
            {
                Enabled = enabled,
                ApiKey = "test-api-key",
                BaseUrl = "https://places.googleapis.com",
                TimeoutSeconds = timeoutSeconds
            }),
            NullLogger<GooglePlacesPhotoService>.Instance);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://places.googleapis.com")
        };
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        return new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider()
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handlerFunc = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (handlerFunc is null)
            {
                throw new InvalidOperationException("HTTP request was not expected.");
            }

            return handlerFunc(request, cancellationToken);
        }
    }

    private sealed class FakePhotoService(
        string? errorCode,
        string? redirectUri = null) : IGooglePlacesPhotoService
    {
        public string? BuildAppPhotoUrl(string? photoResourceName, int? maxWidthPx = null, int? maxHeightPx = null)
        {
            return photoResourceName is null ? null : "/api/ai/places/photos";
        }

        public Task<GooglePlacesPhotoMediaResult> ResolvePhotoAsync(
            GooglePlacesPhotoMediaRequest request,
            CancellationToken cancellationToken)
        {
            if (errorCode is not null)
            {
                return Task.FromResult(new GooglePlacesPhotoMediaResult(
                    Succeeded: false,
                    RedirectUri: null,
                    Content: null,
                    ContentType: null,
                    ErrorCode: errorCode,
                    ErrorMessage: errorCode,
                    Elapsed: TimeSpan.Zero));
            }

            return Task.FromResult(new GooglePlacesPhotoMediaResult(
                Succeeded: true,
                RedirectUri: redirectUri,
                Content: null,
                ContentType: null,
                ErrorCode: null,
                ErrorMessage: null,
                Elapsed: TimeSpan.Zero));
        }
    }
}
