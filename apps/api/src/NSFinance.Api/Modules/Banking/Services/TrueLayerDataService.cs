using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
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
                    GetProviderBrandingString(provider, "icon_uri"),
                    GetProviderBrandingString(provider, "logo_uri"),
                    GetProviderBrandingString(provider, "bg_color"),
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

    public async Task<ServiceResult<TrueLayerProviderBranding?>> GetProviderBrandingAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string providerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return ServiceResult<TrueLayerProviderBranding?>.Ok(null);
        }

        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/providers/{providerId}";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer provider branding providerId={ProviderId} status={StatusCode}",
                providerId,
                response.Error?.StatusCode);
            return ServiceResult<TrueLayerProviderBranding?>.Fail(
                "TrueLayer provider branding request failed.",
                "truelayer_provider_branding_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            var root = document.RootElement;
            var providerNode = root;

            if (root.TryGetProperty("results", out var resultsNode)
                && resultsNode.ValueKind == JsonValueKind.Array
                && resultsNode.GetArrayLength() > 0)
            {
                providerNode = resultsNode[0];
            }
            else if (root.TryGetProperty("results", out var singleResult)
                     && singleResult.ValueKind == JsonValueKind.Object)
            {
                providerNode = singleResult;
            }
            else if (root.TryGetProperty("provider", out var providerWrapper)
                     && providerWrapper.ValueKind == JsonValueKind.Object)
            {
                providerNode = providerWrapper;
            }

            var resolvedProviderId =
                GetProviderBrandingString(providerNode, "provider_id")
                ?? GetProviderBrandingString(providerNode, "id")
                ?? providerId;

            return ServiceResult<TrueLayerProviderBranding?>.Ok(
                new TrueLayerProviderBranding(
                    resolvedProviderId,
                    GetProviderBrandingString(providerNode, "display_name"),
                    GetProviderBrandingString(providerNode, "icon_uri"),
                    GetProviderBrandingString(providerNode, "logo_uri"),
                    GetProviderBrandingString(providerNode, "bg_color")));
        }
        catch (JsonException)
        {
            return ServiceResult<TrueLayerProviderBranding?>.Fail(
                "TrueLayer provider branding response could not be parsed.",
                "truelayer_provider_branding_payload_invalid",
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
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/accounts/{accountId}/transactions";
        var query = new Dictionary<string, string?>();

        if (fromUtc.HasValue)
        {
            query["from"] = fromUtc.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        if (toUtc.HasValue)
        {
            query["to"] = toUtc.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        if (query.Count > 0)
        {
            endpoint = QueryHelpers.AddQueryString(endpoint, query);
        }

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
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedOffset))
        {
            return parsedOffset.UtcDateTime;
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDateTime))
        {
            return parsedDateTime.ToUniversalTime();
        }

        return null;
    }

    private static string ComputeDedupeKey(string stableTransactionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stableTransactionId));
        return Convert.ToHexString(bytes);
    }

    private static string? GetProviderBrandingString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null || property.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var value = property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
