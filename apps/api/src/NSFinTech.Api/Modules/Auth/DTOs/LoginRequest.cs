namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record LoginRequest(
    string Email,
    string Password);
