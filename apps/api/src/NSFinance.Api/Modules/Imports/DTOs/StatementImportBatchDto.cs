namespace NSFinance.Api.Modules.Imports.DTOs;

public sealed record StatementImportRowDto(
    Guid Id,
    int RowNumber,
    string ValidationStatus,
    string? ValidationCode,
    string DuplicateClassification,
    string ReviewDisposition,
    Guid? DuplicateCandidateTransactionId,
    DateOnly? EffectiveDate,
    DateTime? EffectiveAtUtc,
    string? TimestampPrecision,
    string? Description,
    decimal? Amount,
    string? Currency,
    Guid? CommittedTransactionId);

public sealed record StatementImportBatchDto(
    Guid Id,
    Guid AccountId,
    string FileName,
    string Status,
    string AccountCurrency,
    string Locale,
    string TimeZoneId,
    long FileSizeBytes,
    int TotalRowCount,
    int ValidRowCount,
    int InvalidRowCount,
    int ExactDuplicateRowCount,
    int LikelyDuplicateRowCount,
    int IncludedRowCount,
    int CommittedRowCount,
    int Revision,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? ReadyForReviewUtc,
    DateTime? CommittedUtc,
    DateTime? UndoneUtc,
    DateTime? ExpiresUtc,
    bool WasReplay);

public sealed record StatementImportRowPageDto(
    Guid BatchId,
    IReadOnlyList<StatementImportRowDto> Items,
    string? NextCursor,
    int PageSize,
    int TotalMatchingRows);
