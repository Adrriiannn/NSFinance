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

## API environment behavior

- Development runtime (`__DEV__ = true`):
  - Uses `EXPO_PUBLIC_API_BASE_URL` when set.
  - Falls back to local defaults:
    - Android emulator: `http://10.0.2.2:5080`
    - iOS simulator: `http://localhost:5080`
  - Prevents accidental Azure usage unless `EXPO_PUBLIC_ALLOW_AZURE_IN_DEV=true`.
  - Turnstile challenge page can be pinned to Azure with `EXPO_PUBLIC_TURNSTILE_PAGE_BASE_URL` (recommended).
- Production runtime (`__DEV__ = false`):
  - Uses Azure API by default:
    - `https://api.finance.nsireland.ie`
  - Ignores local/LAN API URLs if they are accidentally provided.

## Run locally

1. Install dependencies:
   - `pnpm install`
2. Configure local env:
   - copy `.env.example` to `.env`
   - set `EXPO_PUBLIC_API_BASE_URL` only if testing on a physical device
   - set `EXPO_PUBLIC_TURNSTILE_PAGE_BASE_URL=https://api.finance.nsireland.ie` to keep Turnstile host consistent across dev/prod
3. Start Expo:
   - `pnpm --filter @nsfinance/mobile start`

## EAS build profiles

- `development`: internal dev client build, local-development environment mode.
- `preview`: internal APK build targeting Azure API.
- `production`: release AAB build targeting Azure API.

Demo login for local dev:

- Email: `demo@nsfinance.local`
- Password: `Password123!`
