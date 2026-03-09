namespace NSFinTech.Shared.Configuration;

public static class EnvironmentVariableNames
{
    public const string DatabaseConnectionString = "NSFINTECH_DB_CONNECTION_STRING";
    public const string ApiBaseUrl = "EXPO_PUBLIC_API_BASE_URL";
    public const string JwtSigningKey = "NSFINTECH_JWT_SIGNING_KEY";
    public const string AllowedCorsOrigins = "NSFINTECH_ALLOWED_CORS_ORIGINS";
    public const string GoogleClientId = "NSFINTECH_GOOGLE_CLIENT_ID";
    public const string GoogleClientSecret = "NSFINTECH_GOOGLE_CLIENT_SECRET";
    public const string GoogleRedirectUri = "NSFINTECH_GOOGLE_REDIRECT_URI";
    public const string EmailSenderAddress = "NSFINTECH_EMAIL_SENDER_ADDRESS";
    public const string EmailTransportMode = "NSFINTECH_EMAIL_TRANSPORT_MODE";
    public const string TrueLayerClientId = "TRUELAYER_CLIENT_ID";
    public const string TrueLayerClientSecret = "TRUELAYER_CLIENT_SECRET";
    public const string TrueLayerRedirectUri = "TRUELAYER_REDIRECT_URI";
    public const string TrueLayerEnvironment = "TRUELAYER_ENVIRONMENT";
    public const string TrueLayerAuthBaseUrl = "TRUELAYER_AUTH_BASE_URL";
    public const string TrueLayerApiBaseUrl = "TRUELAYER_API_BASE_URL";
}
