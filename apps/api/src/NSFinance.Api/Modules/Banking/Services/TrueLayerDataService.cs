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
    public async Task<ServiceResult<TrueLayerIdentityInfoRecord?>> GetInfoAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/info";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer info status={StatusCode}",
                response.Error?.StatusCode);
            return ServiceResult<TrueLayerIdentityInfoRecord?>.Fail(
                "TrueLayer info request failed.",
                "truelayer_info_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            var payload = document.RootElement;
            var node = payload;
            if (payload.TryGetProperty("results", out var resultsNode))
            {
                if (resultsNode.ValueKind == JsonValueKind.Array && resultsNode.GetArrayLength() > 0)
                {
                    node = resultsNode[0];
                }
                else if (resultsNode.ValueKind == JsonValueKind.Object)
                {
                    node = resultsNode;
                }
            }

            return ServiceResult<TrueLayerIdentityInfoRecord?>.Ok(
                new TrueLayerIdentityInfoRecord(
                    FullName: GetString(node, "full_name"),
                    Email: GetString(node, "email"),
                    Phone: GetString(node, "phone"),
                    DateOfBirth: GetString(node, "date_of_birth"),
                    RawPayloadJson: node.GetRawText()));
        }
        catch (JsonException)
        {
            return ServiceResult<TrueLayerIdentityInfoRecord?>.Fail(
                "TrueLayer info response could not be parsed.",
                "truelayer_info_payload_invalid",
                StatusCodes.Status502BadGateway);
        }
    }

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

    public async Task<ServiceResult<IReadOnlyList<TrueLayerCardRecord>>> GetCardsAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/cards";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer cards status={StatusCode}",
                response.Error?.StatusCode);
            return ServiceResult<IReadOnlyList<TrueLayerCardRecord>>.Fail(
                "TrueLayer cards request failed.",
                "truelayer_cards_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array)
            {
                return ServiceResult<IReadOnlyList<TrueLayerCardRecord>>.Ok([]);
            }

            var records = new List<TrueLayerCardRecord>();
            foreach (var item in resultsNode.EnumerateArray())
            {
                var cardId = GetString(item, "card_id");
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    continue;
                }

                var cardNumber = item.TryGetProperty("card_number", out var cardNumberNode)
                    ? cardNumberNode
                    : default;
                var cardDetails = item.TryGetProperty("card_details", out var cardDetailsNode)
                    ? cardDetailsNode
                    : default;

                records.Add(new TrueLayerCardRecord(
                    CardId: cardId,
                    DisplayName: GetString(item, "display_name") ?? "Linked card",
                    Currency: (GetString(item, "currency") ?? "EUR").ToUpperInvariant(),
                    ProviderAccountId: GetString(item, "account_id"),
                    CardType: GetString(item, "card_type"),
                    CardNetwork: GetString(item, "card_network"),
                    CardNumberLastFour:
                        GetString(cardNumber, "last_4_digits")
                        ?? GetString(cardDetails, "last_4_digits")
                        ?? GetString(item, "last4"),
                    NameOnCard: GetString(cardDetails, "name_on_card") ?? GetString(item, "name_on_card"),
                    ValidFromUtc: ParseDateTime(GetString(cardDetails, "valid_from")),
                    ValidToUtc: ParseDateTime(GetString(cardDetails, "valid_to")),
                    RawPayloadJson: item.GetRawText()));
            }

            return ServiceResult<IReadOnlyList<TrueLayerCardRecord>>.Ok(records);
        }
        catch (JsonException)
        {
            return ServiceResult<IReadOnlyList<TrueLayerCardRecord>>.Fail(
                "TrueLayer cards response could not be parsed.",
                "truelayer_cards_payload_invalid",
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

    public async Task<ServiceResult<TrueLayerCardBalanceRecord?>> GetCardBalanceAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string cardId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/cards/{cardId}/balance";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer card balance cardId={CardId} status={StatusCode}",
                cardId,
                response.Error?.StatusCode);
            return ServiceResult<TrueLayerCardBalanceRecord?>.Fail(
                "TrueLayer card balance request failed.",
                "truelayer_card_balance_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array
                || resultsNode.GetArrayLength() == 0)
            {
                return ServiceResult<TrueLayerCardBalanceRecord?>.Ok(null);
            }

            var first = resultsNode[0];
            var updateTimestamp = ParseDateTime(GetString(first, "update_timestamp")) ?? DateTime.UtcNow;
            return ServiceResult<TrueLayerCardBalanceRecord?>.Ok(
                new TrueLayerCardBalanceRecord(
                    Available: GetDecimal(first, "available"),
                    Current: GetDecimal(first, "current"),
                    Limit: GetDecimal(first, "credit_limit") ?? GetDecimal(first, "limit"),
                    Outstanding: GetDecimal(first, "outstanding"),
                    Currency: (GetString(first, "currency") ?? "EUR").ToUpperInvariant(),
                    CapturedAtUtc: updateTimestamp,
                    RawPayloadJson: first.GetRawText()));
        }
        catch (JsonException)
        {
            return ServiceResult<TrueLayerCardBalanceRecord?>.Fail(
                "TrueLayer card balance response could not be parsed.",
                "truelayer_card_balance_payload_invalid",
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

                var rawDescription = GetString(item, "description") ?? "Imported transaction";
                var description = ResolveTransactionDisplayDescription(item, rawDescription, "Imported transaction");
                var providerTransactionId = GetString(item, "transaction_id");
                var stableTransactionId =
                    GetString(item, "normalised_provider_transaction_id")
                    ?? providerTransactionId
                    ?? $"{timestamp:O}|{amount.Value:0.00}|{rawDescription}";

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

    public async Task<ServiceResult<IReadOnlyList<TrueLayerCardTransactionRecord>>> GetCardTransactionsAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string cardId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/cards/{cardId}/transactions";
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
                "Failed to fetch TrueLayer card transactions cardId={CardId} status={StatusCode}",
                cardId,
                response.Error?.StatusCode);
            return ServiceResult<IReadOnlyList<TrueLayerCardTransactionRecord>>.Fail(
                "TrueLayer card transactions request failed.",
                "truelayer_card_transactions_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array)
            {
                return ServiceResult<IReadOnlyList<TrueLayerCardTransactionRecord>>.Ok([]);
            }

            var records = new List<TrueLayerCardTransactionRecord>();
            foreach (var item in resultsNode.EnumerateArray())
            {
                var timestamp = ParseDateTime(GetString(item, "timestamp")) ?? DateTime.UtcNow;
                var amount = GetDecimal(item, "amount");
                if (!amount.HasValue)
                {
                    continue;
                }

                var rawDescription = GetString(item, "description") ?? "Imported card transaction";
                var description = ResolveTransactionDisplayDescription(item, rawDescription, "Imported card transaction");
                var providerTransactionId = GetString(item, "transaction_id");
                var stableTransactionId =
                    GetString(item, "normalised_provider_transaction_id")
                    ?? providerTransactionId
                    ?? $"{timestamp:O}|{amount.Value:0.00}|{rawDescription}";

                records.Add(new TrueLayerCardTransactionRecord(
                    ProviderTransactionId: providerTransactionId,
                    Amount: amount.Value,
                    Currency: (GetString(item, "currency") ?? "EUR").ToUpperInvariant(),
                    BookedAtUtc: timestamp,
                    Description: description,
                    TransactionType: GetString(item, "transaction_type"),
                    TransactionStatus: GetString(item, "status"),
                    DedupeKey: ComputeDedupeKey(stableTransactionId),
                    RawPayloadJson: item.GetRawText()));
            }

            return ServiceResult<IReadOnlyList<TrueLayerCardTransactionRecord>>.Ok(records);
        }
        catch (JsonException)
        {
            return ServiceResult<IReadOnlyList<TrueLayerCardTransactionRecord>>.Fail(
                "TrueLayer card transactions response could not be parsed.",
                "truelayer_card_transactions_payload_invalid",
                StatusCodes.Status502BadGateway);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TrueLayerDirectDebitRecord>>> GetDirectDebitsAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string accountId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/accounts/{accountId}/direct_debits";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer direct debits accountId={AccountId} status={StatusCode}",
                accountId,
                response.Error?.StatusCode);
            return ServiceResult<IReadOnlyList<TrueLayerDirectDebitRecord>>.Fail(
                "TrueLayer direct debits request failed.",
                "truelayer_direct_debits_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array)
            {
                return ServiceResult<IReadOnlyList<TrueLayerDirectDebitRecord>>.Ok([]);
            }

            var records = new List<TrueLayerDirectDebitRecord>();
            foreach (var item in resultsNode.EnumerateArray())
            {
                var directDebitId = GetString(item, "direct_debit_id") ?? GetString(item, "id");
                if (string.IsNullOrWhiteSpace(directDebitId))
                {
                    continue;
                }

                records.Add(new TrueLayerDirectDebitRecord(
                    DirectDebitId: directDebitId,
                    Status: GetString(item, "status"),
                    MandateType: GetString(item, "mandate_type"),
                    Reference: GetString(item, "reference"),
                    MerchantName: GetString(item, "name"),
                    PreviousPaymentDateUtc: ParseDateTime(GetString(item, "previous_payment_timestamp") ?? GetString(item, "previous_payment_date")),
                    PreviousPaymentAmount: GetDecimal(item, "previous_payment_amount"),
                    PreviousPaymentCurrency: GetString(item, "previous_payment_currency"),
                    NextPaymentDateUtc: ParseDateTime(GetString(item, "next_payment_timestamp") ?? GetString(item, "next_payment_date")),
                    NextPaymentAmount: GetDecimal(item, "next_payment_amount"),
                    NextPaymentCurrency: GetString(item, "next_payment_currency"),
                    RawPayloadJson: item.GetRawText()));
            }

            return ServiceResult<IReadOnlyList<TrueLayerDirectDebitRecord>>.Ok(records);
        }
        catch (JsonException)
        {
            return ServiceResult<IReadOnlyList<TrueLayerDirectDebitRecord>>.Fail(
                "TrueLayer direct debits response could not be parsed.",
                "truelayer_direct_debits_payload_invalid",
                StatusCodes.Status502BadGateway);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TrueLayerStandingOrderRecord>>> GetStandingOrdersAsync(
        TrueLayerResolvedConfiguration configuration,
        string accessToken,
        string accountId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{configuration.ApiBaseUrl}/data/v1/accounts/{accountId}/standing_orders";
        var response = await httpClient.GetAsync(endpoint, accessToken, cancellationToken);
        if (!response.Succeeded)
        {
            logger.LogWarning(
                "Failed to fetch TrueLayer standing orders accountId={AccountId} status={StatusCode}",
                accountId,
                response.Error?.StatusCode);
            return ServiceResult<IReadOnlyList<TrueLayerStandingOrderRecord>>.Fail(
                "TrueLayer standing orders request failed.",
                "truelayer_standing_orders_fetch_failed",
                response.Error?.StatusCode ?? StatusCodes.Status502BadGateway);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value!);
            if (!document.RootElement.TryGetProperty("results", out var resultsNode)
                || resultsNode.ValueKind != JsonValueKind.Array)
            {
                return ServiceResult<IReadOnlyList<TrueLayerStandingOrderRecord>>.Ok([]);
            }

            var records = new List<TrueLayerStandingOrderRecord>();
            foreach (var item in resultsNode.EnumerateArray())
            {
                var standingOrderId = GetString(item, "standing_order_id") ?? GetString(item, "id");
                if (string.IsNullOrWhiteSpace(standingOrderId))
                {
                    continue;
                }

                var payee = item.TryGetProperty("payee", out var payeeNode)
                    ? payeeNode
                    : default;

                records.Add(new TrueLayerStandingOrderRecord(
                    StandingOrderId: standingOrderId,
                    Status: GetString(item, "status"),
                    Frequency: GetString(item, "frequency"),
                    Reference: GetString(item, "reference"),
                    PayeeName: GetString(payee, "name"),
                    FirstPaymentDateUtc: ParseDateTime(GetString(item, "first_payment_date")),
                    NextPaymentDateUtc: ParseDateTime(GetString(item, "next_payment_date") ?? GetString(item, "next_payment_timestamp")),
                    FinalPaymentDateUtc: ParseDateTime(GetString(item, "final_payment_date")),
                    NextPaymentAmount: GetDecimal(item, "next_payment_amount") ?? GetDecimal(item, "amount"),
                    NextPaymentCurrency: GetString(item, "next_payment_currency") ?? GetString(item, "currency"),
                    PayeeAccountMetadataJson: payee.ValueKind != JsonValueKind.Undefined && payee.ValueKind != JsonValueKind.Null
                        ? payee.GetRawText()
                        : null,
                    RawPayloadJson: item.GetRawText()));
            }

            return ServiceResult<IReadOnlyList<TrueLayerStandingOrderRecord>>.Ok(records);
        }
        catch (JsonException)
        {
            return ServiceResult<IReadOnlyList<TrueLayerStandingOrderRecord>>.Fail(
                "TrueLayer standing orders response could not be parsed.",
                "truelayer_standing_orders_payload_invalid",
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

    private static string ResolveTransactionDisplayDescription(
        JsonElement item,
        string? rawDescription,
        string fallback)
    {
        var preferredCandidates = new[]
        {
            GetString(item, "merchant_name"),
            GetString(item, "normalised_provider_merchant_name"),
            GetString(item, "provider_merchant_name"),
            GetString(item, "display_name"),
            GetNestedString(item, "merchant", "name"),
            GetNestedString(item, "counterparty", "name"),
            GetNestedString(item, "meta", "merchant_name"),
            GetNestedString(item, "meta", "display_name"),
            rawDescription
        };

        foreach (var candidate in preferredCandidates)
        {
            var normalized = NormalizeDisplayLabel(candidate);
            if (normalized is null)
            {
                continue;
            }

            return normalized;
        }

        return fallback;
    }

    private static string? GetNestedString(JsonElement item, string parentPropertyName, string childPropertyName)
    {
        if (!item.TryGetProperty(parentPropertyName, out var parent)
            || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parent, childPropertyName);
    }

    private static string? NormalizeDisplayLabel(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalized = string.Join(" ", candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var hashIndex = normalized.IndexOf('#');
        if (hashIndex > 0 && hashIndex < normalized.Length - 1)
        {
            var suffix = normalized[(hashIndex + 1)..];
            if (suffix.Length >= 4 && suffix.Any(char.IsDigit))
            {
                var trimmed = normalized[..hashIndex].Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    normalized = trimmed;
                }
            }
        }

        return normalized;
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
