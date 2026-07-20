#:package Npgsql@10.0.0

using Npgsql;

// Read-only audit of the merchant knowledge growth loop (CAT-001): knowledge
// rows by source and version, promoted business rows (business identities are
// publishable; candidate descriptors that may name people are shown only as
// status/outcome aggregates), and the review-queue shape.

const string connectionVariable = "NSFINANCE_DB_CONNECTION_STRING";
var connectionString = Environment.GetEnvironmentVariable(connectionVariable);

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set {connectionVariable} before running this audit.");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        ApplicationName = "NSFinance.MerchantKnowledgeAudit",
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

    Console.WriteLine("== MerchantKnowledge by source/version ==");
    const string knowledgeSql = """
        SELECT "Source",
               "CharacteristicsVersion",
               COUNT(*) AS rows,
               COUNT(*) FILTER (WHERE "UserId" IS NOT NULL) AS user_scoped
        FROM "MerchantKnowledge"
        GROUP BY "Source", "CharacteristicsVersion"
        ORDER BY "Source", "CharacteristicsVersion"
        """;
    await using (var command = new NpgsqlCommand(knowledgeSql, connection, transaction))
    await using (var reader = await command.ExecuteReaderAsync(timeout.Token))
    {
        while (await reader.ReadAsync(timeout.Token))
        {
            Console.WriteLine(
                $"  source={reader.GetString(0)} version={reader.GetInt32(1)} rows={reader.GetInt64(2)} userScoped={reader.GetInt64(3)}");
        }
    }

    Console.WriteLine("== Promoted AI knowledge (verified businesses) ==");
    const string promotedSql = """
        SELECT "NormalizedPattern", "DisplayName", "TaxonomyDomainId", "TaxonomyCategoryId",
               "TaxonomySubcategoryId", "Confidence", "CreatedUtc"
        FROM "MerchantKnowledge"
        WHERE "Source" = 'ai_investigation'
        ORDER BY "CreatedUtc" DESC
        LIMIT 20
        """;
    await using (var command = new NpgsqlCommand(promotedSql, connection, transaction))
    await using (var reader = await command.ExecuteReaderAsync(timeout.Token))
    {
        var any = false;
        while (await reader.ReadAsync(timeout.Token))
        {
            any = true;
            var sub = reader.IsDBNull(4) ? "-" : reader.GetInt32(4).ToString();
            Console.WriteLine(
                $"  {reader.GetString(0)} | {reader.GetString(1)} | triple={reader.GetInt32(2)}/{reader.GetInt32(3)}/{sub} | confidence={reader.GetDouble(5):0.00} | {reader.GetDateTime(6):HH:mm:ss}Z");
        }

        if (!any)
        {
            Console.WriteLine("  (none yet)");
        }
    }

    Console.WriteLine("== Candidate ledger by status/outcome ==");
    const string candidateSql = """
        SELECT "Status", COALESCE("LastOutcomeCode", '-') AS outcome, COUNT(*) AS rows,
               MAX("AttemptCount") AS max_attempts
        FROM "MerchantKnowledgeCandidates"
        GROUP BY "Status", COALESCE("LastOutcomeCode", '-')
        ORDER BY "Status", outcome
        """;
    await using (var command = new NpgsqlCommand(candidateSql, connection, transaction))
    await using (var reader = await command.ExecuteReaderAsync(timeout.Token))
    {
        var any = false;
        while (await reader.ReadAsync(timeout.Token))
        {
            any = true;
            Console.WriteLine(
                $"  status={reader.GetString(0)} outcome={reader.GetString(1)} rows={reader.GetInt64(2)} maxAttempts={reader.GetInt32(3)}");
        }

        if (!any)
        {
            Console.WriteLine("  (empty)");
        }
    }

    Console.WriteLine("== Evidence rule keys on transactions ==");
    const string evidenceSql = """
        SELECT COALESCE("CategorizationRuleKey", '-') AS rule_key,
               COALESCE("CategorizationCharacteristicsVersion", 0) AS version,
               COUNT(*) AS rows
        FROM "Transactions"
        WHERE "CategorizationRuleKey" IS NOT NULL
        GROUP BY rule_key, version
        ORDER BY rule_key, version
        """;
    await using (var command = new NpgsqlCommand(evidenceSql, connection, transaction))
    await using (var reader = await command.ExecuteReaderAsync(timeout.Token))
    {
        while (await reader.ReadAsync(timeout.Token))
        {
            Console.WriteLine(
                $"  ruleKey={reader.GetString(0)} version={reader.GetInt32(1)} rows={reader.GetInt64(2)}");
        }
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Audit failed: {exception.GetType().Name}: {exception.Message}");
    return 1;
}
