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

<<<<<<< HEAD
## Environment variables

See `.env.example`.

Important values:

- `NSFINANCE_DB_CONNECTION_STRING`
- `NSFINANCE_ALLOW_REMOTE_DB_IN_DEVELOPMENT`
- `NSFINANCE_JWT_SIGNING_KEY`
- `TRUELAYER_CLIENT_ID`
- `TRUELAYER_CLIENT_SECRET`
- `TRUELAYER_REDIRECT_URI`
- `TRUELAYER_ENVIRONMENT`
- `TRUELAYER_AUTH_BASE_URL`
- `TRUELAYER_API_BASE_URL`
- `EXPO_PUBLIC_API_BASE_URL`
- `ASPNETCORE_ENVIRONMENT`

Default DB:

`Host=localhost;Port=5432;Database=nsfinance;Username=nsfinance`

## Run API

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

1. Copy `apps/api/src/NSFinance.Api/appsettings.Local.example.json` to `apps/api/src/NSFinance.Api/appsettings.Local.json`.
2. Set `TrueLayer:ClientId` and `TrueLayer:ClientSecret` in that local file.
3. Set `TrueLayer:RedirectUri` to your local API callback URL (phone dev example: `http://192.168.0.11:5080/api/banking/truelayer/callback`).
4. Register that redirect URI in your TrueLayer sandbox console.

## Run mobile

```bash
pnpm install
pnpm --filter @nsfinance/mobile start
```

Mobile API URL strategy:
=======
Set `EXPO_PUBLIC_API_BASE_URL` for your runtime target:
>>>>>>> a5e9c2674941c884d3ac97161d995961900ca3c2

- development defaults are local:
  - iOS simulator: `http://localhost:5080`
  - Android emulator: `http://10.0.2.2:5080`
- production default URL: `https://api.finance.nsireland.ie`
- optional override: `EXPO_PUBLIC_API_BASE_URL`
- for local safety, do not set `EXPO_PUBLIC_API_BASE_URL` to production when running Expo dev builds.

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

## Phase 2 docs

- `docs/phase-2-open-banking-truelayer.md`
- `docs/api-endpoints-phase-2.md`
- `docs/env-config-phase-2.md`
- `docs/manual-qa-phase-2.md`
