# NSFinance API

ASP.NET Core modular monolith backend for NSFinance mobile.

## Run

```powershell
dotnet run --project .\src\NSFinance.Api\NSFinance.Api.csproj
```

## Configuration

- Shared defaults: `src/NSFinance.Api/appsettings.json`
- Machine-local secrets: `src/NSFinance.Api/appsettings.Local.json` (gitignored)
- Secret template: `src/NSFinance.Api/appsettings.Local.example.json`
- Azure deployment checklist: `..\..\docs\deployment\azure-production.md`
- TrueLayer callback URI: `https://api.finance.nsireland.ie/api/banking/truelayer/callback`

### Secret Bootstrap

1. Copy `src/NSFinance.Api/appsettings.Local.example.json` to `src/NSFinance.Api/appsettings.Local.json`.
2. Fill production-connected secrets:
   - `ConnectionStrings:DefaultConnection`
   - `Jwt:SigningKey`
   - `TrueLayer:ClientId`
   - `TrueLayer:ClientSecret`
   - `TrueLayer:RedirectUri`
   - `GoogleAuth:WebClientId`
   - `GoogleAuth:AndroidClientIdProd`
   - Azure OpenAI settings under `AI:AzureOpenAI`
   - `Turnstile:SecretKey` when backend verification is wired

### Active Config Keys

- API consumes `ConnectionStrings:DefaultConnection`, `Jwt:SigningKey`, `TrueLayer:*`, and Google auth client IDs from `GoogleAuth:WebClientId` and `GoogleAuth:AndroidClientIdProd`.
- TrueLayer is live-only. The API validates live base URLs and HTTPS callback shape at startup.
- `Turnstile:SecretKey` is template-ready but not yet used at runtime.
- Email env constants exist for provider wiring, but no email transport service is active yet.

## Key Endpoints

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

Finance endpoints require a JWT bearer token.

## Startup Data

- Baseline policy/version records are seeded when `Database:SeedPolicyDataOnStartup` is enabled.
- Database migrations are controlled by `Database:ApplyMigrationsOnStartup`; production deployment should use the migration workflow in `docs/deployment/database-migrations.md`.
