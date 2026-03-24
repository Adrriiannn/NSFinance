# Local Development

## Prerequisites

- .NET SDK 10.x
- Node.js 22+
- pnpm 10+
- PostgreSQL local instance (or Docker in `infra`)

## API local setup

1. Copy:
   - `apps/api/src/NSFinance.Api/appsettings.Local.example.json`
   - to `apps/api/src/NSFinance.Api/appsettings.Local.json`
2. Fill local secrets:
   - `ConnectionStrings:DefaultConnection`
   - `Jwt:SigningKey`
   - `TrueLayer:ClientId`
   - `TrueLayer:ClientSecret`
3. Run API:

```bash
dotnet run --project .\apps\api\src\NSFinance.Api\NSFinance.Api.csproj
```

Development defaults from `appsettings.Development.json` include:

- local CORS origins
- dev seeding flags
- TrueLayer sandbox redirect/environment/base URLs

## Mobile local setup

1. Install dependencies:

```bash
pnpm install
```

2. In `apps/mobile`, copy `.env.example` to `.env` (if needed).
3. Start mobile:

```bash
pnpm --filter @nsfinance/mobile start
```

API URL behavior:

- iOS simulator default: `http://localhost:5080`
- Android emulator default: `http://10.0.2.2:5080`
- optional override: `EXPO_PUBLIC_API_BASE_URL`

## Local TrueLayer sandbox

1. Ensure local secrets include:
   - `TrueLayer:ClientId`
   - `TrueLayer:ClientSecret`
2. Ensure redirect URI is registered in TrueLayer sandbox:
   - `http://localhost:5080/api/banking/truelayer/callback`

## Demo login

- Email: `demo@nsfinance.local`
- Password: `Password123!`
