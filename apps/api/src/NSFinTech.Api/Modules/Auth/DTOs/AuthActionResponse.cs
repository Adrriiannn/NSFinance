namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record AuthActionResponse(string Message, string? DebugToken = null);
