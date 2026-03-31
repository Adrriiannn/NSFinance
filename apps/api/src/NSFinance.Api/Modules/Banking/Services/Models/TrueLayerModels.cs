namespace NSFinance.Api.Modules.Banking.Services.Models;

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
    string? ProviderIconUri,
    string? ProviderLogoUri,
    string? ProviderBrandBgColor,
    string? AccountNumberMetadataJson,
    string RawPayloadJson);

public sealed record TrueLayerProviderBranding(
    string? ProviderId,
    string? ProviderDisplayName,
    string? ProviderIconUri,
    string? ProviderLogoUri,
    string? ProviderBrandBgColor);

public sealed record TrueLayerIdentityInfoRecord(
    string? FullName,
    string? Email,
    string? Phone,
    string? DateOfBirth,
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
    string? NormalizedProviderTransactionId,
    decimal Amount,
    string Currency,
    DateTime BookedAtUtc,
    DateTime? ValueAtUtc,
    string Description,
    string? TransactionType,
    string? TransactionStatus,
    string SourceEndpoint,
    string? ProviderStatus,
    string StatusNormalizationReason,
    string DedupeKey,
    string RawPayloadJson);

public sealed record TrueLayerTransactionQueryWindow(
    DateTime? FromUtc,
    DateTime? ToUtc,
    string Mode,
    string? PolicyName);

public sealed record TrueLayerCardRecord(
    string CardId,
    string DisplayName,
    string Currency,
    string? ProviderAccountId,
    string? CardType,
    string? CardNetwork,
    string? CardNumberLastFour,
    string? NameOnCard,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    string RawPayloadJson);

public sealed record TrueLayerCardBalanceRecord(
    decimal? Available,
    decimal? Current,
    decimal? Limit,
    decimal? Outstanding,
    string Currency,
    DateTime CapturedAtUtc,
    string RawPayloadJson);

public sealed record TrueLayerCardTransactionRecord(
    string? ProviderTransactionId,
    decimal Amount,
    string Currency,
    DateTime BookedAtUtc,
    string Description,
    string? TransactionType,
    string? TransactionStatus,
    string DedupeKey,
    string RawPayloadJson);

public sealed record TrueLayerDirectDebitRecord(
    string DirectDebitId,
    string? Status,
    string? MandateType,
    string? Reference,
    string? MerchantName,
    DateTime? PreviousPaymentDateUtc,
    decimal? PreviousPaymentAmount,
    string? PreviousPaymentCurrency,
    DateTime? NextPaymentDateUtc,
    decimal? NextPaymentAmount,
    string? NextPaymentCurrency,
    string RawPayloadJson);

public sealed record TrueLayerStandingOrderRecord(
    string StandingOrderId,
    string? Status,
    string? Frequency,
    string? Reference,
    string? PayeeName,
    DateTime? FirstPaymentDateUtc,
    DateTime? NextPaymentDateUtc,
    DateTime? FinalPaymentDateUtc,
    decimal? NextPaymentAmount,
    string? NextPaymentCurrency,
    string? PayeeAccountMetadataJson,
    string RawPayloadJson);

public sealed record BankSyncResult(
    Guid ConnectionId,
    int AccountsSynced,
    int BalancesSynced,
    int TransactionsImported,
    int SettledFetched,
    int PendingFetched,
    DateTime? LatestFetchedRowUtc,
    bool? HasFetchedRowNewerThanCheckpoint,
    string FreshnessSummary,
    string Status,
    DateTime SyncedAtUtc,
    bool DataChanged);
