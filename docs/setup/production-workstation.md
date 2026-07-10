# Production-Connected Workstation

NSFinance currently uses one production-connected setup for day-to-day work. The aim is to keep the app, API, mobile build, TrueLayer, Google OAuth, and Azure resources pointed at the same live configuration while the product is being rebuilt.

## Required Tools

- .NET SDK 10.x
- Node.js 24.x
- pnpm 10.x
- Git
- VS Code or Visual Studio
- Postman
- DBeaver
- Azure CLI
- .NET EF tool: `dotnet tool install --global dotnet-ef`

## Visual Studio Workloads

Install these workloads:

- ASP.NET and web development
- Azure and AI development
- Node.js development

Expo handles the mobile build workflow, so MAUI and C++ mobile workloads are not required for the current setup.

## API Setup

Copy:

```powershell
apps/api/src/NSFinance.Api/appsettings.Local.example.json
```

to:

```powershell
apps/api/src/NSFinance.Api/appsettings.Local.json
```

Fill it with production-connected values:

- Azure PostgreSQL connection string
- JWT signing key
- TrueLayer live client ID, secret, redirect URI, auth base URL, and API base URL
- Google web and Android production client IDs
- Azure OpenAI endpoint, deployment, and key or managed identity settings
- DataProtection key path when an explicit path is needed

## Mobile Setup

The mobile app defaults to:

```text
https://api.finance.nsireland.ie
```

Only add `EXPO_PUBLIC_*` values that are safe to ship in a client bundle. Use `EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_PROD` for Android OAuth.

## Run Commands

```powershell
pnpm install
dotnet run --project .\apps\api\src\NSFinance.Api\NSFinance.Api.csproj
pnpm --filter @nsfinance/mobile start
```

## Guardrails

- Keep `appsettings.Local.json` and `.env` files uncommitted.
- Keep TrueLayer on `live`.
- Keep the mobile API base URL pointed at Azure unless a task explicitly requires diagnosing network behavior.
- Apply schema changes through the migration workflow before relying on updated production data shape.
