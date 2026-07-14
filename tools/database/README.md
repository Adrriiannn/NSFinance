# Governed Database Audits

These file-based .NET tools perform bounded, aggregate-only database checks.
They do not print connection strings, credentials, user identifiers, emails,
transaction descriptions, or financial amounts.

Set the production connection only in the current process environment and run
the relevant audit from the repository root:

```powershell
$env:NSFINANCE_DB_CONNECTION_STRING = '<approved read-only connection>'
$env:NSFINANCE_EXPECTED_LATEST_MIGRATION = '<latest source migration id>'
dotnet run .\tools\database\Inspect-BankingIntegrity.cs
Remove-Item Env:NSFINANCE_DB_CONNECTION_STRING
Remove-Item Env:NSFINANCE_EXPECTED_LATEST_MIGRATION
```

`Inspect-BankingIntegrity.cs` starts a repeatable-read transaction, marks it
read-only, applies statement and lock timeouts, reports only aggregate evidence,
and rolls the transaction back. A non-zero exit code means the audit could not
establish a clean result:

- `1`: connection, authentication, or query failure;
- `2`: connection environment variable missing;
- `3`: production schema is missing an expected table;
- `4`: one or more critical integrity invariants failed;
- `124`: the internal deadline elapsed.

Never paste an actual connection value into a command transcript, committed
file, Obsidian note, or CI log. Production audit evidence should record only the
redacted key/value output and the identified source revision.
