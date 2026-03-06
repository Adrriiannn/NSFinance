# NSFinTech Monorepo Foundation

NSFinTech is an Ireland-first personal finance companion.  
This repository now contains the initial mobile-first production scaffold only.

## Stack

- Backend API: .NET 10 + ASP.NET Core Web API
- Mobile app: React Native (Expo + TypeScript + Expo Router)
- Worker: .NET 10 Worker Service
- Database: PostgreSQL
- ORM: Entity Framework Core + Npgsql
- Architecture: Modular Monolith (backend)
- Repo style: Monorepo
- Package manager: pnpm (mobile workspace)

## Repository structure

```text
NSFinTech
├─ apps
│  ├─ api
│  │  └─ src/NSFinTech.Api
│  ├─ mobile
│  └─ worker
├─ libs
│  ├─ shared
│  ├─ domain
│  ├─ infrastructure
│  └─ connectors
├─ docs
├─ infra
└─ scripts
```

## Prerequisites

- .NET SDK 10.x
- Node.js 22+ (or compatible with Expo tooling)
- pnpm 10+
- Docker PostgreSQL already running locally with:
  - `POSTGRES_HOST=localhost`
  - `POSTGRES_PORT=5432`
  - `POSTGRES_DB=nsfintech`
  - `POSTGRES_USER=nsfintech`
  - `POSTGRES_PASSWORD=nsfintech_dev_password`

## Configuration

Copy `.env.example` and set values as needed.

Key variables:

- `NSFINTECH_DB_CONNECTION_STRING`
- `EXPO_PUBLIC_API_BASE_URL`
- `ASPNETCORE_ENVIRONMENT`

Default DB connection string in API appsettings:

`Host=localhost;Port=5432;Database=nsfintech;Username=nsfintech;Password=nsfintech_dev_password`

`NSFINTECH_DB_CONNECTION_STRING` overrides appsettings when set.

## Run PostgreSQL

Local PostgreSQL infra is already present at `infra/docker/docker-compose.yml`.

Start it (if not already running):

```bash
docker compose -f .\infra\docker\docker-compose.yml up -d postgres
```

## Run API

```bash
dotnet run --project .\apps\api\src\NSFinTech.Api\NSFinTech.Api.csproj
```

API endpoints:

- `GET /health`
- `GET /api/users` (stub/empty until data exists)

Swagger UI is enabled in Development.

## EF Core migration readiness

EF Core + Npgsql are configured in API project with:

- `AppDbContext`
- Entity configurations
- `IDesignTimeDbContextFactory` (`AppDbContextFactory`)
- Placeholder migrations folder

Example migration command:

```bash
dotnet ef migrations add InitialFoundation --project .\apps\api\src\NSFinTech.Api\NSFinTech.Api.csproj
```

## Run worker

```bash
dotnet run --project .\apps\worker\src\NSFinTech.Worker\NSFinTech.Worker.csproj
```

Worker currently logs startup + heartbeat and includes placeholder folders:

- `Jobs/Imports`
- `Jobs/Sync`
- `Jobs/Insights`

## Run mobile

```bash
pnpm install
pnpm mobile:start
```

or directly:

```bash
pnpm --filter @nsfintech/mobile start
```

Set API URL for your runtime target:

- iOS simulator: `http://192.168.0.11:5080`
- Android emulator: `http://10.0.2.2:5080`
- Physical device: `http://<YOUR_PC_LAN_IP>:5080`

If PowerShell execution policy blocks `pnpm`, use `pnpm.cmd` on Windows.

Mobile screens included:

- `index` (landing)
- `dashboard` (placeholder app shell)
- `health` (calls API `/health`)

## What is scaffolded

- Modular backend folder structure for:
  - Auth
  - Users
  - Accounts
  - Transactions
  - Imports
  - Categories
  - Goals
  - Insights
  - Admin
- Starter entities:
  - User
  - FinancialAccount
  - Transaction
  - TransactionCategory
  - ImportJob
  - AuditEvent
- Shared libs placeholders (`libs/*`)
- Initial docs for architecture and local dev setup

## Intentionally not implemented yet

- Open banking providers (Plaid/TrueLayer/Tink)
- Authentication provider integrations / full JWT auth flow
- AI assistant logic
- Forecasting engine
- PDF parsing/import intelligence
- Redis, message broker, microservices split
- Deployment setup (Azure or otherwise)
