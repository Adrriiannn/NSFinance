# API Endpoints

This is the canonical endpoint inventory for NSFinance API.

## System

- `GET /health`

## Auth

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout` (auth)
- `POST /api/auth/logout-all` (auth)
- `GET /api/auth/me` (auth)
- `GET /api/auth/sessions` (auth)
- `DELETE /api/auth/sessions/{sessionId}` (auth)
- `POST /api/auth/forgot-password`
- `POST /api/auth/reset-password`
- `POST /api/auth/verify-email/request`
- `POST /api/auth/verify-email/confirm`
- `POST /api/auth/change-password` (auth)
- `GET /api/auth/providers/google`
- `GET /api/auth/providers/google/callback` (scaffold)

## Users and preferences

- `GET /api/users/profile` (auth)
- `PATCH /api/users/profile` (auth)
- `GET /api/users/preferences` (auth)
- `PATCH /api/users/preferences` (auth)

## Policies, legal, consent

- `GET /api/policies/active`
- `GET /api/legal/terms`
- `GET /api/legal/privacy`
- `GET /api/legal/ai-limitations`
- `GET /api/policies/acceptances` (auth)
- `POST /api/policies/accept` (auth)
- `GET /api/policies/consents` (auth)
- `PUT /api/policies/consents` (auth)

## Support and rights

- `POST /api/support/requests`
- `GET /api/support/requests/me` (auth)
- `POST /api/support/deletion-requests` (auth)
- `POST /api/support/export-requests` (auth)

## Banking (TrueLayer)

- `POST /api/banking/truelayer/link` (auth)
- `GET /api/banking/truelayer/callback`
- `GET /api/banking/connections` (auth)
- `GET /api/banking/accounts` (auth)
- `GET /api/banking/accounts/{accountId}/balances` (auth)
- `GET /api/banking/accounts/{accountId}/transactions?page=1&pageSize=50` (auth)
- `POST /api/banking/connections/{connectionId}/sync` (auth)
- `POST /api/banking/connections/{connectionId}/disconnect` (auth)

## Expense plan community

- `GET /api/expense-tracker/community`
- `GET /api/expense-tracker/community/mine`
- `GET /api/expense-tracker/community/{id}`
- `POST /api/expense-tracker/community/publish`
- `PUT /api/expense-tracker/community/{id}`
- `POST /api/expense-tracker/community/{id}/like`
- `POST /api/expense-tracker/community/{id}/use`
- `POST /api/expense-tracker/community/{id}/report`
- `POST /api/expense-tracker/community/{id}/unpublish`
- `POST /api/expense-tracker/community/{id}/rescan`
