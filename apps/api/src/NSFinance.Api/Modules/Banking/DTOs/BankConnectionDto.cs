namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankConnectionDto(
    Guid Id,
    string Provider,
    string ProviderEnvironment,
    string? ProviderDisplayName,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? LastSuccessfulSyncUtc,
    DateTime? LastSyncAttemptedUtc,
    string? LastErrorCode);
