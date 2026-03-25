namespace NSFinance.Api.Modules.Support.DTOs;

public sealed record CreateExportRequestRequest(
    string? Notes = null,
    string? Format = null,
    Guid? ConnectionId = null,
    Guid? FinancialAccountId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? PeriodPreset = null);
