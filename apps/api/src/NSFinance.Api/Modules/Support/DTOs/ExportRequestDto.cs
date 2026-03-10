namespace NSFinance.Api.Modules.Support.DTOs;

public sealed record ExportRequestDto(
    Guid Id,
    Guid UserId,
    string Status,
    DateTime RequestedUtc,
    DateTime UpdatedUtc,
    string? Notes);
