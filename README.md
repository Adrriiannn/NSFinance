# NSFinance

Ireland-first personal finance companion built as a mobile-first monorepo.

## Stack

- Backend: .NET 10 + ASP.NET Core Web API
- Mobile: Expo React Native + TypeScript + Expo Router
- Worker: .NET 10 Worker Service
- Database: Azure PostgreSQL through EF Core and Npgsql

## Repo Structure

```text
NSFinance
|- apps/
|  |- api/
|  |- mobile/
|  `- worker/
|- libs/
|- docs/
|- tools/
`- NSFinance/
```

## Production-Connected Workstation

1. Install prerequisites: .NET SDK 10.x, Node.js 24.x, pnpm 10.x, Git, VS Code or Visual Studio, Postman, DBeaver, Azure CLI, Android Platform Tools, and the .NET EF tool.
2. Install JavaScript dependencies: `pnpm install`.
3. Copy `apps/api/src/NSFinance.Api/appsettings.Local.example.json` to `apps/api/src/NSFinance.Api/appsettings.Local.json`.
4. Fill `appsettings.Local.json` with production-connected secrets only: Azure PostgreSQL, JWT signing key, TrueLayer live credentials, Google OAuth client IDs, Azure OpenAI, and any provider keys.
5. Run API: `dotnet run --project .\apps\api\src\NSFinance.Api\NSFinance.Api.csproj`.
6. Run mobile: `pnpm --filter @nsfinance/mobile start`.

## Configuration Model

- Shared defaults: `apps/api/src/NSFinance.Api/appsettings.json`.
- Machine-local secrets: `apps/api/src/NSFinance.Api/appsettings.Local.json` only; this file is gitignored.
- Azure/App Service secrets: environment variables and Key Vault-backed app settings.
- TrueLayer is live-only: `TRUELAYER_ENVIRONMENT=live`, live base URLs, and the callback `https://api.finance.nsireland.ie/api/banking/truelayer/callback`.
- The mobile app targets `https://api.finance.nsireland.ie` by default.

## Canonical Docs

- Docs index: `docs/README.md`
- Workstation setup: `docs/setup/production-workstation.md`
- Configuration and secrets: `docs/setup/configuration.md`
- Azure deployment: `docs/deployment/azure-production.md`
- Database migrations: `docs/deployment/database-migrations.md`
- Postman collection: `tools/postman/nsfinance.postman_collection.json`
- DBeaver notes: `tools/dbeaver/README.md`
- API endpoints: `docs/features/api-endpoints.md`
- TrueLayer integration: `docs/features/banking-truelayer.md`
- Manual QA: `docs/testing/manual-qa.md`
- Architecture overview: `docs/architecture/overview.md`
- Auth/privacy/consent: `docs/architecture/auth-privacy.md`
- Obsidian knowledge base: `NSFinance/00 - Start Here.md`
- QA delivery control center: `NSFinance/Project Management/00 - Delivery Control Center.md`

## Notes

- Never commit secrets.
- `EXPO_PUBLIC_*` values are public client config only.
- This project intentionally uses one production-connected runtime configuration while the product is being rebuilt.
