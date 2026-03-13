# Mobile Android Build Guide

This document defines the NSFinance mobile Android build process with strict separation between local development and Azure-backed production builds.

## 1. Prerequisites

- Node.js 20+ (recommended for Expo SDK 54)
- pnpm 10+
- Expo account
- EAS CLI access

Commands:

```bash
pnpm install
pnpm dlx eas-cli login
pnpm dlx eas-cli whoami
```

## 2. Environment strategy

The project uses `apps/mobile/eas.json` profile env injection plus runtime mode checks.

Source of truth:

- API resolver: `apps/mobile/src/lib/api/config.ts`
- Expo config: `apps/mobile/app.config.ts`
- EAS profiles: `apps/mobile/eas.json`

### Development behavior (`__DEV__ = true`)

- Uses `EXPO_PUBLIC_API_BASE_URL` if set.
- Otherwise uses local defaults:
  - Android emulator: `http://10.0.2.2:5080`
  - iOS simulator: `http://localhost:5080`
- Azure API is blocked in dev unless `EXPO_PUBLIC_ALLOW_AZURE_IN_DEV=true`.

### Production behavior (`__DEV__ = false`)

- Uses `EXPO_PUBLIC_API_BASE_URL` or falls back to Azure base URL.
- Local/LAN URLs are rejected and replaced by Azure default.

Azure production API base URL:

`https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net`

## 3. Build profiles

Defined in `apps/mobile/eas.json`:

- `development`
  - Purpose: internal development client
  - Env:
    - `EXPO_PUBLIC_APP_ENV=development`
    - `EXPO_PUBLIC_ALLOW_AZURE_IN_DEV=false`
- `preview`
  - Purpose: internal APK testing against Azure
  - Android output: APK
  - Env:
    - `EXPO_PUBLIC_APP_ENV=preview`
    - `EXPO_PUBLIC_API_BASE_URL=https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net`
- `production`
  - Purpose: production release build against Azure
  - Android output: AAB
  - Env:
    - `EXPO_PUBLIC_APP_ENV=production`
    - `EXPO_PUBLIC_API_BASE_URL=https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net`

## 4. Exact commands

Run commands from repo root.

### Local development (Expo Go)

```bash
pnpm --filter @nsfinance/mobile start
```

Optional local API override (physical phone): set in `apps/mobile/.env`:

```env
EXPO_PUBLIC_APP_ENV=development
EXPO_PUBLIC_API_BASE_URL=http://<your-lan-ip>:5080
EXPO_PUBLIC_ALLOW_AZURE_IN_DEV=false
```

### Local development build (Android dev client)

```bash
pnpm dlx eas-cli build --platform android --profile development --non-interactive
```

### Internal/preview APK against Azure

```bash
pnpm dlx eas-cli build --platform android --profile preview --non-interactive
```

### Production Android build against Azure

```bash
pnpm dlx eas-cli build --platform android --profile production --non-interactive
```

## 5. Manual setup still required

- Google OAuth console:
  - Register Android app package `com.nsfinance.mobile` and SHA certificate fingerprints.
  - Ensure client IDs used by mobile and API verification are aligned.
- TrueLayer console:
  - Register callback URL to backend endpoint:
    - `https://nsfinance-api-auazcjdde0h4bsey.northeurope-01.azurewebsites.net/api/banking/truelayer/callback`
- Azure App Service:
  - Keep production env vars configured (`ConnectionStrings__DefaultConnection`, JWT keys, TrueLayer credentials, Google auth settings).

## 6. Notes

- Never embed API secrets in the mobile app.
- `EXPO_PUBLIC_*` values are public and should only be used for non-secret client config.
- For local TrueLayer sandbox testing, configure backend local TrueLayer settings separately from production Azure settings.
## 7. Preview diagnostics for register/login routing

In preview APK builds, auth API target diagnostics are enabled automatically.

Where diagnostics appear:

- Expo/native log output from API client:
  - `[API ROUTE DIAGNOSTIC]` with `baseUrl`, route, and full request URL.
- Auth error cards (login/register) when a request fails:
  - shows resolved API base URL
  - shows resolved register URL
  - shows resolved login URL

Files:

- `apps/mobile/src/lib/api/diagnostics.ts`
- `apps/mobile/src/lib/api/client.ts`
- `apps/mobile/app/(auth)/register.tsx`
- `apps/mobile/app/(auth)/login.tsx`

To disable later:

- Remove `isPreviewDiagnosticsEnabled` usage from `api/config.ts` and related `authApiRouteDiagnostics` usage.
- Or keep it as-is; it only activates for preview (`EXPO_PUBLIC_APP_ENV=preview` and non-__DEV__ runtime).

