# Phase 1 Auth Architecture

## User and Provider Model
- `Users` is the canonical identity record.
- Auth methods are normalized in `UserAuthProviders` (`local_password`, `google_oidc` scaffold).
- Password storage is isolated in `PasswordCredentials` (PBKDF2 hash, rehash flag support).

## Session and Token Design
- Access token: short-lived JWT with minimal claims (`sub`, `sid`, `email`, `role`).
- Refresh token: opaque random token stored as SHA-256 hash in `SessionRefreshTokens`.
- Rotation model:
  - each refresh token can be used once
  - use marks current token as `UsedUtc`
  - replacement token is issued and linked
  - replay/reuse revokes the refresh family and sessions
- Session state is persisted in `Sessions` and includes device/app/platform metadata.
- JWT authentication validates DB-backed session state on every authorized request.

## Password and Recovery Flows
- Password policy validation in `PasswordPolicyValidator`.
- Password hashing/verification in `Pbkdf2PasswordHasher`.
- Password reset:
  - request creates single-use expiring `EmailActionToken` (`password_reset`)
  - reset consumes token and updates password hash
  - all sessions revoked on successful reset
- Email verification:
  - request creates single-use expiring `EmailActionToken` (`email_verification`)
  - confirm consumes token and sets `Users.EmailVerified`

## Biometric Stance
- Biometrics are not a server credential.
- `Users.BiometricUnlockEnabled` indicates local-app unlock preference readiness only.
- Device/session architecture supports future secure local credential unlock.

## Google Sign-In
- `google_oidc` provider type and callback endpoints exist.
- Current implementation is scaffolded (not active OIDC token verification in Phase 1).
- Existing user/provider linking model supports future production OIDC completion.
