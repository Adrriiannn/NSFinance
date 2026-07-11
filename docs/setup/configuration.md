# Configuration And Secrets

This is the canonical source for NSFinance configuration ownership.

## API Config Files

- `apps/api/src/NSFinance.Api/appsettings.json`
  - shared safe defaults
  - live TrueLayer defaults
  - Azure OpenAI provider selected by default
- `apps/api/src/NSFinance.Api/appsettings.Local.json`
  - machine-local secrets only
  - gitignored
- `apps/api/src/NSFinance.Api/appsettings.Local.example.json`
  - onboarding template for the local secret structure

The API reads configuration in this order:

1. `appsettings.json`
2. `appsettings.Local.json`
3. environment variables

## API Secrets

Store machine-local secrets only in:

```text
apps/api/src/NSFinance.Api/appsettings.Local.json
```

Required keys:

- `ConnectionStrings:DefaultConnection`
- `Jwt:SigningKey`
- `TrueLayer:ClientId`
- `TrueLayer:ClientSecret`
- `TrueLayer:RedirectUri`
- `GoogleAuth:WebClientId`
- `GoogleAuth:AndroidClientIdProd`

AI keys:

- `AI:AzureOpenAI:Endpoint`
- `AI:AzureOpenAI:Deployment`
- `AI:AzureOpenAI:ApiKey` or managed identity settings

Reserved key for backend captcha verification:

- `Turnstile:SecretKey`

## Azure App Settings

Production Azure settings should come from App Service settings and Key Vault-backed values.

Primary keys:

- `ConnectionStrings__DefaultConnection`
- `Jwt__SigningKey`
- `TrueLayer__ClientId`
- `TrueLayer__ClientSecret`
- `TrueLayer__RedirectUri`
- `TrueLayer__Environment=live`
- `TrueLayer__AuthBaseUrl=https://auth.truelayer.com`
- `TrueLayer__ApiBaseUrl=https://api.truelayer.com`
- `DataProtection__KeysPath` or `NSFINANCE_DATA_PROTECTION_KEYS_PATH`
- `GoogleAuth__WebClientId`
- `GoogleAuth__AndroidClientIdProd`

Environment variable aliases:

- `NSFINANCE_DB_CONNECTION_STRING`
- `NSFINANCE_JWT_SIGNING_KEY`
- `NSFINANCE_ALLOWED_CORS_ORIGINS`
- `NSFINANCE_GOOGLE_WEB_CLIENT_ID`
- `NSFINANCE_GOOGLE_ANDROID_CLIENT_ID_PROD`
- `TRUELAYER_CLIENT_ID`
- `TRUELAYER_CLIENT_SECRET`
- `TRUELAYER_REDIRECT_URI`
- `TRUELAYER_ENVIRONMENT`
- `TRUELAYER_AUTH_BASE_URL`
- `TRUELAYER_API_BASE_URL`
- `NSFINANCE_DATA_PROTECTION_KEYS_PATH`

## TrueLayer

- Environment is live-only.
- Callback URI is `https://api.finance.nsireland.ie/api/banking/truelayer/callback`.
- Auth base URL is `https://auth.truelayer.com`.
- API base URL is `https://api.truelayer.com`.
- Provider targeting is backend-driven and currently uses Ireland provider group `ie-ob-all`.

Requested OAuth scopes:

- `info`
- `accounts`
- `cards`
- `balance`
- `transactions`
- `offline_access`
- `direct_debits`
- `standing_orders`

## Mobile Public Config

`EXPO_PUBLIC_*` values are public and bundled into the client app. Never place private secrets in these keys.

Expected keys:

- `EXPO_PUBLIC_APP_ENV=production`
- `EXPO_PUBLIC_API_BASE_URL=https://api.finance.nsireland.ie`
- `EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID`
- `EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_PROD`
- `EXPO_PUBLIC_TURNSTILE_PAGE_BASE_URL=https://api.finance.nsireland.ie`

## Not Yet Active

- `Turnstile:SecretKey` is template-ready but not yet used by runtime services.
- Email transport environment constants exist for provider wiring, but no email transport service is active yet.
