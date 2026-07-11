# Database Migration Workflow

NSFinance uses EF Core migrations committed to source control. Schema changes should be intentional, reviewed, and applied through the deployment workflow before production traffic depends on the new shape.

## Developer Workflow

From the repository root:

```powershell
dotnet tool restore
```

Create a migration:

```powershell
dotnet ef migrations add <MigrationName> `
  --project apps/api/src/NSFinance.Api/NSFinance.Api.csproj `
  --startup-project apps/api/src/NSFinance.Api/NSFinance.Api.csproj
```

Review the generated migration before committing it.

## Production Deployment Workflow

The API workflow is defined in:

```text
.github/workflows/main_nsfinance-api.yml
```

The production delivery workflow classifies changed paths. API and shared
backend changes use this order:

1. Restore .NET tools.
2. Restore the backend solution and run the full Release test suite.
3. Publish the API project only when all tests pass.
4. Build an EF Core migration bundle for `apps/api/src/NSFinance.Api/NSFinance.Api.csproj`.
5. Execute the bundle against the production database using the GitHub secret connection string.
6. Deploy the published API artifact to Azure App Service.
7. Poll `https://api.finance.nsireland.ie/health` and fail the release visibly if it does not recover.

If tests, bundle creation, or migration execution fail, the workflow stops before
API deployment. If a push also requests an Android APK, that APK waits for a
successful API deployment so it is not presented as a complete combined release.

## Required GitHub Configuration

Secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `PROD_DB_CONNECTION_STRING`

Repository variables:

- `AZURE_API_RESOURCE_GROUP`

## Secret Handling

- `PROD_DB_CONNECTION_STRING` is passed to the migration bundle as a workflow secret.
- During bundle execution, GitHub Actions maps that secret into `NSFINANCE_DB_CONNECTION_STRING` and `ConnectionStrings__DefaultConnection`.
- Do not hardcode connection strings in the repository.
- Do not print connection strings in workflow logs.

## Startup Migration Behavior

`Database:ApplyMigrationsOnStartup` exists as an explicit switch, but the preferred production path is the migration bundle. Keep startup migrations off unless there is a deliberate operational reason.

## If A Migration Fails

1. Check the failed GitHub Actions run.
2. Review the migration bundle step output.
3. Fix the migration or target database issue.
4. Create a corrective migration if needed.
5. Push the fix and rerun CI/CD before deploying API changes that depend on the schema.

Never bypass a failing backend test merely to reach the migration step. The
current repeated same-amount transfer test is a tracked release blocker, not an
allowed warning.
