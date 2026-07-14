# PostgreSQL Restore Rehearsal

This runbook proves that the automated Azure PostgreSQL backup chain can be
restored and inspected without changing the live NSFinance database, API,
App Service connection, DNS, or provider integrations.

## Safety Boundary

- `Plan` and `Status` are read-only Azure operations.
- `Start` creates a separate, billable PostgreSQL Flexible Server. It requires
  both `-AllowMutation` and the exact confirmation emitted by `Plan`.
- `Delete` removes only a server whose name starts with
  `psql-nsfinance-restore-`. It requires both `-AllowMutation` and the exact
  confirmation emitted by `Status`.
- Starting or deleting a restore requires explicit approval for that exact
  resource operation. General project approval is not enough.
- The script is pinned to the enabled `NSIreland-Production` subscription,
  `rg-nsfinance-prod`, and `psql-nsfinance-prod`.
- A rehearsal never performs connection-string cutover. Emergency cutover is a
  separate incident decision with a write freeze and data-loss assessment.

Azure point-in-time restore creates a new server rather than overwriting the
source. The restored compute and storage are billable until the server is
deleted; stopping it does not eliminate storage charges.

## Verified Baseline

The read-only audit on 2026-07-14 found the production server `Ready` on
PostgreSQL 17 with `Standard_B1ms`, 32 GB storage, seven-day retention, seven
retained full backups, geo-redundant backup disabled, and high availability
disabled. Re-run `Plan` immediately before a rehearsal because backup and
restore-point evidence is time-sensitive.

## 1. Produce A Read-Only Plan

From the repository root:

```powershell
pwsh .\tools\azure\Invoke-NsFinancePostgresRestoreRehearsal.ps1 `
  -Action Plan `
  -CommandTimeoutSeconds 60
```

Record only the redacted fields emitted by the script: source state/version,
SKU, storage, retention, retained-backup count, restore time, target name,
target absence, and confirmation token. Do not record subscription IDs,
connection strings, credentials, database rows, or financial data.

## 2. Start The Approved Restore

Use the exact target, timestamp, and confirmation from the same plan. Obtain
explicit approval before running this command:

```powershell
pwsh .\tools\azure\Invoke-NsFinancePostgresRestoreRehearsal.ps1 `
  -Action Start `
  -TargetServerName '<target from Plan>' `
  -RestoreTimeUtc '<restore time from Plan>' `
  -AllowMutation `
  -Confirmation '<exact StartConfirmation from Plan>'
```

`Start` is asynchronous. It creates no App Service, DNS, or provider changes.

## 3. Wait For Readiness

Poll with a bounded command until `State` is `Ready`:

```powershell
pwsh .\tools\azure\Invoke-NsFinancePostgresRestoreRehearsal.ps1 `
  -Action Status `
  -TargetServerName '<target from Plan>'
```

If access is not inherited, add only the smallest approved temporary firewall
rule to the restored server. Never widen the source firewall or enable public
access broadly. Record the temporary rule and remove it during cleanup.

## 4. Run The Read-Only Integrity Audit

Use the approved source credential in memory and override only its host. The
override accepts only an NSFinance restore hostname and is never printed:

```powershell
$env:NSFINANCE_DB_CONNECTION_STRING = '<approved production connection>'
$env:NSFINANCE_DB_HOST_OVERRIDE = '<target from Plan>.postgres.database.azure.com'
$env:NSFINANCE_EXPECTED_LATEST_MIGRATION = '<migration deployed at restore point>'
dotnet run .\tools\database\Inspect-BankingIntegrity.cs
Remove-Item Env:NSFINANCE_DB_CONNECTION_STRING -ErrorAction SilentlyContinue
Remove-Item Env:NSFINANCE_DB_HOST_OVERRIDE -ErrorAction SilentlyContinue
Remove-Item Env:NSFINANCE_EXPECTED_LATEST_MIGRATION -ErrorAction SilentlyContinue
```

Required evidence:

- database reachable and transaction read-only
- expected tables present
- restored latest migration matches the selected restore point
- zero unexplained ownership, duplicate, projection, currency, transfer,
  recurring-record, future-snapshot, or dangling-link defects
- no identifiers, descriptions, amounts, credentials, or row-level data in the
  evidence record

## 5. Delete The Temporary Server

Read `DeleteConfirmation` from `Status`, obtain explicit approval for that exact
target, then delete it:

```powershell
pwsh .\tools\azure\Invoke-NsFinancePostgresRestoreRehearsal.ps1 `
  -Action Delete `
  -TargetServerName '<target from Plan>' `
  -AllowMutation `
  -Confirmation '<exact DeleteConfirmation from Status>'
```

Run `Status` until `Exists` is `false`, remove any restored-server-only firewall
rule, and close the evidence record. Cleanup is part of the rehearsal, not an
optional follow-up.

## Migration Recovery Decisions

| Failure point | Default response |
| --- | --- |
| Tests or migration bundle creation fail | Stop before database or API mutation; repair or withdraw the candidate. |
| Migration execution fails | Keep API deployment stopped, inspect migration history, and prefer a forward corrective migration. Do not assume every failed migration rolled back completely. |
| API smoke check fails after an additive migration | Roll back the API artifact only when the previous artifact is schema-compatible; preserve the additive schema and diagnose. |
| Incompatible or destructive migration corrupts behavior | Quiesce writes, identify a restore point before the migration, restore to a new server, run the integrity audit, quantify writes after the restore point, and obtain an incident cutover decision. |
| Restore validation fails | Do not cut over. Preserve production, collect redacted evidence, delete the temporary server, and repair the recovery plan. |

An emergency database cutover must account for all writes after the selected
restore point. It must rotate or update the approved secret reference through a
separate rollback-ready procedure and verify API health before traffic resumes.

## References

- [Azure PostgreSQL backup and restore concepts](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/concepts-backup-restore)
- [Azure CLI flexible-server restore](https://learn.microsoft.com/en-us/cli/azure/postgres/flexible-server?view=azure-cli-lts)
- [Restore to the latest restore point](https://learn.microsoft.com/en-us/azure/postgresql/backup-restore/how-to-restore-latest-restore-point)
