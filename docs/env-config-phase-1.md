# Phase 1 Environment and Config

## Required Environment Variables
- `NSFINTECH_DB_CONNECTION_STRING`
  - PostgreSQL connection string for API database.
- `NSFINTECH_JWT_SIGNING_KEY`
  - JWT signing key (minimum 32 chars).
- `NSFINTECH_ALLOWED_CORS_ORIGINS`
  - Comma-separated allowed origins for API CORS.
- `NSFINTECH_GOOGLE_CLIENT_ID`
  - Google OIDC client id (scaffold, optional in Phase 1).
- `NSFINTECH_GOOGLE_CLIENT_SECRET`
  - Google OIDC client secret (scaffold, optional in Phase 1).
- `NSFINTECH_GOOGLE_REDIRECT_URI`
  - Google callback URI (scaffold, optional in Phase 1).
- `NSFINTECH_EMAIL_SENDER_ADDRESS`
  - Sender address used by email workflow scaffold.
- `NSFINTECH_EMAIL_TRANSPORT_MODE`
  - Email transport mode (for example `log-only` in local/dev).
- `EXPO_PUBLIC_API_BASE_URL`
  - Mobile app API base URL.

## AppSettings Keys
- `Database:ApplyMigrationsOnStartup`
- `Database:SeedDemoDataOnStartup`
- `Database:SeedPolicyDataOnStartup`
- `Cors:AllowedOrigins`
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:SigningKey`
- `Jwt:AccessTokenMinutes`
- `Jwt:RefreshTokenDays`
- `Jwt:PasswordResetTokenMinutes`
- `Jwt:EmailVerificationTokenMinutes`
- `Jwt:MaxFailedLoginAttempts`
- `Jwt:FailedLoginWindowMinutes`
- `Jwt:LoginLockoutMinutes`

## Security Notes
- No production secrets should be committed into source.
- JWT signing key and OAuth secrets must come from environment/secret store.
- Separate env values for local/test/staging/production are required for DB, CORS, JWT, and provider config.
