namespace NSFinance.Api.Modules.Support.DTOs;

public sealed record SupportRequestDto(
    Guid Id,
    Guid? UserId,
    string Category,
    string Subcategory,
    string Title,
    string Message,
    string? ContactEmail,
    string? ScreenshotReference,
    Guid? ConnectionId,
    Guid? LinkedBankAccountId,
    string DiagnosticsJson,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
