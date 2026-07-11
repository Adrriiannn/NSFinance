namespace NSFinance.Shared.Configuration;

public static class EnvironmentVariableNames
{
    public const string DatabaseConnectionString = "NSFINANCE_DB_CONNECTION_STRING";
    public const string ApiBaseUrl = "EXPO_PUBLIC_API_BASE_URL";
    public const string JwtSigningKey = "NSFINANCE_JWT_SIGNING_KEY";
    public const string AllowedCorsOrigins = "NSFINANCE_ALLOWED_CORS_ORIGINS";
    public const string GoogleClientId = "NSFINANCE_GOOGLE_CLIENT_ID";
    public const string GoogleWebClientId = "NSFINANCE_GOOGLE_WEB_CLIENT_ID";
    public const string GoogleAndroidClientIdProd = "NSFINANCE_GOOGLE_ANDROID_CLIENT_ID_PROD";
    public const string GoogleClientSecret = "NSFINANCE_GOOGLE_CLIENT_SECRET";
    public const string GoogleRedirectUri = "NSFINANCE_GOOGLE_REDIRECT_URI";
    public const string EmailSenderAddress = "NSFINANCE_EMAIL_SENDER_ADDRESS";
    public const string EmailTransportMode = "NSFINANCE_EMAIL_TRANSPORT_MODE";
    public const string TrueLayerClientId = "TRUELAYER_CLIENT_ID";
    public const string TrueLayerClientSecret = "TRUELAYER_CLIENT_SECRET";
    public const string TrueLayerRedirectUri = "TRUELAYER_REDIRECT_URI";
    public const string TrueLayerEnvironment = "TRUELAYER_ENVIRONMENT";
    public const string TrueLayerAuthBaseUrl = "TRUELAYER_AUTH_BASE_URL";
    public const string TrueLayerApiBaseUrl = "TRUELAYER_API_BASE_URL";
    public const string DataProtectionKeysPath = "NSFINANCE_DATA_PROTECTION_KEYS_PATH";
}
