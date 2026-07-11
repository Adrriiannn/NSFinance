namespace NSFinance.Api.Modules.Banking.Services;

public static class BankingProviders
{
    public const string TrueLayer = "TrueLayer";
}

public static class BankConnectionStatuses
{
    public const string NotConnected = "not_connected";
    public const string ConnectionStarted = "connection_started";
    public const string ConsentInProgress = "consent_in_progress";
    public const string ConnectedPendingSync = "connected_pending_sync";
    public const string Connected = "connected";
    public const string SyncPending = "sync_pending";
    public const string Synced = "synced";
    public const string ReauthRequired = "reauth_required";
    public const string Expired = "expired";
    public const string DisconnectPending = "disconnect_pending";
    public const string DisconnectFailed = "disconnect_failed";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
}

public static class BankConnectionLifecycleStages
{
    public const string Idle = "idle";
    public const string LaunchingAuthorization = "launching_authorization";
    public const string AwaitingBankAuthorization = "awaiting_bank_authorization";
    public const string AuthorizationReturned = "authorization_returned";
    public const string AuthorizationConfirmed = "authorization_confirmed";
    public const string DeepLinkReturnInitiated = "deep_link_return_initiated";
    public const string ReturnedToApp = "returned_to_app";
    public const string ConnectionCreated = "connection_created";
    public const string FetchingAccounts = "fetching_accounts";
    public const string FetchingBalances = "fetching_balances";
    public const string FetchingTransactions = "fetching_transactions";
    public const string TransactionsFetched = "transactions_fetched";
    public const string CategorizationPending = "categorization_pending";
    public const string CategorizationRunning = "categorization_running";
    public const string PostProcessingRunning = "post_processing_running";
    public const string Completed = "completed";
    public const string CompletedWithLimitedHistory = "completed_with_limited_history";
    public const string CompletedWithWarnings = "completed_with_warnings";
    public const string DelayedRetrying = "delayed_retrying";
    public const string CooldownWait = "cooldown_wait";
    public const string ProviderSlow = "provider_slow";
    public const string PartialFailure = "partial_failure";
    public const string Failed = "failed";
    public const string ReauthRequired = "reauth_required";
    public const string Disconnected = "disconnected";
    public const string Disconnecting = "disconnecting";
}

public static class BankConnectionAttemptStatuses
{
    public const string Created = "created";
    public const string AuthLaunched = "auth_launched";
    public const string AwaitingCallback = "awaiting_callback";
    public const string CallbackReceived = "callback_received";
    public const string AppReturnInitiated = "app_return_initiated";
    public const string AppReturnConfirmed = "app_return_confirmed";
    public const string ConnectionCreated = "connection_created";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Expired = "expired";
    public const string Superseded = "superseded";
    public const string Cancelled = "cancelled";
}

public static class BankConnectionCompletionSemantics
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string CompletedWithLimitedHistory = "completed_with_limited_history";
    public const string CompletedWithWarnings = "completed_with_warnings";
    public const string NeedsAttention = "needs_attention";
}

public static class BankConnectionRequiredActionKinds
{
    public const string None = "none";
    public const string Reconnect = "reconnect";
    public const string RetrySync = "retry_sync";
    public const string RetryDisconnect = "retry_disconnect";
}

public static class TrueLayerScopes
{
    public static readonly IReadOnlyList<string> Default =
    [
        "info",
        "accounts",
        "cards",
        "balance",
        "transactions",
        "offline_access",
        "direct_debits",
        "standing_orders"
    ];
}

public static class TrueLayerProviders
{
    public static readonly IReadOnlyList<string> LiveIrelandDefault =
    [
        "ie-ob-all"
    ];
}

public static class TrueLayerCountryIds
{
    public const string Ireland = "IE";
}
