using Google.Apis.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Tests.Unit;

public class GoogleAuthServiceTests
{
    [Fact]
    public async Task VerifyIdTokenAsync_ValidToken_ReturnsMappedPayload()
    {
        var verifier = new StubGoogleIdTokenVerifier
        {
            Payload = new GoogleJsonWebSignature.Payload
            {
                Subject = "google-sub-1",
                Email = "person@test.local",
                EmailVerified = true,
                Name = "Person One",
                GivenName = "Person",
                FamilyName = "One",
                Picture = "https://example.com/avatar.png"
            }
        };

        var service = BuildService(verifier, "expected-client-id");
        var result = await service.VerifyIdTokenAsync("token-1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("google-sub-1", result.Value!.Subject);
        Assert.Equal("person@test.local", result.Value.Email);
        Assert.True(result.Value.EmailVerified);
        Assert.Equal("expected-client-id", verifier.LastAudience);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_InvalidToken_ReturnsUnauthorized()
    {
        var verifier = new StubGoogleIdTokenVerifier
        {
            Exception = new InvalidJwtException("invalid token")
        };

        var service = BuildService(verifier, "expected-client-id");
        var result = await service.VerifyIdTokenAsync("bad-token", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_id_token_invalid", result.Error?.Code);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Error?.StatusCode);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_WrongAudience_ReturnsUnauthorized()
    {
        var verifier = new StubGoogleIdTokenVerifier
        {
            Exception = new InvalidJwtException("Wrong recipient, payload audience did not match.")
        };

        var service = BuildService(verifier, "expected-client-id");
        var result = await service.VerifyIdTokenAsync("token-with-wrong-aud", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_id_token_invalid", result.Error?.Code);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_MissingConfiguration_ReturnsServiceUnavailable()
    {
        var verifier = new StubGoogleIdTokenVerifier();
        var service = BuildService(verifier, clientId: string.Empty);

        var result = await service.VerifyIdTokenAsync("token-1", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_sign_in_not_configured", result.Error?.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.Error?.StatusCode);
    }

    private static GoogleAuthService BuildService(StubGoogleIdTokenVerifier verifier, string clientId)
    {
        return new GoogleAuthService(
            Options.Create(new GoogleAuthOptions { ClientId = clientId }),
            verifier,
            NullLogger<GoogleAuthService>.Instance);
    }

    private sealed class StubGoogleIdTokenVerifier : IGoogleIdTokenVerifier
    {
        public GoogleJsonWebSignature.Payload Payload { get; set; } = new();
        public Exception? Exception { get; set; }
        public string? LastAudience { get; private set; }

        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(
            string idToken,
            string audience,
            CancellationToken cancellationToken)
        {
            LastAudience = audience;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Payload);
        }
    }
}
