namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankEnrichmentConnectionProgressDto(
    Guid ConnectionId,
    string? ProviderDisplayName,
    bool InProgress,
    bool Completed,
    double ProgressPercent,
    int ProcessedCount,
    int TotalCount,
    int RemainingCount,
    string Stage,
    DateTime? LastUpdatedUtc);

public sealed record BankEnrichmentProgressDto(
    bool InProgress,
    bool Completed,
    double ProgressPercent,
    int ProcessedCount,
    int TotalCount,
    int RemainingCount,
    string Stage,
    DateTime? LastUpdatedUtc,
    bool NewestFirst,
    IReadOnlyList<BankEnrichmentConnectionProgressDto> Connections);
