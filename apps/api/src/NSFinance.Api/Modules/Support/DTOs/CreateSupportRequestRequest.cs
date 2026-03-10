namespace NSFinance.Api.Modules.Support.DTOs;

public sealed record SupportScreenshotUploadRequest(
    string FileName,
    string ContentType,
    string Base64Data);

public sealed record CreateSupportRequestRequest(
    string Category,
    string Subcategory,
    string Title,
    string Message,
    string? ContactEmail,
    Guid? ConnectionId,
    Guid? LinkedBankAccountId,
    IReadOnlyList<SupportScreenshotUploadRequest>? Screenshots);