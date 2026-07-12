#:package Npgsql@10.0.0

using System.Net.Sockets;
using System.Security.Authentication;
using Npgsql;

const string connectionVariable = "NSFINANCE_DB_CONNECTION_STRING";
var connectionString = Environment.GetEnvironmentVariable(connectionVariable);

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set {connectionVariable} before running this inspection.");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
NpgsqlConnectionStringBuilder? builder = null;

try
{
    builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        ApplicationName = "NSFinance.MfaStateInspection",
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

    const string summarySql = """
        WITH active_authenticators AS (
            SELECT "UserId", COUNT(*)::bigint AS "AuthenticatorCount"
            FROM "TotpAuthenticators"
            WHERE "VerifiedUtc" IS NOT NULL
              AND "DisabledUtc" IS NULL
            GROUP BY "UserId"
        ),
        active_mfa_users AS (
            SELECT "UserId"
            FROM active_authenticators
        )
        SELECT
            (SELECT COUNT(*)::bigint FROM active_authenticators),
            (SELECT COALESCE(SUM("AuthenticatorCount"), 0)::bigint FROM active_authenticators),
            (
                SELECT COUNT(*)::bigint
                FROM "Users" AS users
                WHERE users."TwoFactorEnabled" IS DISTINCT FROM EXISTS (
                    SELECT 1
                    FROM active_mfa_users AS active
                    WHERE active."UserId" = users."Id"
                )
            ),
            (
                SELECT COUNT(DISTINCT providers."UserId")::bigint
                FROM "UserAuthProviders" AS providers
                JOIN active_mfa_users AS active ON active."UserId" = providers."UserId"
                WHERE providers."ProviderType" = 'local_password'
                  AND providers."IsActive" = TRUE
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_login'
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_login'
                  AND "CreatedUtc" >= timezone('utc', now()) - interval '24 hours'
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_login'
                  AND "ConsumedUtc" IS NOT NULL
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_login'
                  AND "ConsumedUtc" IS NULL
                  AND "SupersededUtc" IS NULL
                  AND "ExpiresUtc" > timezone('utc', now())
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_session_resume'
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_session_resume'
                  AND "CreatedUtc" >= timezone('utc', now()) - interval '24 hours'
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_session_resume'
                  AND "ConsumedUtc" IS NOT NULL
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "IdentityChallenges"
                WHERE "Purpose" = 'mfa_session_resume'
                  AND "ConsumedUtc" IS NULL
                  AND "SupersededUtc" IS NULL
                  AND "ExpiresUtc" > timezone('utc', now())
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "MfaTrustedDevices"
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "MfaTrustedDevices"
                WHERE "RevokedUtc" IS NULL
                  AND "ExpiresUtc" > timezone('utc', now())
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "MfaTrustedDevices"
                WHERE "RevokedUtc" IS NULL
                  AND "ExpiresUtc" <= timezone('utc', now())
            ),
            (
                SELECT COUNT(*)::bigint
                FROM "MfaTrustedDevices" AS trusted
                JOIN "Devices" AS devices ON devices."Id" = trusted."DeviceId"
                WHERE trusted."UserId" <> devices."UserId"
            )
        """;

    await using var command = new NpgsqlCommand(summarySql, connection, transaction);
    await using var reader = await command.ExecuteReaderAsync(timeout.Token);
    await reader.ReadAsync(timeout.Token);

    Console.WriteLine("databaseReachable=true");
    Console.WriteLine($"activeMfaUsers={reader.GetInt64(0)}");
    Console.WriteLine($"activeAuthenticators={reader.GetInt64(1)}");
    Console.WriteLine($"userFlagMismatches={reader.GetInt64(2)}");
    Console.WriteLine($"activePasswordMfaUsers={reader.GetInt64(3)}");
    Console.WriteLine($"mfaLoginChallengesTotal={reader.GetInt64(4)}");
    Console.WriteLine($"mfaLoginChallengesLast24Hours={reader.GetInt64(5)}");
    Console.WriteLine($"mfaLoginChallengesConsumed={reader.GetInt64(6)}");
    Console.WriteLine($"mfaLoginChallengesOpen={reader.GetInt64(7)}");
    Console.WriteLine($"mfaSessionResumeChallengesTotal={reader.GetInt64(8)}");
    Console.WriteLine($"mfaSessionResumeChallengesLast24Hours={reader.GetInt64(9)}");
    Console.WriteLine($"mfaSessionResumeChallengesConsumed={reader.GetInt64(10)}");
    Console.WriteLine($"mfaSessionResumeChallengesOpen={reader.GetInt64(11)}");
    Console.WriteLine($"mfaTrustedDevicesTotal={reader.GetInt64(12)}");
    Console.WriteLine($"mfaTrustedDevicesActive={reader.GetInt64(13)}");
    Console.WriteLine($"mfaTrustedDevicesExpiredUnrevoked={reader.GetInt64(14)}");
    Console.WriteLine($"mfaTrustedDeviceBindingMismatches={reader.GetInt64(15)}");

    await reader.DisposeAsync();
    await transaction.CommitAsync(timeout.Token);
    return 0;
}
catch (Exception exception)
{
    var socketCode = FindInner<SocketException>(exception)?.SocketErrorCode.ToString() ?? "none";
    var authenticationFailure = FindInner<AuthenticationException>(exception) is not null;
    var postgresState = FindInner<PostgresException>(exception)?.SqlState ?? "none";

    Console.Error.WriteLine("databaseReachable=false");
    Console.Error.WriteLine($"failureType={exception.GetType().Name}");
    Console.Error.WriteLine($"socketCode={socketCode}");
    Console.Error.WriteLine($"authenticationFailure={authenticationFailure.ToString().ToLowerInvariant()}");
    Console.Error.WriteLine($"postgresState={postgresState}");
    var azurePostgresTarget = builder?.Host is { } host
        && host.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase);
    Console.Error.WriteLine($"azurePostgresTarget={azurePostgresTarget.ToString().ToLowerInvariant()}");
    return 1;
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
