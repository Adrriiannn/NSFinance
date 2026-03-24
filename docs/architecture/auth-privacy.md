# Auth, Privacy, Consent, And Trust

## Identity and provider model

- `Users` is canonical identity
- provider links via `UserAuthProviders`
- password hashes in `PasswordCredentials` (PBKDF2)

## Session and token model

- short-lived JWT access token (`sub`, `sid`, `email`, `role`)
- refresh tokens stored hashed in `SessionRefreshTokens`
- refresh token rotation and replay protection
- server-side session validity checks on authorized requests

## Recovery and verification

- forgot/reset password via single-use expiring `EmailActionToken`
- email verification request/confirm flow scaffold
- successful reset revokes active sessions

## Policy and consent model

- versioned policy documents (`PolicyDocuments`, `PolicyVersions`)
- acceptance records (`PolicyAcceptances`)
- consent state records (`ConsentRecords`)
- audit events for trust/legal actions

## Data rights and support scaffolding

- deletion/export request records
- support request records
- correlation IDs attached for traceability

## Security baseline

- auth-route and callback rate limits
- login abuse lockout tracking
- environment-driven CORS policy
- security headers and correlation middleware

## Provider status

- Google sign-in callback/provider structure exists (scaffold)
- full production OIDC callback/token exchange remains future work
