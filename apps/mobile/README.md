# NSFinance Mobile

Expo Router + TypeScript mobile client for NSFinance.

## Implemented in this slice

- auth entry/login/register flow
- auth-gated app shell
- 4 tabs: Dashboard / Accounts / Activity / Planner
- account details screen with persistent tab bar
- add account / add transaction modals
- transaction context modal for planner enrichment fields
- centralized API client (`src/lib/api/*`)
- React Query data hooks + mutation invalidation + optimistic cache reconciliation
- pull-to-refresh and reliable loading/error/empty states
- inactivity logout after 10 minutes
- premium floating tab bar and polished fintech UI primitives
- planner foundation:
  - month-over-month comparison
  - necessities management
  - planning categories
  - suggestions cards
  - AI companion chat shell

## Run locally

1. API base URL behavior:
   - development default auto-detects your Metro host IP for physical devices and uses port `5080`
   - development fallbacks:
     - Android emulator: `http://10.0.2.2:5080`
     - iOS simulator: `http://localhost:5080`
   - production default is Azure API:
     - `https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net`
   - optional override with `EXPO_PUBLIC_API_BASE_URL` in `.env`
2. Install dependencies:
   - `pnpm install`
3. Start Expo:
   - `pnpm --filter @nsfinance/mobile start`

Demo login for local dev:

- Email: `demo@nsfinance.local`
- Password: `Password123!`