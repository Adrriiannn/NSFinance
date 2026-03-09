namespace NSFinTech.Api.Modules.Banking.Services;

public static class BankingProviders
{
    public const string TrueLayer = "TrueLayer";
}

public static class BankConnectionStatuses
{
    public const string NotConnected = "not_connected";
    public const string ConnectionStarted = "connection_started";
    public const string ConsentInProgress = "consent_in_progress";
    public const string Connected = "connected";
    public const string SyncPending = "sync_pending";
    public const string Synced = "synced";
    public const string ReauthRequired = "reauth_required";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string Failed = "failed";
}

public static class TrueLayerScopes
{
    public static readonly IReadOnlyList<string> Default =
    [
        "accounts",
        "balance",
        "transactions",
        "offline_access"
    ];
}

public static class TrueLayerProviders
{
    public static readonly IReadOnlyList<string> SandboxDefault =
    [
        "uk-cs-mock"
    ];
}
