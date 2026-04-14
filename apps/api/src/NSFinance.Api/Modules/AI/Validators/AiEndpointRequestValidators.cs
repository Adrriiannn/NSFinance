using NSFinance.Api.Modules.AI.DTOs;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Modules.AI.Validators;

public static class AiEndpointRequestValidators
{
    public static Dictionary<string, string[]> ValidateSendChatRequest(
        SendChatMessageRequest request,
        AIIntegrationOptions options)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            errors["message"] = ["Message is required."];
        }
        else if (request.Message.Trim().Length > options.ChatTurns.MaxUserMessageChars)
        {
            errors["message"] = [$"Message exceeds maximum length of {options.ChatTurns.MaxUserMessageChars} characters."];
        }

        if (string.IsNullOrWhiteSpace(request.ClientRequestId))
        {
            errors["clientRequestId"] = ["Client request id is required."];
        }
        else if (request.ClientRequestId.Trim().Length > options.ChatTurns.MaxClientRequestIdLength)
        {
            errors["clientRequestId"] = [$"Client request id exceeds maximum length of {options.ChatTurns.MaxClientRequestIdLength} characters."];
        }

        if (request.CorrelationId is { Length: > 256 })
        {
            errors["correlationId"] = ["Correlation id exceeds maximum length of 256 characters."];
        }

        if (request.RecentTurns is { Count: > 80 })
        {
            errors["recentTurns"] = ["Recent turns exceed maximum allowed count of 80."];
        }

        if (request.Metadata is { Count: > 64 })
        {
            errors["metadata"] = ["Metadata exceeds maximum entry count of 64."];
        }

        if (request.State?.Constraints is { Count: > 32 })
        {
            errors["state.constraints"] = ["State constraints exceed maximum entry count of 32."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateMerchantInvestigationRequest(
        MerchantInvestigationTestRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(request.RawDescriptor))
        {
            errors["rawDescriptor"] = ["Raw descriptor is required."];
        }
        else if (request.RawDescriptor.Trim().Length > 512)
        {
            errors["rawDescriptor"] = ["Raw descriptor exceeds maximum length of 512 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.NormalizedDescriptor) && request.NormalizedDescriptor.Trim().Length > 512)
        {
            errors["normalizedDescriptor"] = ["Normalized descriptor exceeds maximum length of 512 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.TriggerSource) && request.TriggerSource.Trim().Length > 64)
        {
            errors["triggerSource"] = ["Trigger source exceeds maximum length of 64 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.ProviderContext) && request.ProviderContext.Trim().Length > 128)
        {
            errors["providerContext"] = ["Provider context exceeds maximum length of 128 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.CountryHint) && request.CountryHint.Trim().Length > 8)
        {
            errors["countryHint"] = ["Country hint exceeds maximum length of 8 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.Currency) && request.Currency.Trim().Length > 8)
        {
            errors["currency"] = ["Currency exceeds maximum length of 8 characters."];
        }

        if (request.Amount is < -1000000000m or > 1000000000m)
        {
            errors["amount"] = ["Amount is outside supported bounds."];
        }

        if (!request.DryRun)
        {
            errors["dryRun"] = ["Only dry-run merchant investigation is allowed through this endpoint."];
        }

        return errors;
    }
}
