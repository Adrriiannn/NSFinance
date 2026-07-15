namespace NSFinance.Api.Modules.Imports.Services;

public sealed record StageStatementImportRowCommand(
    int RowNumber,
    string RowFingerprint,
    string? SourceReferenceFingerprint,
    string ValidationStatus,
    string? ValidationCode,
    string DuplicateClassification,
    string ReviewDisposition,
    Guid? DuplicateCandidateTransactionId,
    string SourceEvidenceJson,
    DateOnly? EffectiveDate,
    DateTime? EffectiveAtUtc,
    string? TimestampPrecision,
    string? Description,
    decimal? Amount,
    string? Currency);

public sealed record StageStatementImportBatchCommand(
    Guid AccountId,
    string FileName,
    long FileSizeBytes,
    string SourceFingerprint,
    string MappingFingerprint,
    string ParserVersion,
    string MappingVersion,
    string MappingJson,
    string Locale,
    string TimeZoneId,
    IReadOnlyList<StageStatementImportRowCommand> Rows);

public sealed record StatementImportRowsQuery(
    string? Cursor = null,
    int PageSize = 50,
    string? ValidationStatus = null,
    string? DuplicateClassification = null,
    string? ReviewDisposition = null);

public sealed record StatementImportRowReviewDecision(
    Guid RowId,
    string? ReviewDisposition);

public sealed record ReviewStatementImportRowsCommand(
    int? ExpectedRevision,
    IReadOnlyList<StatementImportRowReviewDecision>? Decisions);

public sealed record StatementImportRevisionCommand(int? ExpectedRevision);
