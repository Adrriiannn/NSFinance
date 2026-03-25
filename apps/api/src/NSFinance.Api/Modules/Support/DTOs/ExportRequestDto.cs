namespace NSFinance.Api.Modules.Support.DTOs;

public sealed record ExportRequestDto(
    Guid Id,
    Guid UserId,
    string Status,
    DateTime RequestedUtc,
    DateTime UpdatedUtc,
    string? Notes,
    string Format,
    Guid? ConnectionId,
    string? ConnectionLabel,
    Guid? FinancialAccountId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? PeriodPreset,
    long? FileSizeBytes);
