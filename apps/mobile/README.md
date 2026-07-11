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
- Native Android Google account selection through Credential Manager, with ID-token verification and session issuance delegated to the NSFinance API
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
pnpm android:production:check
```

Production Android artifacts:

```powershell
pnpm android:production:apk
pnpm android:production:aab
pnpm android:production:build
```

The release gate runs type-check, lint, all Node-native mobile tests, Expo SDK
compatibility, Expo Doctor, resolved Expo config, native/config assertions, and
artifact signing/provenance verification. Builds run directly through the local
Android Gradle toolchain; hosted EAS Build is not part of delivery.

## Public Config

Values in `runtime.config.json` are bundled into the client app and must never
contain private secrets. The Android Credential Manager flow uses the Google Web
OAuth client ID to obtain an ID token for backend verification. The Android OAuth
client remains registered in Google Cloud by package name and production signing
SHA-1; it is not a client secret.

Expected public production values:

- API and Turnstile base URL: `https://api.finance.nsireland.ie`
- Google Web OAuth client ID
- Google Android production client registration metadata

## Expo Updates

`expo-updates`, the production channel, and the runtime contract remain configured.
OTA publication is a separate controlled release action and is not performed by
the APK/AAB build command.
