# NSFinance Mobile

Expo Router + TypeScript mobile client for NSFinance.

## Implemented In This Slice

- Auth entry, login, register, reset, and account security flows
- Auth-gated app shell
- Dashboard, Accounts, Activity, and Planner tabs
- Account details screen with persistent tab bar
- Add account and add transaction modals
- Transaction context modal for planner enrichment fields
- Centralized API client under `src/lib/api`
- React Query hooks with mutation invalidation and optimistic cache reconciliation
- Pull-to-refresh and reliable loading, error, and empty states
- Persistent device-bound sessions
- Floating tab bar and fintech UI primitives
- Planner foundation:
  - month-over-month comparison
  - necessities management
  - planning categories
  - suggestions cards
  - AI companion chat shell

## API Target

The app targets:

```text
https://api.finance.nsireland.ie
```

`EXPO_PUBLIC_API_BASE_URL` may override this only when it points to a public production-compatible API host. Localhost, emulator, and private LAN values are ignored by the runtime config helper.

## Run

```powershell
pnpm install
pnpm --filter @nsfinance/mobile start
```

Production Android verification:

```powershell
pnpm android:release:check
```

This runs type-check, lint, all Node-native mobile tests, Expo SDK compatibility,
resolved Expo config, and the APK packaging self-test.

## Public Config

`EXPO_PUBLIC_*` values are bundled into the client app and must never contain private secrets.

Expected public keys:

- `EXPO_PUBLIC_API_BASE_URL=https://api.finance.nsireland.ie`
- `EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID`
- `EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_PROD`
- `EXPO_PUBLIC_TURNSTILE_PAGE_BASE_URL=https://api.finance.nsireland.ie` when the Turnstile host needs to be explicit

## EAS Build Profile

- `production`: installable Android APK targeting the Azure API.
