# Android Production Delivery

NSFinance currently targets Android only and has one EAS build profile: `production`.
The profile builds an installable APK connected to `https://api.finance.nsireland.ie`.

## Toolchain

- Node.js 24.x
- pnpm 10.8.0
- Expo SDK 54
- EAS CLI 20.5.1
- PowerShell 7
- Android Platform Tools for `adb` installation

The EAS cloud build supplies the Android SDK and release-signing integration. A
local Java or Android SDK installation is not required for this cloud path.

## Automated Main-Branch Flow

The workflow is `.github/workflows/main_nsfinance-api.yml` and is named
`NSFinance Production Delivery`.

On a push to `main`, it classifies the changed paths:

1. API or shared backend changes run the full backend suite.
2. Only a green backend suite can publish the API, build and execute the EF
   migration bundle, deploy Azure App Service, and verify `/health`.
3. Mobile or Android-tooling changes run type-check, lint, all Node-native
   mobile tests, Expo SDK/Doctor checks, native/config parity, Expo configuration
   resolution, and the artifact-tooling self-test.
4. If the same push changes API and mobile code, the APK waits for the API
   deployment and health check.
5. The workflow checks the live API before consuming an EAS build credit.
6. EAS produces a production-signed APK. The workflow downloads it, checks that
   it is an APK/ZIP archive, computes SHA-256, writes a redacted manifest, and
   retains all three files as a GitHub Actions artifact for 30 days.

An API-only push does not rebuild an identical APK. The installed app uses the
updated live API immediately. A manual workflow run can request an APK without
requesting an API deployment.

## Required GitHub Configuration

Existing API delivery values remain required:

- Secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID`, and `PROD_DB_CONNECTION_STRING`.
- Repository variable: `AZURE_API_RESOURCE_GROUP`.

Android delivery also requires the secret `EXPO_TOKEN`. The token must belong to
an Expo account with access to project
`21986a2d-cbfa-4757-bf6d-04eb6aa4f197`. Never place it in source, workflow
inputs, logs, or Obsidian.

Before the first non-interactive CI build, verify the EAS project and managed
Android signing credentials interactively on a trusted workstation. EAS Build
injects the managed release signing configuration during its remote Android
build; the checked-in debug signing block is not production-signing evidence.

## Local Commands

Run from the repository root:

```powershell
pnpm install
pnpm android:release:check
pnpm android:release:build
```

`android:release:build` performs the checks, verifies EAS authentication, waits
for the cloud build, downloads the APK to ignored `local-builds/android`, and
writes its checksum and manifest.

Install the latest locally downloaded APK on one connected, authorized phone:

```powershell
pnpm android:release:install -- -Launch
```

Install a specific APK:

```powershell
pnpm android:release:install -- -ApkPath "C:\path\to\NSFinance.apk" -Launch
```

The installer uses `adb install -r`, which upgrades the existing app only when
the package and signing identity are compatible.

## Artifact Contract

Every successful Android workflow artifact contains:

- `NSFinance-android-<version>-<commit>.apk`
- `<apk-name>.sha256`
- `<apk-name>.manifest.json`

The manifest records the EAS build ID, profile, channel, package, app/runtime
version, source commit, API target, byte length, and checksum. It never records
the temporary EAS download URL, credentials, or user data.

The checked-in native Android project is validated against the production app
config. Camera, microphone, and overlay permissions are explicitly removed;
gallery and foreground-location permissions remain for implemented journeys.

## Faster Compatible Updates

`expo-updates`, the sole `production` channel, and the app-version runtime policy
are configured. Local releases embed `expo-channel-name: production` in app and
native metadata; `Test-AndroidRelease.ps1` rejects missing or divergent channel
configuration. The current app/runtime version is `1.0.3`.

After the first APK is installed and smoke-tested, compatible JavaScript and
bundled-asset changes can use EAS Update instead of rebuilding an APK. This must
not be automated until update-time production values, runtime compatibility,
rollback, and real-device behavior are proven.

Native dependencies, app configuration, permissions, Gradle, Android source,
or signing changes still require a new APK and an intentional app/runtime
version decision.

## Provider Checks

Google OAuth must register package `com.nsfinance.mobile` with the SHA
fingerprint of the EAS-managed signing certificate. TrueLayer keeps the live
callback `https://api.finance.nsireland.ie/api/banking/truelayer/callback`.

The preferred mobile bank return is
`nsfinance://accounts/connect-bank?...`; the legacy modal path remains supported
until verified HTTPS App Links are implemented and proven.
