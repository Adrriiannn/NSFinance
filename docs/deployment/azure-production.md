# NSFinance Azure Production Checklist

## Target Endpoints
- Production API base URL: `https://api.finance.nsireland.ie`
- Health endpoint: `GET /health`

## Required Azure App Service Environment Variables
- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__DefaultConnection=<Azure PostgreSQL connection string>`
- `Jwt__SigningKey=<32+ character signing key>`
- `GoogleAuth__WebClientId=<Google OAuth web client id>`
- `GoogleAuth__AndroidClientIdProd=<Google OAuth Android production client id>`
- `GoogleAuth__AndroidClientIdDebug=<Google OAuth Android debug client id (optional in production)>`
- `TrueLayer__ClientId=<TrueLayer client id>`
- `TrueLayer__ClientSecret=<TrueLayer client secret>`
- `TrueLayer__RedirectUri=https://api.finance.nsireland.ie/api/banking/truelayer/callback`
- `TrueLayer__Environment=live`
- `TrueLayer__AuthBaseUrl=https://auth.truelayer.com`
- `TrueLayer__ApiBaseUrl=https://api.truelayer.com`
- `Cors__AllowedOrigins=<comma-separated allowed web origins, if needed>`
- Optional reserved (not currently consumed by API runtime): `Turnstile__SecretKey`

## Optional Environment Variable Aliases
- `NSFINANCE_DB_CONNECTION_STRING` (alias for `ConnectionStrings__DefaultConnection`)
- `NSFINANCE_JWT_SIGNING_KEY` (alias for `Jwt__SigningKey`)
- `NSFINANCE_GOOGLE_WEB_CLIENT_ID` (alias for `GoogleAuth__WebClientId`)
- `NSFINANCE_GOOGLE_ANDROID_CLIENT_ID_DEBUG` (alias for `GoogleAuth__AndroidClientIdDebug`)
- `NSFINANCE_GOOGLE_ANDROID_CLIENT_ID_PROD` (alias for `GoogleAuth__AndroidClientIdProd`)
- `NSFINANCE_GOOGLE_CLIENT_ID` (legacy alias for `GoogleAuth__ClientId`)
- `TRUELAYER_CLIENT_ID`, `TRUELAYER_CLIENT_SECRET`, `TRUELAYER_REDIRECT_URI`, `TRUELAYER_ENVIRONMENT`, `TRUELAYER_AUTH_BASE_URL`, `TRUELAYER_API_BASE_URL` (aliases for `TrueLayer__*`)
- `NSFINANCE_ALLOWED_CORS_ORIGINS` (alias for `Cors__AllowedOrigins`)

## Backend Config Structure
- Base config: `apps/api/src/NSFinance.Api/appsettings.json` (production-safe defaults, no secrets).
- Development-safe config: `apps/api/src/NSFinance.Api/appsettings.Development.json` (no secrets).
- Local secrets (development only): `apps/api/src/NSFinance.Api/appsettings.Local.json` (gitignored).
- Production secrets: Azure App Service settings / Key Vault only.
- `appsettings.Production.json` is not required.
- Environment variable override order:
  1. `appsettings.json`
  2. `appsettings.{Environment}.json`
  3. development only: optional `appsettings.Local.json`
  4. environment variables

## Startup and Runtime Behavior
- Swagger UI: enabled only in Development.
- HTTPS/HSTS: enabled outside Development.
- Forwarded headers: enabled for Azure reverse-proxy scenarios.
- Database init defaults:
  - Production-safe defaults in base config (`ApplyMigrationsOnStartup=false`, no demo seeding).
  - Development overrides in `appsettings.Development.json`.

## Mobile API Base URL Configuration
- Single source of truth: `apps/mobile/src/lib/api/config.ts`
- Behavior:
  - `EXPO_PUBLIC_API_BASE_URL` overrides everything.
  - In production builds, default is Azure production API URL.
  - In development builds, default is localhost/emulator values.
- Local mobile env example: `apps/mobile/.env.example`

## Auth and Banking Callback Inventory
- Turnstile hosted register challenge page: `/turnstile/register`
  - Defined in:
    - `apps/api/src/NSFinance.Api/Modules/Auth/AuthModule.cs`
    - `apps/api/src/NSFinance.Api/Modules/Auth/Endpoints/TurnstileRegisterPageEndpoint.cs`
  - Mobile app loads this page in a WebView and passes `siteKey` via query string.
  - The hostname serving this page must be authorized in Cloudflare Turnstile hostnames.
- Backend Google callback route (scaffold route): `/api/auth/providers/google/callback`
  - Defined in:
    - `apps/api/src/NSFinance.Api/Modules/Auth/AuthModule.cs`
    - `apps/api/src/NSFinance.Api/Modules/Auth/Services/AuthService.cs`
  - Current behavior: scaffold response (not active OAuth redirect flow).
- TrueLayer provider callback route: `/api/banking/truelayer/callback`
  - Defined in:
    - `apps/api/src/NSFinance.Api/Modules/Banking/BankingModule.cs`
    - `apps/api/src/NSFinance.Api/Modules/Banking/Endpoints/TrueLayerCallbackEndpoint.cs`
- TrueLayer redirect URI source:
  - `TrueLayer__RedirectUri` (preferred) or the alias variable names listed above.
  - Development local default is only in `appsettings.Development.json`.
- Mobile return deep link after TrueLayer callback:
  - `nsfinance://modals/add-account?...`
  - Defined in `apps/api/src/NSFinance.Api/Modules/Banking/Endpoints/TrueLayerCallbackEndpoint.cs`.

## Manual Azure Portal Steps (Not Done in Code)
- Add/update App Service environment variables listed above.
- Store secrets in Key Vault.
- Configure Managed Identity and Key Vault access policy/RBAC.
- Configure custom domains/TLS if needed.
- Register production callback/redirect URIs in:
  - Google OAuth console (mobile client IDs and redirect handling).
  - TrueLayer console (`https://api.finance.nsireland.ie/api/banking/truelayer/callback` exact URI).
- Handle APK/AAB distribution and mobile release rollout.
