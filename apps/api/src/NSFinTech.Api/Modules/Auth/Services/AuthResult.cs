using NSFinTech.Api.Modules.Auth.DTOs;

namespace NSFinTech.Api.Modules.Auth.Services;

public sealed record AuthResult(AuthTokenResponse? Response, string? Error, bool Conflict = false);
