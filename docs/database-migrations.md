# Database Migration Workflow

## Summary

NSFinance uses EF Core migrations that are committed to source control.

- Local development can still create and apply migrations with the normal EF workflow.
- Production schema changes are applied in GitHub Actions with an EF Core migration bundle.
- The API is deployed to Azure App Service only after the migration bundle succeeds.
- Production does not rely on app-startup auto-migrations.

## Local developer workflow

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

Apply migrations locally:

```powershell
dotnet ef database update `
  --project apps/api/src/NSFinance.Api/NSFinance.Api.csproj `
  --startup-project apps/api/src/NSFinance.Api/NSFinance.Api.csproj
```

Recommended local flow:

1. Create the migration.
2. Apply it locally.
3. Run the API and tests against the updated schema.
4. Commit the migration files with the code change.
5. Push to `main`.

## Production CI/CD workflow

The production API workflow is defined in:

- `.github/workflows/main_nsfinance-api.yml`

Deployment order:

1. Restore local .NET tools.
2. Restore and publish the API project.
3. Build an EF Core migration bundle for `apps/api/src/NSFinance.Api/NSFinance.Api.csproj`.
4. Execute the bundle against the production database using a GitHub secret connection string.
5. Deploy the published API artifact to Azure App Service.

If the migration bundle fails, the workflow stops and the API deploy does not continue.

## Required GitHub configuration

### Secrets

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `PROD_DB_CONNECTION_STRING`

### Repository variables

- `AZURE_API_RESOURCE_GROUP`

## Notes on secrets

- `PROD_DB_CONNECTION_STRING` is passed to the migration bundle as a workflow secret.
- Do not hardcode connection strings in the repository.
- Do not print the connection string in workflow logs.
- GitHub Actions masks secret values automatically, but workflow steps should still avoid echoing them.

## Startup migration behavior

`Database:ApplyMigrationsOnStartup` is honored only in `Development`.

This keeps local development convenient while making production schema changes explicit and pipeline-driven.

## If a production migration fails

1. Check the failed GitHub Actions run.
2. Review the migration bundle step output.
3. Fix the migration or the target database issue locally.
4. Create a corrective migration if needed.
5. Push the fix and let CI/CD rerun the migration before deploy.

Do not rely on Azure App Service startup to repair schema drift in production.
