namespace NSFinTech.Api.Modules.Banking.Services.Models;

public sealed record TrueLayerTokenExchangeResult(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresUtc,
    string? Scope);

public sealed record TrueLayerApiError(
    string ErrorCode,
    string ErrorDescription,
    int StatusCode);

public sealed record TrueLayerAccountRecord(
    string AccountId,
    string DisplayName,
    string Currency,
    string? AccountType,
    string? AccountSubType,
    string? ProviderId,
    string? ProviderDisplayName,
    string? AccountNumberMetadataJson,
    string RawPayloadJson);

public sealed record TrueLayerBalanceRecord(
    decimal? Available,
    decimal? Current,
    decimal? Overdraft,
    string Currency,
    DateTime CapturedAtUtc,
    string RawPayloadJson);

public sealed record TrueLayerTransactionRecord(
    string? ProviderTransactionId,
    decimal Amount,
    string Currency,
    DateTime BookedAtUtc,
    string Description,
    string? TransactionType,
    string? TransactionStatus,
    string DedupeKey,
    string RawPayloadJson);

public sealed record BankSyncResult(
    Guid ConnectionId,
    int AccountsSynced,
    int BalancesSynced,
    int TransactionsImported,
    string Status,
    DateTime SyncedAtUtc);
