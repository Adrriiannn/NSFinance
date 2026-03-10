# NSFinance Architecture

## Monorepo overview

```text
apps/
  api/      ASP.NET Core modular monolith API
  mobile/   Expo React Native app (primary client)
  worker/   .NET worker for future async jobs
libs/
  shared/         cross-cutting constants/primitives
  domain/         reserved for reusable domain abstractions
  infrastructure/ reserved for shared infra abstractions
  connectors/     reserved for external provider connectors
```

## Mobile-first architecture

The mobile app is the first-class client and drives API contract design:

- dashboard provides quick-glance finance summary
- accounts and activity are drill-down flows
- planner provides category/baseline/suggestion structure
- create actions are mobile-first modal flows
- data fetching favors stale-while-revalidate responsiveness

## Backend modular monolith

Single deployable API with internal modules:

- Auth
- Users
- Accounts
- Transactions
- Categories
- Insights (dashboard summary)

Module conventions:

- endpoints
- DTOs
- services
- validators

## Auth foundation

Current implementation:

- email/password register + login
- PBKDF2 password hashing
- JWT access token issuance
- `/api/auth/me` current-user endpoint
- authorization required on finance routes

Local development:

- deterministic seeded demo user (`demo@nsfinance.local`)
- easy path to replace/extend with production auth providers later

## Data layer and behavior

- PostgreSQL + EF Core + Npgsql
- UTC storage for date/timestamps
- signed transaction amounts:
  - income: positive
  - expense: negative
- account balance = opening balance transaction + transaction sum

Development startup:

- ensures schema exists
- applies auth-column compatibility SQL for existing local DBs
- seeds categories, accounts, and sample transactions

## Mobile data architecture

- centralized API client in `src/lib/api`
  - single base URL source
  - timeout handling
  - structured error parsing
- TanStack Query for query/mutation lifecycle
- explicit query keys + mutation invalidation strategy
- optimistic cache updates after account/transaction mutations
- secure auth session persistence via Expo SecureStore
- inactivity timeout and lifecycle-aware session expiry
- planner state provider for necessities, notes, and transaction context annotations

## UX system foundation

- premium blue-glass visual language with tokens
- reusable primitives (`GlassCard`, `PrimaryButton`, `TransactionRow`, etc.)
- floating tab bar
- persistent tab experience on account drill-down
- planner and AI companion shell surfaces
- loading/error/empty/success states on every core screen
- tasteful motion for section/list entry and touch feedback

## Deliberately postponed

- real Google OAuth
- bank integrations (Plaid/TrueLayer/Tink)
- autonomous AI reasoning
- full budgeting/goals automation
- push notifications
- microservices/message broker split
