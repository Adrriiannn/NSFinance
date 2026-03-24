# Configuration And Secrets

This is the canonical source for NSFinance configuration ownership.

## API config files

- `apps/api/src/NSFinance.Api/appsettings.json`
  - shared safe defaults only
- `apps/api/src/NSFinance.Api/appsettings.Development.json`
  - development-only non-secret overrides
- `apps/api/src/NSFinance.Api/appsettings.Local.json`
  - local secrets only (gitignored)
- `apps/api/src/NSFinance.Api/appsettings.Local.example.json`
  - onboarding template for local secret structure

## Local API secrets

Store local secrets only in:

- `apps/api/src/NSFinance.Api/appsettings.Local.json`

Required local secret keys:

- `ConnectionStrings:DefaultConnection`
- `Jwt:SigningKey`
- `TrueLayer:ClientId`
- `TrueLayer:ClientSecret`

Reserved key for upcoming backend wiring:

- `Turnstile:SecretKey`

Optional local override key:

- `GoogleAuth:ClientId`

## Production secrets (Azure)

Production must use Azure App Service settings / Key Vault / environment variables.

Primary keys:

- `ConnectionStrings__DefaultConnection`
- `Jwt__SigningKey`
- `TrueLayer__ClientId`
- `TrueLayer__ClientSecret`
- `TrueLayer__RedirectUri`
- `TrueLayer__Environment`
- `TrueLayer__AuthBaseUrl`
- `TrueLayer__ApiBaseUrl`
- `GoogleAuth__ClientId` (if Google sign-in is enabled)

## Runtime sources

API runtime reads configuration in this order:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `appsettings.Local.json` (development only)
4. environment variables

## Current API consumption status

Actively consumed by runtime:

- database connection string
- JWT options and signing key
- TrueLayer options
- Google auth client id

Not yet bound by runtime services (template-ready only):

- `Turnstile:SecretKey`
- email transport/env constants

## Mobile public config

`EXPO_PUBLIC_*` values are public and bundled into the client app.
Never place private secrets in `EXPO_PUBLIC_*` keys.

Turnstile public key for the register challenge:

- `EXPO_PUBLIC_TURNSTILE_SITE_KEY`
