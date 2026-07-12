using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Validators;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class MicrosoftAccessTokenVerifier : IMicrosoftAccessTokenVerifier
{
    private readonly MicrosoftAuthOptions _options;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly AadIssuerValidator _issuerValidator;

    public MicrosoftAccessTokenVerifier(
        HttpClient httpClient,
        IOptions<MicrosoftAuthOptions> options)
    {
        _options = options.Value;
        var documentRetriever = new HttpDocumentRetriever(httpClient) { RequireHttps = true };
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            _options.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            documentRetriever);
        _issuerValidator = AadIssuerValidator.GetAadIssuerValidator(_options.Authority, httpClient);
    }

    public async Task<ClaimsPrincipal> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Microsoft authentication is not configured.");
        }

        var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(
            accessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                IssuerValidator = _issuerValidator.Validate,
                ValidateAudience = true,
                ValidAudience = _options.ClientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            },
            out var validatedToken);

        if (validatedToken is not JwtSecurityToken jwt
            || !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            throw new SecurityTokenValidationException("Microsoft access token algorithm is invalid.");
        }

        return principal;
    }
}
