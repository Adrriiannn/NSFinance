# NSFinance

Ireland-first personal finance companion built as a mobile-first monorepo.

## Stack

- Backend: .NET 10 + ASP.NET Core Web API
- Mobile: Expo React Native + TypeScript + Expo Router
- Worker: .NET 10 Worker Service
- Database: PostgreSQL + EF Core (Npgsql)

## Repo structure

```text
NSFinance
|- apps/
|  |- api/
|  |- mobile/
|  `- worker/
|- libs/
|- docs/
|- infra/
`- scripts/
```

## Quick start

1. Install prerequisites: .NET SDK 10.x, Node.js 22+, pnpm 10+, PostgreSQL.
2. Install JavaScript dependencies: `pnpm install`.
3. Bootstrap local API secrets:
   - copy `apps/api/src/NSFinance.Api/appsettings.Local.example.json`
   - create `apps/api/src/NSFinance.Api/appsettings.Local.json` (gitignored)
   - fill local secrets (`ConnectionStrings:DefaultConnection`, `Jwt:SigningKey`, `TrueLayer:ClientId`, `TrueLayer:ClientSecret`).
4. Run API: `dotnet run --project .\apps\api\src\NSFinance.Api\NSFinance.Api.csproj`
5. Run mobile: `pnpm --filter @nsfinance/mobile start`

## Configuration model

- Local development secrets: `apps/api/src/NSFinance.Api/appsettings.Local.json` only (never committed).
- Shared safe defaults: `apps/api/src/NSFinance.Api/appsettings.json` and non-secret dev overrides in `appsettings.Development.json`.
- Production secrets: Azure App Service settings / Key Vault / environment variables only.

## Canonical docs

- Docs index: `docs/README.md`
- Local setup: `docs/setup/local-development.md`
- Configuration and secrets: `docs/setup/configuration.md`
- Azure production deployment: `docs/deployment/azure-production.md`
- Database migrations / CI bundle workflow: `docs/deployment/database-migrations.md`
- API endpoints: `docs/features/api-endpoints.md`
- TrueLayer integration: `docs/features/banking-truelayer.md`
- Manual QA: `docs/testing/manual-qa.md`
- Architecture overview: `docs/architecture/overview.md`
- Auth/privacy/consent: `docs/architecture/auth-privacy.md`
- Design system: `docs/design/design-system.md`
- Canonical taxonomy: `docs/data/canonical-taxonomy.md`

## Notes

- Never commit secrets to tracked files.
- `EXPO_PUBLIC_*` values are public client config only; do not place private secrets there.
