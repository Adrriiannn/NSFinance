namespace NSFinance.Api.Modules.Imports.DTOs;

public sealed record StatementImportRowReviewRequest(
    Guid RowId,
    string? ReviewDisposition);

public sealed record ReviewStatementImportRowsRequest(
    int? ExpectedRevision,
    IReadOnlyList<StatementImportRowReviewRequest>? Decisions);

public sealed record StatementImportReviewedRowDto(
    Guid RowId,
    int RowNumber,
    string ReviewDisposition,
    DateTime UpdatedUtc);

public sealed record StatementImportReviewMutationDto(
    Guid BatchId,
    string Status,
    int Revision,
    int IncludedRowCount,
    int PendingRowCount,
    int ExcludedRowCount,
    DateTime UpdatedUtc,
    IReadOnlyList<StatementImportReviewedRowDto> ReviewedRows);
