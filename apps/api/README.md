# NSFinTech API

ASP.NET Core modular monolith backend for NSFinTech mobile.

## Run

```bash
dotnet run --project .\src\NSFinTech.Api\NSFinTech.Api.csproj
```

## Key endpoints

- `GET /health` (public)
- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `GET /api/auth/me`
- `POST /api/auth/logout`
- `POST /api/auth/logout-all`
- `GET /api/auth/sessions`
- `DELETE /api/auth/sessions/{sessionId}`
- `POST /api/auth/forgot-password`
- `POST /api/auth/reset-password`
- `POST /api/auth/verify-email/request`
- `POST /api/auth/verify-email/confirm`
- `POST /api/auth/change-password`
- `GET /api/auth/providers/google`
- `GET /api/policies/active`
- `GET /api/policies/acceptances`
- `POST /api/policies/accept`
- `GET /api/policies/consents`
- `PUT /api/policies/consents`
- `GET /api/users/profile`
- `PATCH /api/users/profile`
- `GET /api/users/preferences`
- `PATCH /api/users/preferences`
- `POST /api/support/requests`
- `POST /api/support/deletion-requests`
- `POST /api/support/export-requests`
- `GET /api/accounts`
- `GET /api/accounts/{id}`
- `POST /api/accounts`
- `GET /api/accounts/{id}/transactions`
- `GET /api/transactions`
- `GET /api/transactions/{id}`
- `POST /api/transactions`
- `GET /api/categories`
- `GET /api/dashboard/summary`
- `POST /api/banking/truelayer/link`
- `GET /api/banking/truelayer/callback`
- `GET /api/banking/connections`
- `GET /api/banking/accounts`
- `GET /api/banking/accounts/{accountId}/balances`
- `GET /api/banking/accounts/{accountId}/transactions`
- `POST /api/banking/connections/{connectionId}/sync`
- `POST /api/banking/connections/{connectionId}/disconnect`

All finance endpoints require a JWT bearer token.

## Dev seed behavior

In development startup:

- migrations are applied
- baseline policy/version records are seeded
- demo user is seeded with credentials:
  - `demo@nsfintech.local`
  - `Password123!`
- demo categories/accounts/transactions are seeded
