# NSFinance Azure Deployment Checklist

## Target Endpoints

- API base URL: `https://api.finance.nsireland.ie`
- Health endpoint: `GET /health`
- TrueLayer callback: `https://api.finance.nsireland.ie/api/banking/truelayer/callback`

## Required Azure App Service Settings

- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__DefaultConnection=<Azure PostgreSQL connection string>`
- `Jwt__SigningKey=<32+ character signing key>`
- `GoogleAuth__WebClientId=<Google OAuth web client id>`
- `GoogleAuth__AndroidClientIdProd=<Google OAuth Android production client id>`
- `TrueLayer__ClientId=<TrueLayer live client id>`
- `TrueLayer__ClientSecret=<TrueLayer live client secret>`
- `TrueLayer__RedirectUri=https://api.finance.nsireland.ie/api/banking/truelayer/callback`
- `TrueLayer__Environment=live`
- `TrueLayer__AuthBaseUrl=https://auth.truelayer.com`
- `TrueLayer__ApiBaseUrl=https://api.truelayer.com`
- `NSFINANCE_DATA_PROTECTION_KEYS_PATH=/home/ASP.NET/DataProtection-Keys`
- `Cors__AllowedOrigins=<comma-separated allowed web origins, if needed>`

Optional reserved keys:

- `Turnstile__SecretKey`
- Email transport settings when provider wiring is implemented

## Environment Variable Aliases

- `NSFINANCE_DB_CONNECTION_STRING`
- `NSFINANCE_JWT_SIGNING_KEY`
- `NSFINANCE_GOOGLE_WEB_CLIENT_ID`
- `NSFINANCE_GOOGLE_ANDROID_CLIENT_ID_PROD`
- `NSFINANCE_GOOGLE_CLIENT_ID`
- `TRUELAYER_CLIENT_ID`
- `TRUELAYER_CLIENT_SECRET`
- `TRUELAYER_REDIRECT_URI`
- `TRUELAYER_ENVIRONMENT`
- `TRUELAYER_AUTH_BASE_URL`
- `TRUELAYER_API_BASE_URL`
- `NSFINANCE_ALLOWED_CORS_ORIGINS`
- `NSFINANCE_DATA_PROTECTION_KEYS_PATH`

## Backend Config Structure

- Base config: `apps/api/src/NSFinance.Api/appsettings.json`
- Machine-local secrets: `apps/api/src/NSFinance.Api/appsettings.Local.json` (gitignored)
- Azure secrets: App Service settings and Key Vault-backed values

Runtime load order:

1. `appsettings.json`
2. `appsettings.Local.json`
3. environment variables

## Startup And Runtime Behavior

- Swagger UI is enabled for the single current API surface.
- HTTPS redirection and HSTS are enabled.
- Forwarded headers are enabled for Azure reverse-proxy scenarios.
- DataProtection keys persist to the configured key-ring path.
- Baseline policy/version records are seeded.
- Demo data seeding and startup migrations are explicit config switches.

## Mobile API Base URL

- Source of truth: `apps/mobile/src/lib/api/config.ts`
- Default: `https://api.finance.nsireland.ie`
- Localhost, emulator, and private LAN values are ignored by the runtime resolver.

## Auth And Banking Callback Inventory

- Turnstile register challenge page: `/turnstile/register`
- Backend Google callback route: `/api/auth/providers/google/callback`
- TrueLayer provider callback route: `/api/banking/truelayer/callback`
- Mobile return deep link after TrueLayer callback:
  - preferred: `nsfinance://accounts/connect-bank?...`
  - legacy supported route: `nsfinance://modals/add-account?...`

TrueLayer market targeting is backend-generated:

- Provider group: `ie-ob-all`
- Country ID: `IE`

## Operational Notes

- TrueLayer options are validated at startup.
- Redirect URI must be an HTTPS absolute URI ending in `/api/banking/truelayer/callback`.
- TrueLayer auth/API base URLs must be the live hosts.
- DataProtection keys must persist across restarts and deployments.
- Initial TrueLayer sync queue is currently in-memory; if the app restarts before the queue drains, users can trigger manual sync from the app.

## Manual Azure Portal Steps

- Add or update App Service settings listed above.
- Store secrets in Key Vault where possible.
- Configure managed identity and Key Vault RBAC.
- Apply database migrations through the CI/CD migration bundle workflow before API changes depend on the new schema.
- Configure custom domains and TLS if needed.
- Register callback/redirect URIs in Google OAuth and TrueLayer.
- Use the production APK for direct Android QA. Deliberately return the same profile to Android App Bundle output only when Play Store distribution begins.
