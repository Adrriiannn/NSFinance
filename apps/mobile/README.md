# NSFinTech Mobile

Expo Router + TypeScript mobile client for NSFinTech.

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

1. Set API URL:
   - iOS simulator: `http://192.168.0.11:5080`
   - Android emulator: `http://10.0.2.2:5080`
   - physical device: `http://<YOUR_PC_LAN_IP>:5080`
2. Install dependencies:
   - `pnpm install`
3. Start Expo:
   - `pnpm --filter @nsfintech/mobile start`

Demo login for local dev:

- Email: `demo@nsfintech.local`
- Password: `Password123!`
