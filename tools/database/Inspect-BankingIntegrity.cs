#:package Npgsql@10.0.0

using System.Data;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using Npgsql;

const string connectionVariable = "NSFINANCE_DB_CONNECTION_STRING";
const string expectedMigrationVariable = "NSFINANCE_EXPECTED_LATEST_MIGRATION";
const string hostOverrideVariable = "NSFINANCE_DB_HOST_OVERRIDE";

var connectionString = Environment.GetEnvironmentVariable(connectionVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set {connectionVariable} before running this audit.");
    return 2;
}

var expectedLatestMigration = Environment.GetEnvironmentVariable(expectedMigrationVariable);
var hostOverride = Environment.GetEnvironmentVariable(hostOverrideVariable);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
NpgsqlConnectionStringBuilder? builder = null;

try
{
    builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        ApplicationName = "NSFinance.BankingIntegrityAudit",
        Timeout = 10,
        CommandTimeout = 15,
        Pooling = false
    };

    if (!string.IsNullOrWhiteSpace(hostOverride))
    {
        var normalizedHostOverride = hostOverride.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(
                normalizedHostOverride,
                "^psql-nsfinance-restore-[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?\\.postgres\\.database\\.azure\\.com$",
                RegexOptions.CultureInvariant)
            || string.Equals(builder.Host, normalizedHostOverride, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"{hostOverrideVariable} must name a distinct NSFinance restore host.");
            return 2;
        }

        builder.Host = normalizedHostOverride;
        builder.ApplicationName = "NSFinance.RestoreIntegrityAudit";
        Console.WriteLine("hostOverrideApplied=true");
    }

    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync(timeout.Token);
    await using var transaction = await connection.BeginTransactionAsync(
        IsolationLevel.RepeatableRead,
        timeout.Token);

    await ExecuteAsync(connection, transaction, "SET TRANSACTION READ ONLY", timeout.Token);
    await ExecuteAsync(connection, transaction, "SET LOCAL statement_timeout = '15s'", timeout.Token);
    await ExecuteAsync(connection, transaction, "SET LOCAL lock_timeout = '2s'", timeout.Token);
    await ExecuteAsync(
        connection,
        transaction,
        "SET LOCAL idle_in_transaction_session_timeout = '30s'",
        timeout.Token);

    string[] expectedTables =
    [
        "__EFMigrationsHistory",
        "Users",
        "FinancialAccounts",
        "Transactions",
        "OpenBankingConnections",
        "LinkedBankAccounts",
        "BankBalanceSnapshots",
        "RawBankTransactions",
        "NormalizedBankTransactions",
        "TransactionRelationships",
        "BankDirectDebits",
        "BankStandingOrders"
    ];

    var missingTables = new List<string>();
    foreach (var table in expectedTables)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass(@qualified_name) IS NOT NULL",
            connection,
            transaction);
        command.Parameters.AddWithValue("qualified_name", $"public.\"{table}\"");
        var exists = (bool)(await command.ExecuteScalarAsync(timeout.Token) ?? false);
        if (!exists)
        {
            missingTables.Add(table);
        }
    }

    Console.WriteLine("databaseReachable=true");
    Console.WriteLine("auditReadOnly=true");
    Console.WriteLine($"schemaCompatible={(missingTables.Count == 0).ToString().ToLowerInvariant()}");
    Console.WriteLine($"missingTableCount={missingTables.Count}");

    if (missingTables.Count > 0)
    {
        Console.WriteLine($"missingTables={string.Join(',', missingTables.Order())}");
        await transaction.RollbackAsync(timeout.Token);
        return 3;
    }

    var latestMigration = await ScalarAsync<string?>(
        connection,
        transaction,
        "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1",
        timeout.Token);
    var migrationCount = await ScalarAsync<long>(
        connection,
        transaction,
        "SELECT COUNT(*)::bigint FROM \"__EFMigrationsHistory\"",
        timeout.Token);

    Console.WriteLine($"migrationCount={migrationCount}");
    Console.WriteLine($"latestMigration={latestMigration ?? "none"}");
    if (!string.IsNullOrWhiteSpace(expectedLatestMigration))
    {
        Console.WriteLine(
            $"latestMigrationMatchesSource={string.Equals(latestMigration, expectedLatestMigration, StringComparison.Ordinal).ToString().ToLowerInvariant()}");
    }

    const string auditSql = """
        SELECT
            (SELECT COUNT(*) FROM "OpenBankingConnections")::bigint AS "connectionCount",
            (SELECT COUNT(*) FROM "OpenBankingConnections" WHERE "Status" = 'connected')::bigint AS "connectedConnectionCount",
            (SELECT COUNT(*)
               FROM "OpenBankingConnections"
              WHERE "Status" IN ('connected_pending_sync', 'connected', 'sync_pending', 'synced'))::bigint AS "activeConnectionCount",
            (SELECT COUNT(*) FROM "LinkedBankAccounts")::bigint AS "linkedAccountCount",
            (SELECT COUNT(*) FROM "RawBankTransactions")::bigint AS "rawTransactionCount",
            (SELECT COUNT(*) FROM "NormalizedBankTransactions")::bigint AS "normalizedTransactionCount",
            (SELECT COUNT(*) FROM "Transactions")::bigint AS "projectedTransactionCount",
            (SELECT COUNT(*) FROM "TransactionRelationships")::bigint AS "relationshipCount",
            (SELECT COUNT(*)
               FROM "OpenBankingConnections" c
               LEFT JOIN "Users" u ON u."Id" = c."UserId"
              WHERE u."Id" IS NULL)::bigint AS "orphanConnectionCount",
            (SELECT COUNT(*)
               FROM "LinkedBankAccounts" a
               LEFT JOIN "OpenBankingConnections" c ON c."Id" = a."ConnectionId"
              WHERE c."Id" IS NULL)::bigint AS "orphanLinkedAccountCount",
            (SELECT COUNT(*)
               FROM "LinkedBankAccounts" a
               JOIN "OpenBankingConnections" c ON c."Id" = a."ConnectionId"
               JOIN "FinancialAccounts" f ON f."Id" = a."FinancialAccountId"
              WHERE c."UserId" <> f."UserId")::bigint AS "crossUserLinkedAccountCount",
            (SELECT COUNT(*)
               FROM "LinkedBankAccounts"
              WHERE "FinancialAccountId" IS NULL)::bigint AS "linkedAccountWithoutFinancialAccountCount",
            (SELECT COALESCE(SUM(duplicates."row_count" - 1), 0)
               FROM (
                    SELECT COUNT(*)::bigint AS "row_count"
                      FROM "RawBankTransactions"
                     GROUP BY "LinkedBankAccountId", "DedupeKey"
                    HAVING COUNT(*) > 1
               ) duplicates)::bigint AS "duplicateRawDedupeRowCount",
            (SELECT COALESCE(SUM(duplicates."row_count" - 1), 0)
               FROM (
                    SELECT COUNT(*)::bigint AS "row_count"
                      FROM "RawBankTransactions"
                     WHERE "ProviderTransactionId" IS NOT NULL
                     GROUP BY "LinkedBankAccountId", "ProviderTransactionId"
                    HAVING COUNT(*) > 1
               ) duplicates)::bigint AS "duplicateRawProviderRowCount",
            (SELECT COUNT(*)
               FROM "RawBankTransactions" r
               LEFT JOIN "NormalizedBankTransactions" n ON n."RawBankTransactionId" = r."Id"
              WHERE n."Id" IS NULL)::bigint AS "rawWithoutNormalizationCount",
            (SELECT COUNT(*)
               FROM "RawBankTransactions"
              WHERE "ProjectedTransactionId" IS NULL)::bigint AS "rawWithoutProjectionCount",
            (SELECT COUNT(*)
               FROM "RawBankTransactions"
              WHERE "ProjectedTransactionId" IS NULL
                AND lower(COALESCE("TransactionStatus", "ProviderStatus", '')) = 'pending')::bigint AS "rawWithoutProjectionPendingCount",
            (SELECT COUNT(*)
               FROM "RawBankTransactions"
              WHERE "ProjectedTransactionId" IS NULL
                AND lower(COALESCE("TransactionStatus", "ProviderStatus", '')) IN ('reverted', 'reversed'))::bigint AS "rawWithoutProjectionReversedCount",
            (SELECT COUNT(*)
               FROM "RawBankTransactions"
              WHERE "ProjectedTransactionId" IS NULL
                AND lower(COALESCE("TransactionStatus", "ProviderStatus", '')) NOT IN ('pending', 'reverted', 'reversed'))::bigint AS "rawWithoutProjectionUnexpectedCount",
            (SELECT COUNT(*)
               FROM "NormalizedBankTransactions" n
               LEFT JOIN "RawBankTransactions" r ON r."Id" = n."RawBankTransactionId"
              WHERE r."Id" IS NULL)::bigint AS "orphanNormalizedTransactionCount",
            (SELECT COUNT(*)
               FROM "NormalizedBankTransactions" n
               JOIN "RawBankTransactions" r ON r."Id" = n."RawBankTransactionId"
              WHERE n."LinkedBankAccountId" <> r."LinkedBankAccountId")::bigint AS "normalizedAccountMismatchCount",
            (SELECT COUNT(*)
               FROM "NormalizedBankTransactions" n
               JOIN "RawBankTransactions" r ON r."Id" = n."RawBankTransactionId"
              WHERE n."DedupeKey" <> r."DedupeKey"
                 OR n."Amount" <> r."Amount"
                 OR n."Currency" <> r."Currency")::bigint AS "normalizedFinancialMismatchCount",
            (SELECT COUNT(*)
               FROM "RawBankTransactions" r
               JOIN "LinkedBankAccounts" a ON a."Id" = r."LinkedBankAccountId"
               JOIN "Transactions" t ON t."Id" = r."ProjectedTransactionId"
              WHERE a."FinancialAccountId" IS DISTINCT FROM t."FinancialAccountId")::bigint AS "projectionAccountMismatchCount",
            (SELECT COUNT(*)
               FROM "RawBankTransactions" r
               JOIN "LinkedBankAccounts" a ON a."Id" = r."LinkedBankAccountId"
              WHERE r."Currency" <> a."Currency")::bigint AS "rawAccountCurrencyMismatchCount",
            (SELECT COUNT(*)
               FROM "BankBalanceSnapshots" b
               JOIN "LinkedBankAccounts" a ON a."Id" = b."LinkedBankAccountId"
              WHERE b."Currency" <> a."Currency")::bigint AS "balanceAccountCurrencyMismatchCount",
            (SELECT COUNT(*)
               FROM "BankBalanceSnapshots"
              WHERE "CapturedUtc" > timezone('utc', now()) + interval '5 minutes')::bigint AS "futureBalanceSnapshotCount",
            (SELECT COUNT(*)
               FROM "LinkedBankAccounts" a
              WHERE NOT EXISTS (
                    SELECT 1 FROM "BankBalanceSnapshots" b
                     WHERE b."LinkedBankAccountId" = a."Id"
              ))::bigint AS "linkedAccountWithoutBalanceCount",
            (SELECT COUNT(*)
               FROM "LinkedBankAccounts" a
              WHERE NOT EXISTS (
                    SELECT 1 FROM "BankBalanceSnapshots" b
                     WHERE b."LinkedBankAccountId" = a."Id"
                       AND b."CapturedUtc" >= timezone('utc', now()) - interval '24 hours'
              ))::bigint AS "linkedAccountWithStaleBalanceCount",
            (SELECT COUNT(*)
               FROM "OpenBankingConnections"
              WHERE "Status" IN ('connected_pending_sync', 'connected', 'sync_pending', 'synced')
                AND "LastSuccessfulSyncUtc" IS NULL)::bigint AS "connectedWithoutSuccessfulSyncCount",
            (SELECT COUNT(*)
               FROM "OpenBankingConnections"
              WHERE "Status" IN ('connected_pending_sync', 'connected', 'sync_pending', 'synced')
                AND "LastSuccessfulSyncUtc" < timezone('utc', now()) - interval '24 hours')::bigint AS "connectedWithStaleSyncCount",
            (SELECT COUNT(*)
               FROM "Transactions" t
               LEFT JOIN "Transactions" linked ON linked."Id" = t."LinkedTransferTransactionId"
              WHERE t."LinkedTransferTransactionId" IS NOT NULL
                AND linked."Id" IS NULL)::bigint AS "danglingLinkedTransferCount",
            (SELECT COUNT(*)
               FROM "Transactions" t
               JOIN "Transactions" linked ON linked."Id" = t."LinkedTransferTransactionId"
              WHERE linked."LinkedTransferTransactionId" IS DISTINCT FROM t."Id")::bigint AS "asymmetricLinkedTransferCount",
            (SELECT COUNT(*)
               FROM "Transactions" t
               JOIN "Transactions" linked ON linked."Id" = t."LinkedTransferTransactionId"
               JOIN "FinancialAccounts" source_account ON source_account."Id" = t."FinancialAccountId"
               JOIN "FinancialAccounts" target_account ON target_account."Id" = linked."FinancialAccountId"
              WHERE source_account."UserId" <> target_account."UserId")::bigint AS "crossUserLinkedTransferCount",
            (SELECT COUNT(*)
               FROM "Transactions" t
               JOIN "Transactions" linked ON linked."Id" = t."LinkedTransferTransactionId"
              WHERE t."Currency" <> linked."Currency"
                 OR abs(t."Amount") <> abs(linked."Amount")
                 OR sign(t."Amount") = sign(linked."Amount"))::bigint AS "financiallyInvalidLinkedTransferCount",
            (SELECT COUNT(*)
               FROM "TransactionRelationships" r
               LEFT JOIN "Transactions" source_transaction ON source_transaction."Id" = r."SourceTransactionId"
              WHERE source_transaction."Id" IS NULL)::bigint AS "relationshipWithoutSourceCount",
            (SELECT COUNT(*)
               FROM "TransactionRelationships" r
               LEFT JOIN "Transactions" target_transaction ON target_transaction."Id" = r."TargetTransactionId"
              WHERE r."TargetTransactionId" IS NOT NULL
                AND target_transaction."Id" IS NULL)::bigint AS "relationshipWithoutTargetCount",
            (SELECT COUNT(*)
               FROM "TransactionRelationships" r
               JOIN "Transactions" source_transaction ON source_transaction."Id" = r."SourceTransactionId"
               LEFT JOIN "Transactions" target_transaction ON target_transaction."Id" = r."TargetTransactionId"
              WHERE source_transaction."FinancialAccountId" <> r."SourceFinancialAccountId"
                 OR (
                      target_transaction."Id" IS NOT NULL
                      AND target_transaction."FinancialAccountId" IS DISTINCT FROM r."TargetFinancialAccountId"
                 ))::bigint AS "relationshipAccountPointerMismatchCount",
            (SELECT COUNT(*)
               FROM "TransactionRelationships" r
               JOIN "FinancialAccounts" source_account ON source_account."Id" = r."SourceFinancialAccountId"
               JOIN "FinancialAccounts" target_account ON target_account."Id" = r."TargetFinancialAccountId"
              WHERE source_account."UserId" <> target_account."UserId")::bigint AS "crossUserRelationshipCount",
            (SELECT COUNT(*)
               FROM "TransactionRelationships"
              WHERE "TargetTransactionId" = "SourceTransactionId")::bigint AS "selfRelationshipCount",
            (SELECT COALESCE(SUM(duplicates."row_count" - 1), 0)
               FROM (
                    SELECT COUNT(*)::bigint AS "row_count"
                      FROM "BankDirectDebits"
                     GROUP BY "LinkedBankAccountId", "ProviderDirectDebitId"
                    HAVING COUNT(*) > 1
               ) duplicates)::bigint AS "duplicateDirectDebitRowCount",
            (SELECT COUNT(*)
               FROM "BankDirectDebits"
              WHERE "NextPaymentDateUtc" IS NOT NULL
                AND ("NextPaymentAmount" IS NULL OR "NextPaymentCurrency" IS NULL))::bigint AS "incompleteDirectDebitCommitmentCount",
            (SELECT COALESCE(SUM(duplicates."row_count" - 1), 0)
               FROM (
                    SELECT COUNT(*)::bigint AS "row_count"
                      FROM "BankStandingOrders"
                     GROUP BY "LinkedBankAccountId", "ProviderStandingOrderId"
                    HAVING COUNT(*) > 1
               ) duplicates)::bigint AS "duplicateStandingOrderRowCount",
            (SELECT COUNT(*)
               FROM "BankStandingOrders"
              WHERE "NextPaymentDateUtc" IS NOT NULL
                AND ("NextPaymentAmount" IS NULL OR "NextPaymentCurrency" IS NULL))::bigint AS "incompleteStandingOrderCommitmentCount"
        """;

    var metrics = new Dictionary<string, long>(StringComparer.Ordinal);
    await using (var command = new NpgsqlCommand(auditSql, connection, transaction))
    await using (var reader = await command.ExecuteReaderAsync(timeout.Token))
    {
        await reader.ReadAsync(timeout.Token);
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            metrics[reader.GetName(ordinal)] = reader.GetInt64(ordinal);
        }
    }

    foreach (var metric in metrics.OrderBy(x => x.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"{metric.Key}={metric.Value}");
    }

    string[] criticalMetricNames =
    [
        "orphanConnectionCount",
        "orphanLinkedAccountCount",
        "crossUserLinkedAccountCount",
        "duplicateRawDedupeRowCount",
        "duplicateRawProviderRowCount",
        "orphanNormalizedTransactionCount",
        "normalizedAccountMismatchCount",
        "normalizedFinancialMismatchCount",
        "projectionAccountMismatchCount",
        "rawAccountCurrencyMismatchCount",
        "rawWithoutProjectionUnexpectedCount",
        "balanceAccountCurrencyMismatchCount",
        "futureBalanceSnapshotCount",
        "danglingLinkedTransferCount",
        "asymmetricLinkedTransferCount",
        "crossUserLinkedTransferCount",
        "financiallyInvalidLinkedTransferCount",
        "relationshipWithoutSourceCount",
        "relationshipWithoutTargetCount",
        "relationshipAccountPointerMismatchCount",
        "crossUserRelationshipCount",
        "selfRelationshipCount",
        "duplicateDirectDebitRowCount",
        "duplicateStandingOrderRowCount"
    ];

    var criticalDefectCount = criticalMetricNames.Sum(name => metrics[name]);
    Console.WriteLine($"criticalDefectCount={criticalDefectCount}");
    Console.WriteLine($"integrityStatus={(criticalDefectCount == 0 ? "pass" : "fail")}");

    await transaction.RollbackAsync(timeout.Token);
    return criticalDefectCount == 0 ? 0 : 4;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("databaseReachable=unknown");
    Console.Error.WriteLine("failureType=Timeout");
    return 124;
}
catch (Exception exception)
{
    var socketCode = FindInner<SocketException>(exception)?.SocketErrorCode.ToString() ?? "none";
    var authenticationFailure = FindInner<AuthenticationException>(exception) is not null;
    var postgresState = FindInner<PostgresException>(exception)?.SqlState ?? "none";
    var azurePostgresTarget = builder?.Host is { } host
        && host.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase);

    Console.Error.WriteLine("databaseReachable=false");
    Console.Error.WriteLine($"failureType={exception.GetType().Name}");
    Console.Error.WriteLine($"socketCode={socketCode}");
    Console.Error.WriteLine($"authenticationFailure={authenticationFailure.ToString().ToLowerInvariant()}");
    Console.Error.WriteLine($"postgresState={postgresState}");
    Console.Error.WriteLine($"azurePostgresTarget={azurePostgresTarget.ToString().ToLowerInvariant()}");
    return 1;
}

static async Task ExecuteAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string sql,
    CancellationToken cancellationToken)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await command.ExecuteNonQueryAsync(cancellationToken);
}

static async Task<T> ScalarAsync<T>(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string sql,
    CancellationToken cancellationToken)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    var result = await command.ExecuteScalarAsync(cancellationToken);
    if (result is null or DBNull)
    {
        return default!;
    }

    return (T)result;
}

static TException? FindInner<TException>(Exception exception)
    where TException : Exception
{
    Exception? current = exception;
    while (current is not null)
    {
        if (current is TException typed)
        {
            return typed;
        }

        current = current.InnerException;
    }

    return null;
}
