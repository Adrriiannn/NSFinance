namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? FirstName,
    string? LastName);
