namespace NSFinTech.Api.Modules.Users.DTOs;

public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    DateTime CreatedUtc);
