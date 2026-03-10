# NSFinance Azure Production Checklist

## Target Endpoints
- Production API base URL: `https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net`
- Health endpoint: `GET /health`

## Required Azure App Service Environment Variables
- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__DefaultConnection=<Azure PostgreSQL connection string>`
- `Jwt__SigningKey=<32+ character signing key>`
- `GoogleAuth__ClientId=<Google OAuth client id used by mobile>`
- `TrueLayer__ClientId=<TrueLayer client id>`
- `TrueLayer__ClientSecret=<TrueLayer client secret>`
- `TrueLayer__RedirectUri=https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net/api/banking/truelayer/callback`
- `TrueLayer__Environment=live`
- `TrueLayer__AuthBaseUrl=https://auth.truelayer.com`
- `TrueLayer__ApiBaseUrl=https://api.truelayer.com`
- `Cors__AllowedOrigins=<comma-separated allowed web origins, if needed>`

## Supported Legacy Variable Fallbacks (Compatibility)
- `NSFINANCE_DB_CONNECTION_STRING` (fallback if `ConnectionStrings__DefaultConnection` is not set)
- `NSFINANCE_JWT_SIGNING_KEY` and `NSFINTECH_JWT_SIGNING_KEY` (fallbacks for `Jwt__SigningKey`)
- `NSFINANCE_GOOGLE_CLIENT_ID` and `NSFINTECH_GOOGLE_CLIENT_ID` (fallbacks for `GoogleAuth__ClientId`)
- `TRUELAYER_CLIENT_ID`, `TRUELAYER_CLIENT_SECRET`, `TRUELAYER_REDIRECT_URI`, `TRUELAYER_ENVIRONMENT`, `TRUELAYER_AUTH_BASE_URL`, `TRUELAYER_API_BASE_URL` (fallbacks for `TrueLayer__*`)
- `NSFINANCE_ALLOWED_CORS_ORIGINS` and `NSFINTECH_ALLOWED_CORS_ORIGINS` (fallbacks for `Cors__AllowedOrigins`)

## Backend Config Structure
- Base config: `apps/api/src/NSFinance.Api/appsettings.json` (production-safe defaults, no secrets).
- Local-only config: `apps/api/src/NSFinance.Api/appsettings.Development.json`.
- Environment variable override order:
  1. `appsettings.json`
  2. `appsettings.{Environment}.json`
  3. environment variables

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
  - `TrueLayer__RedirectUri` (preferred) or legacy fallback env vars.
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
  - TrueLayer console (`/api/banking/truelayer/callback` exact URI).
- Handle APK/AAB distribution and mobile release rollout.
