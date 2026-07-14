namespace NSFinance.Api.Modules.Transactions.DTOs;

public sealed record TransactionPageRequest(
    Guid? AccountId,
    int? PageSize,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? Direction,
    string? Cursor);

public sealed record TransactionPageFiltersDto(
    Guid? AccountId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Direction);

public sealed record TransactionPageDto(
    IReadOnlyList<TransactionDto> Items,
    string? NextCursor,
    bool HasMore,
    int PageSize,
    TransactionPageFiltersDto Filters);
