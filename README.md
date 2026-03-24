# NSFinance

Ireland-first personal finance companion built as a mobile-first monorepo.

## Stack

- Backend: .NET 10 + ASP.NET Core Web API (modular monolith)
- Mobile: Expo React Native + TypeScript + Expo Router
- Worker: .NET 10 Worker Service
- Database: PostgreSQL
- ORM: EF Core + Npgsql

## Repo structure

```text
NSFinance
|- apps
|  |- api
|  |- mobile
|  `- worker
|- libs
|  |- shared
|  |- domain
|  |- infrastructure
|  `- connectors
|- docs
|- infra
`- scripts
```

## Local prerequisites

- .NET SDK 10.x
- Node.js 22+
- pnpm 10+
- PostgreSQL running locally (`nsfinance` db), or Docker in `infra`

## API Configuration

### How API config works

- `apps/api/src/NSFinance.Api/appsettings.json`
  - shared safe defaults only (no secrets)
- `apps/api/src/NSFinance.Api/appsettings.Development.json`
  - development-safe non-secret overrides (seeding, local CORS, sandbox redirect defaults)
- `apps/api/src/NSFinance.Api/appsettings.Local.json` (gitignored)
  - the only local secret file for development

Production does not use `appsettings.Local.json`. Production secrets must come from Azure App Service settings / Key Vault / environment variables.

Never commit DB passwords, JWT signing keys, provider client secrets, or any private API keys.

### API keys currently consumed

- Database connection string:
  - local: `ConnectionStrings:DefaultConnection` in `appsettings.Local.json`
  - production: `ConnectionStrings__DefaultConnection` (or `NSFINANCE_DB_CONNECTION_STRING` fallback)
- JWT signing key:
  - local: `Jwt:SigningKey` in `appsettings.Local.json`
  - production: `Jwt__SigningKey` (or `NSFINANCE_JWT_SIGNING_KEY` fallback)
- TrueLayer:
  - local: `TrueLayer:ClientId` and `TrueLayer:ClientSecret` in `appsettings.Local.json`
  - production: `TrueLayer__ClientId` / `TrueLayer__ClientSecret` (or `TRUELAYER_*` fallbacks)
- Google auth:
  - API currently reads `GoogleAuth:ClientId` only.
- Turnstile and email settings:
  - not currently bound by API runtime; `Turnstile:SecretKey` is reserved for upcoming captcha backend wiring.

### Local development

1. Copy `apps/api/src/NSFinance.Api/appsettings.Local.example.json` to `apps/api/src/NSFinance.Api/appsettings.Local.json`.
2. Set local secrets in `appsettings.Local.json`:
   - `ConnectionStrings:DefaultConnection`
   - `Jwt:SigningKey`
   - `TrueLayer:ClientId`
   - `TrueLayer:ClientSecret`
   - `Turnstile:SecretKey` (reserved for captcha backend wiring)
3. Run the API:

```bash
dotnet run --project .\apps\api\src\NSFinance.Api\NSFinance.Api.csproj
```

Development behavior:

- Swagger enabled
- demo data seeded (user/accounts/transactions/categories)
- `/health` remains public
- finance routes protected by JWT auth

Auth endpoints:

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/auth/me`
- `POST /api/auth/logout`

Seeded demo login:

- Email: `demo@nsfinance.local`
- Password: `Password123!`

Local TrueLayer sandbox setup:

1. Put `TrueLayer:ClientId` and `TrueLayer:ClientSecret` in `appsettings.Local.json`.
2. Keep sandbox defaults in `appsettings.Development.json` or override them in `appsettings.Local.json`:
   - `TrueLayer:RedirectUri=http://localhost:5080/api/banking/truelayer/callback`
   - `TrueLayer:Environment=sandbox`
   - `TrueLayer:AuthBaseUrl=https://auth.truelayer-sandbox.com`
   - `TrueLayer:ApiBaseUrl=https://api.truelayer-sandbox.com`
3. Register `http://localhost:5080/api/banking/truelayer/callback` in your TrueLayer sandbox console.

Production TrueLayer callback:

- `https://api.finance.nsireland.ie/api/banking/truelayer/callback`
- Full production checklist: `docs/deployment/azure-production.md`

### Production on Azure

Set production secrets in Azure App Service settings / Key Vault (for example):

- `ConnectionStrings__DefaultConnection`
- `Jwt__SigningKey`
- `TrueLayer__ClientId`
- `TrueLayer__ClientSecret`
- `TrueLayer__RedirectUri`
- `TrueLayer__Environment`
- `TrueLayer__AuthBaseUrl`
- `TrueLayer__ApiBaseUrl`
- `GoogleAuth__ClientId` (if Google login is enabled)

## Run mobile

```bash
pnpm install
pnpm --filter @nsfinance/mobile start
```

Mobile API URL strategy:

- development defaults are local:
  - iOS simulator: `http://localhost:5080`
  - Android emulator: `http://10.0.2.2:5080`
- production default URL: `https://api.finance.nsireland.ie`
- optional override: `EXPO_PUBLIC_API_BASE_URL`
- for local safety, do not set `EXPO_PUBLIC_API_BASE_URL` to production when running Expo dev builds.
- `EXPO_PUBLIC_*` values are public client config only. Never put private API/provider secrets in them.

Implemented mobile slice:

- Auth flow: entry, login, register, auth gating
- Dashboard / Accounts / Activity / Planner tabs
- Account details with persistent floating tab bar
- Add account / add transaction modals
- transaction context modal (category/reason/notes/necessity/merchant hooks)
- React Query caching + invalidation
- optimistic cache reconciliation after mutations
- pull-to-refresh on major list screens
- inactivity auto-logout after 10 minutes
- premium floating tab bar + polished card/form/button system
- planner foundation:
  - month-over-month block
  - necessities baseline
  - category framework
  - suggestions area
  - AI companion chat shell

## Run worker

```bash
dotnet run --project .\apps\worker\src\NSFinance.Worker\NSFinance.Worker.csproj
```

## Not implemented yet

- Real Google OAuth
- additional open banking providers (Plaid/Tink)
- TrueLayer payments and non-phase-2 data scopes
- advanced AI reasoning / forecasting
- full budgeting/goals intelligence engine
- PDF import
- microservice split / message broker / Redis

## Documentation

- Docs index: `docs/README.md`
- Setup: `docs/setup/local-development.md`
- Configuration and secrets: `docs/setup/configuration.md`
- Deployment: `docs/deployment/azure-production.md`
- API endpoints: `docs/features/api-endpoints.md`
- Banking (TrueLayer): `docs/features/banking-truelayer.md`
- Manual QA: `docs/testing/manual-qa.md`
