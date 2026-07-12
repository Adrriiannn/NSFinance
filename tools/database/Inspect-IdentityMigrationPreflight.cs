#:package Npgsql@10.0.0

using System.Net.Sockets;
using System.Security.Authentication;
using Npgsql;

const string connectionVariable = "NSFINANCE_DB_CONNECTION_STRING";
var connectionString = Environment.GetEnvironmentVariable(connectionVariable);

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set {connectionVariable} before running this preflight.");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
NpgsqlConnectionStringBuilder? builder = null;

try
{
    builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        ApplicationName = "NSFinance.IdentityMigrationPreflight",
        Timeout = 15,
        CommandTimeout = 15
    };

    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync(timeout.Token);
    await using var transaction = await connection.BeginTransactionAsync(timeout.Token);

    await using (var readOnlyCommand = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
    {
        await readOnlyCommand.ExecuteNonQueryAsync(timeout.Token);
    }

    var legacyTableExists = await ScalarAsync<bool>(
        connection,
        transaction,
        "SELECT to_regclass('public.\"EmailActionTokens\"') IS NOT NULL",
        timeout.Token);

    long legacyRows = 0;
    long activeLegacyTokens = 0;

    if (legacyTableExists)
    {
        const string legacyCountsSql = """
            SELECT
                COUNT(*)::bigint,
                COUNT(*) FILTER (
                    WHERE "UsedUtc" IS NULL
                      AND "ExpiresUtc" > timezone('utc', now())
                )::bigint
            FROM "EmailActionTokens"
            """;

        await using var countsCommand = new NpgsqlCommand(legacyCountsSql, connection, transaction);
        await using var reader = await countsCommand.ExecuteReaderAsync(timeout.Token);
        await reader.ReadAsync(timeout.Token);
        legacyRows = reader.GetInt64(0);
        activeLegacyTokens = reader.GetInt64(1);
    }

    var latestMigration = await ScalarAsync<string?>(
        connection,
        transaction,
        "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1",
        timeout.Token);

    await transaction.CommitAsync(timeout.Token);

    Console.WriteLine("databaseReachable=true");
    Console.WriteLine($"legacyTableExists={legacyTableExists.ToString().ToLowerInvariant()}");
    Console.WriteLine($"legacyRows={legacyRows}");
    Console.WriteLine($"activeLegacyTokens={activeLegacyTokens}");
    Console.WriteLine($"latestMigration={latestMigration}");

    return 0;
}
catch (Exception exception)
{
    var innerTypes = new List<string>();
    var current = exception.InnerException;
    while (current is not null && innerTypes.Count < 4)
    {
        innerTypes.Add(current.GetType().Name);
        current = current.InnerException;
    }

    var socketCode = FindInner<SocketException>(exception)?.SocketErrorCode.ToString() ?? "none";
    var authenticationFailure = FindInner<AuthenticationException>(exception) is not null;
    var postgresState = FindInner<PostgresException>(exception)?.SqlState ?? "none";

    Console.Error.WriteLine("databaseReachable=false");
    Console.Error.WriteLine($"failureType={exception.GetType().Name}");
    Console.Error.WriteLine($"innerTypes={string.Join(',', innerTypes)}");
    Console.Error.WriteLine($"socketCode={socketCode}");
    Console.Error.WriteLine($"authenticationFailure={authenticationFailure.ToString().ToLowerInvariant()}");
    Console.Error.WriteLine($"postgresState={postgresState}");
    var azurePostgresTarget = builder?.Host is { } host
        && host.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase);
    Console.Error.WriteLine($"azurePostgresTarget={azurePostgresTarget.ToString().ToLowerInvariant()}");
    Console.Error.WriteLine($"sslMode={builder?.SslMode.ToString() ?? "unknown"}");
    return 1;
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
