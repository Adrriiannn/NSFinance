namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record UserProfileDto(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc);
