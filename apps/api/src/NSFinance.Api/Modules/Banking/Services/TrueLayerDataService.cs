using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.Services.Models;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class TrueLayerDataService(
    TrueLayerHttpClient httpClient,
    ILogger<TrueLayerDataService> logger)
{
    public async Task<ServiceResult<IReadOnlyList<TrueLayerAccountRecord>>> GetAccountsAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/accounts";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer accounts status={StatusCode}",
                response.Error?.StatusCode);
            return ServiceResult<IReadOnlyList<TrueLayerAccountRecord>>.Fail(
                "TrueLayer accounts request failed.",
                "truelayer_accounts_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array)
            {
                return ServiceResult<IReadOnlyList<TrueLayerAccountRecord>>.Ok([]);
            }

            var records = new List<TrueLayerAccountRecord>();
            foreach (var item in resultsNode.EnumerateArray())
            {
                var accountId = GetString(item, "account_id");
                if (string.IsNullOrWhiteSpace(accountId))
                {
                    continue;
                }

                var provider = item.TryGetProperty("provider", out var providerNode) ? providerNode : default;
                var accountNumberMetadata = item.TryGetProperty("account_number", out var accountNumberNode)
                    ? accountNumberNode.GetRawText()
                    : null;

                records.Add(new TrueLayerAccountRecord(
                    accountId,
                    GetString(item, "display_name") ?? "Linked bank account",
                    (GetString(item, "currency") ?? "EUR").ToUpperInvariant(),
                    GetString(item, "account_type"),
                    GetString(item, "account_sub_type"),
                    GetString(provider, "provider_id"),
                    GetString(provider, "display_name"),
                    accountNumberMetadata,
                    item.GetRawText()));
            }

            return ServiceResult<IReadOnlyList<TrueLayerAccountRecord>>.Ok(records);
        }
        catch (JsonException)
        {
            return ServiceResult<IReadOnlyList<TrueLayerAccountRecord>>.Fail(
                "TrueLayer accounts response could not be parsed.",
                "truelayer_accounts_payload_invalid",
                StatusCodes.Status502BadGateway);
        }
    }

    public async Task<ServiceResult<TrueLayerBalanceRecord?>> GetBalanceAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string accountId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/accounts/{accountId}/balance";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer balance accountId={AccountId} status={StatusCode}",
                accountId,
                response.Error?.StatusCode);
            return ServiceResult<TrueLayerBalanceRecord?>.Fail(
                "TrueLayer balance request failed.",
                "truelayer_balance_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array
                || resultsNode.GetArrayLength() == 0)
            {
                return ServiceResult<TrueLayerBalanceRecord?>.Ok(null);
            }

            var first = resultsNode[0];
            var updateTimestamp = ParseDateTime(GetString(first, "update_timestamp")) ?? DateTime.UtcNow;
            return ServiceResult<TrueLayerBalanceRecord?>.Ok(
                new TrueLayerBalanceRecord(
                    GetDecimal(first, "available"),
                    GetDecimal(first, "current"),
                    GetDecimal(first, "overdraft"),
                    (GetString(first, "currency") ?? "EUR").ToUpperInvariant(),
                    updateTimestamp,
                    first.GetRawText()));
        }
        catch (JsonException)
        {
            return ServiceResult<TrueLayerBalanceRecord?>.Fail(
                "TrueLayer balance response could not be parsed.",
                "truelayer_balance_payload_invalid",
                StatusCodes.Status502BadGateway);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TrueLayerTransactionRecord>>> GetTransactionsAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string accountId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/accounts/{accountId}/transactions";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer transactions accountId={AccountId} status={StatusCode}",
                accountId,
                response.Error?.StatusCode);
            return ServiceResult<IReadOnlyList<TrueLayerTransactionRecord>>.Fail(
                "TrueLayer transactions request failed.",
                "truelayer_transactions_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array)
            {
                return ServiceResult<IReadOnlyList<TrueLayerTransactionRecord>>.Ok([]);
            }

            var records = new List<TrueLayerTransactionRecord>();
            foreach (var item in resultsNode.EnumerateArray())
            {
                var timestamp = ParseDateTime(GetString(item, "timestamp")) ?? DateTime.UtcNow;
                var amount = GetDecimal(item, "amount");
                if (!amount.HasValue)
                {
                    continue;
                }

                var description = GetString(item, "description") ?? "Imported transaction";
                var providerTransactionId = GetString(item, "transaction_id");
                var stableTransactionId =
                    GetString(item, "normalised_provider_transaction_id")
                    ?? providerTransactionId
                    ?? $"{timestamp:O}|{amount.Value:0.00}|{description}";

                records.Add(new TrueLayerTransactionRecord(
                    providerTransactionId,
                    amount.Value,
                    (GetString(item, "currency") ?? "EUR").ToUpperInvariant(),
                    timestamp,
                    description,
                    GetString(item, "transaction_type"),
                    GetString(item, "status"),
                    ComputeDedupeKey(stableTransactionId),
                    item.GetRawText()));
            }

            return ServiceResult<IReadOnlyList<TrueLayerTransactionRecord>>.Ok(records);
        }
        catch (JsonException)
        {
            return ServiceResult<IReadOnlyList<TrueLayerTransactionRecord>>.Fail(
                "TrueLayer transactions response could not be parsed.",
                "truelayer_transactions_payload_invalid",
                StatusCodes.Status502BadGateway);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        return DateTime.TryParse(value, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    private static string ComputeDedupeKey(string stableTransactionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stableTransactionId));
        return Convert.ToHexString(bytes);
    }
}
