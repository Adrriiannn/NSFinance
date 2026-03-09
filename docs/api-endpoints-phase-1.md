# Phase 1 API Endpoints

## Auth
- `POST /api/auth/register`
  - body: `{ email, password, displayName, timezone, locale, preferredCurrency, deviceContext? }`
- `POST /api/auth/login`
  - body: `{ email, password, deviceContext? }`
- `POST /api/auth/refresh`
  - body: `{ refreshToken, deviceContext? }`
- `POST /api/auth/logout` (auth required)
- `POST /api/auth/logout-all` (auth required)
- `GET /api/auth/me` (auth required)
- `GET /api/auth/sessions` (auth required)
- `DELETE /api/auth/sessions/{sessionId}` (auth required)
- `POST /api/auth/forgot-password`
  - body: `{ email }`
- `POST /api/auth/reset-password`
  - body: `{ token, newPassword }`
- `POST /api/auth/verify-email/request`
  - body: `{ email }`
- `POST /api/auth/verify-email/confirm`
  - body: `{ token }`
- `POST /api/auth/change-password` (auth required)
  - body: `{ currentPassword, newPassword }`
- `GET /api/auth/providers/google`
- `GET /api/auth/providers/google/callback` (scaffold placeholder)

## User Settings
- `GET /api/users/profile` (auth required)
- `PATCH /api/users/profile` (auth required)
  - body: `{ displayName, timezone, locale, preferredCurrency, onboardingStatus, biometricUnlockEnabled }`
- `GET /api/users/preferences` (auth required)
- `PATCH /api/users/preferences` (auth required)
  - body: `{ adviceTonePreference, digestFrequency, reminderPreference, notificationPreferencesJson, privacyPreferencesJson, essentialCategoryPreferencesJson, futureGoalConfigurationJson }`

## Policies, Legal, Consents
- `GET /api/policies/active`
- `GET /api/legal/terms`
- `GET /api/legal/privacy`
- `GET /api/legal/ai-limitations`
- `GET /api/policies/acceptances` (auth required)
- `POST /api/policies/accept` (auth required)
  - body: `{ policyType, policyVersion, acceptanceContext, platform?, appVersion? }`
- `GET /api/policies/consents` (auth required)
- `PUT /api/policies/consents` (auth required)
  - body: `{ consentType, status, source, metadataJson? }`

## Support and Data Rights
- `POST /api/support/requests`
  - body: `{ category, message }`
- `GET /api/support/requests/me` (auth required)
- `POST /api/support/deletion-requests` (auth required)
  - body: `{ notes? }`
- `POST /api/support/export-requests` (auth required)
  - body: `{ notes? }`
