namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Guid SessionId,
    UserProfileDto User);
