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
    public static readonly IReadOnlyList<string> SandboxDefault =
    [
        "uk-cs-mock"
    ];

    public static readonly IReadOnlyList<string> LiveIrelandDefault =
    [
        "ie-ob-all"
    ];
}

public static class TrueLayerCountryIds
{
    public const string Ireland = "IE";
}
