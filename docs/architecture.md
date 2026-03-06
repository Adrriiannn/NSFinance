# NSFinTech Architecture

## Monorepo overview

NSFinTech is organized as a monorepo to keep product delivery fast while sharing contracts and conventions across runtime apps.

```text
apps/
  api/      ASP.NET Core modular monolith API
  mobile/   Expo React Native end-user client
  worker/   .NET worker for future background processing
libs/
  shared/         shared constants and cross-cutting primitives
  domain/         future domain abstractions
  infrastructure/ future infra implementations
  connectors/     future provider adapters
```

## Modular monolith backend approach

The backend is intentionally a modular monolith:

- Single deployable API process
- Internal module boundaries by feature area
- Shared persistence/runtime infrastructure
- Low operational complexity in the startup phase

Each module is scaffolded with its own placeholders for:

- endpoints/controllers
- DTOs
- services
- validators

Current module list:

- Auth
- Users
- Accounts
- Transactions
- Imports
- Categories
- Goals
- Insights
- Admin

Only `Users` has a sample endpoint (`GET /api/users`) to verify modular routing.

## Mobile-first architecture

The mobile app is the primary user client from day one:

- Expo + TypeScript foundation optimized for startup speed
- API base URL configurable through environment variables
- Dedicated health screen for quick backend connectivity checks

The backend and worker are structured to support mobile use cases first, while leaving room for future connectors and async processing.

## Persistence baseline

PostgreSQL is the primary store with EF Core + Npgsql.

Configured starter entities:

- User
- FinancialAccount
- Transaction
- TransactionCategory
- ImportJob
- AuditEvent

Connection defaults (local):

- Host: `localhost`
- Port: `5432`
- Database: `nsfintech`
- Username: `nsfintech`
- Password: `nsfintech_dev_password`

Environment variable override:

- `NSFINTECH_DB_CONNECTION_STRING`
