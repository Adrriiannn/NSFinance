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
- Open banking integrations (Plaid/TrueLayer/Tink)
- advanced AI reasoning / forecasting
- full budgeting/goals intelligence engine
- PDF import
- microservice split / message broker / Redis
