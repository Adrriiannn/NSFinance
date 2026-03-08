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
- `GET /api/auth/me`
- `POST /api/auth/logout`
- `GET /api/accounts`
- `GET /api/accounts/{id}`
- `POST /api/accounts`
- `GET /api/accounts/{id}/transactions`
- `GET /api/transactions`
- `GET /api/transactions/{id}`
- `POST /api/transactions`
- `GET /api/categories`
- `GET /api/dashboard/summary`

All finance endpoints require a JWT bearer token.

## Dev seed behavior

In development startup:

- schema is ensured and auth-compatible columns are patched for local existing DBs
- demo user is seeded with credentials:
  - `demo@nsfintech.local`
  - `Password123!`
- demo categories/accounts/transactions are seeded
