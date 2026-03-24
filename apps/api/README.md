# NSFinance API

ASP.NET Core modular monolith backend for NSFinance mobile.

## Run

```bash
dotnet run --project .\src\NSFinance.Api\NSFinance.Api.csproj
```

## Configuration

- Shared base config: `src/NSFinance.Api/appsettings.json`
- Development-safe overrides: `src/NSFinance.Api/appsettings.Development.json` (no secrets)
- Local secrets (only path, gitignored): `src/NSFinance.Api/appsettings.Local.json`
- Local template: `src/NSFinance.Api/appsettings.Local.example.json`
- Production should use App Service/Key Vault environment variables (no secrets in source).
- Deployment checklist: `..\..\docs\deployment\azure-production.md`
- TrueLayer callback URIs:
  - Development: `http://localhost:5080/api/banking/truelayer/callback`
  - Production: `https://api.finance.nsireland.ie/api/banking/truelayer/callback`

### Local secret bootstrap

1. Copy `src/NSFinance.Api/appsettings.Local.example.json` to `src/NSFinance.Api/appsettings.Local.json`.
2. Fill local secrets in `appsettings.Local.json`:
   - `ConnectionStrings:DefaultConnection`
   - `Jwt:SigningKey`
   - `TrueLayer:ClientId`
   - `TrueLayer:ClientSecret`
   - `GoogleAuth:WebClientId`
   - `GoogleAuth:AndroidClientIdDebug`
   - `GoogleAuth:AndroidClientIdProd`
   - `Turnstile:SecretKey` (reserved for captcha backend wiring)

### Active config keys

- API consumes `ConnectionStrings:DefaultConnection`, `Jwt:SigningKey`, `TrueLayer:*`, and Google auth client IDs from `GoogleAuth:WebClientId`, `GoogleAuth:AndroidClientIdDebug`, and `GoogleAuth:AndroidClientIdProd` (plus `GoogleAuth:ClientId` as a compatibility alias).
- `Turnstile:SecretKey` is template-ready but not yet used at runtime.
- Email env constants exist for future transport wiring, but no email options binding is active yet.

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
  - `demo@nsfinance.local`
  - `Password123!`
- demo categories/accounts/transactions are seeded
