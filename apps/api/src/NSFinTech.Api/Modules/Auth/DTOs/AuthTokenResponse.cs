namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    UserProfileDto User);
