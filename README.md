# NSFinTech

Ireland-first personal finance companion built as a mobile-first monorepo.

## Stack

- Backend: .NET 10 + ASP.NET Core Web API (modular monolith)
- Mobile: Expo React Native + TypeScript + Expo Router
- Worker: .NET 10 Worker Service
- Database: PostgreSQL
- ORM: EF Core + Npgsql

## Repo structure

```text
NSFinTech
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
- PostgreSQL running locally (`nsfintech` db), or Docker in `infra`

## Environment variables

See `.env.example`.

Important values:

- `NSFINTECH_DB_CONNECTION_STRING`
- `NSFINTECH_JWT_SIGNING_KEY`
- `TRUELAYER_CLIENT_ID`
- `TRUELAYER_CLIENT_SECRET`
- `TRUELAYER_REDIRECT_URI`
- `TRUELAYER_ENVIRONMENT`
- `TRUELAYER_AUTH_BASE_URL`
- `TRUELAYER_API_BASE_URL`
- `EXPO_PUBLIC_API_BASE_URL`
- `ASPNETCORE_ENVIRONMENT`

Default DB:

`Host=localhost;Port=5432;Database=nsfintech;Username=nsfintech;Password=nsfintech_dev_password`

## Run API

```bash
dotnet run --project .\apps\api\src\NSFinTech.Api\NSFinTech.Api.csproj
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

- Email: `demo@nsfintech.local`
- Password: `Password123!`

## Run mobile

```bash
pnpm install
pnpm --filter @nsfintech/mobile start
```

Set `EXPO_PUBLIC_API_BASE_URL` for your runtime target:

- iOS simulator: `http://192.168.0.11:5080`
- Android emulator: `http://10.0.2.2:5080`
- physical device: `http://<YOUR_PC_LAN_IP>:5080`

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
dotnet run --project .\apps\worker\src\NSFinTech.Worker\NSFinTech.Worker.csproj
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
