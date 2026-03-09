namespace NSFinTech.Api.Modules.Support.DTOs;

public sealed record DeletionRequestDto(
    Guid Id,
    Guid UserId,
    string Status,
    DateTime RequestedUtc,
    DateTime UpdatedUtc,
    string? Notes);
