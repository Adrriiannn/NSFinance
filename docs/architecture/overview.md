# NSFinance Architecture

## Monorepo overview

```text
apps/
  api/      ASP.NET Core modular monolith API
  mobile/   Expo React Native app
  worker/   .NET worker
libs/
  shared/         cross-cutting constants/primitives
  domain/         reusable domain abstractions (reserved)
  infrastructure/ shared infra abstractions (reserved)
  connectors/     external provider connectors (reserved)
```

## Backend shape

NSFinance API is a modular monolith with feature modules such as:

- Auth
- Users
- Accounts
- Transactions
- Categories
- Insights
- Banking (TrueLayer)
- Expense plans

Typical per-module organization:

- endpoints
- DTOs
- services
- validators

## Data and runtime

- PostgreSQL + EF Core + Npgsql
- UTC timestamps
- JWT authentication with DB-backed session validation
- rate limiting, security headers, correlation ID middleware

## Mobile architecture

- centralized API client (`apps/mobile/src/lib/api`)
- TanStack Query for data lifecycle
- Expo SecureStore for session persistence
- mobile-first UX and screen flows

## Current boundaries

- API owns auth/session/trust-critical state
- mobile is the primary client
- production deployment is Azure App Service + GitHub Actions
