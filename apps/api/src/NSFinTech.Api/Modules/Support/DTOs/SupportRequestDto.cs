namespace NSFinTech.Api.Modules.Support.DTOs;

public sealed record SupportRequestDto(
    Guid Id,
    Guid? UserId,
    string Category,
    string Message,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
