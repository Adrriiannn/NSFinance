using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public (string AccessToken, DateTime ExpiresAtUtc) CreateAccessToken(User user, Guid sessionId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.PrimaryEmail),
            new("email", user.PrimaryEmail),
            new("sid", sessionId.ToString()),
            new(ClaimTypes.Role, user.Role)
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var jwt = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return (jwt, expiresAtUtc);
    }
}
