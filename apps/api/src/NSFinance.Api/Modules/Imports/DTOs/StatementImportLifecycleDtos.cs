namespace NSFinance.Api.Modules.Imports.DTOs;

public sealed record StatementImportRevisionRequest(int? ExpectedRevision);

public sealed record StatementImportLifecycleMutationDto(
    Guid BatchId,
    string Status,
    int Revision,
    int IncludedRowCount,
    int CommittedRowCount,
    DateTime UpdatedUtc,
    DateTime? CommittedUtc,
    DateTime? UndoneUtc,
    bool WasReplay);
