using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.DTOs;

namespace NSFinance.Api.Modules.Banking.Services;

internal static class FinancialCommitmentMutationPolicy
{
    private const decimal MaximumAmount = 1_000_000_000_000m;
    private static readonly IReadOnlySet<string> AllowedCadences = new HashSet<string>(StringComparer.Ordinal)
    {
        "weekly",
        "biweekly",
        "monthly",
        "quarterly",
        "yearly",
        "irregular"
    };
    private static readonly IReadOnlySet<string> ResettableFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "account",
        "label",
        "cadence",
        "nextDate",
        "nextAmount",
        "currency",
        "isVariableAmount"
    };

    internal static ServiceError? ValidateManualRequest(
        CreateManualFinancialCommitmentRequest request,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 160)
        {
            return new ServiceError(
                "Label must contain 1 to 160 characters.",
                "commitment_label_invalid",
                StatusCodes.Status400BadRequest);
        }

        var cadence = FinancialCommitmentContractPolicy.NormalizeCadence(request.Cadence);
        if (cadence is not null && !AllowedCadences.Contains(cadence))
        {
            return new ServiceError(
                "Cadence is unsupported.",
                "commitment_cadence_invalid",
                StatusCodes.Status400BadRequest);
        }

        if (request.NextAmount is <= 0m or > MaximumAmount)
        {
            return new ServiceError(
                "Amount must be greater than zero and within range.",
                "commitment_amount_invalid",
                StatusCodes.Status400BadRequest);
        }

        var startsAtUtc = request.StartsAtUtc?.UtcDateTime;
        var endsAtUtc = request.EndsAtUtc?.UtcDateTime;
        var nextDateUtc = request.NextDateUtc?.UtcDateTime;
        if (startsAtUtc.HasValue && endsAtUtc.HasValue && startsAtUtc > endsAtUtc)
        {
            return new ServiceError(
                "Start date cannot be after end date.",
                "commitment_date_range_invalid",
                StatusCodes.Status400BadRequest);
        }

        if (nextDateUtc.HasValue && nextDateUtc.Value < utcNow.AddMinutes(-5))
        {
            return new ServiceError(
                "Next date cannot be in the past.",
                "commitment_next_date_invalid",
                StatusCodes.Status400BadRequest);
        }

        if (nextDateUtc.HasValue && endsAtUtc.HasValue && nextDateUtc > endsAtUtc)
        {
            return new ServiceError(
                "Next date cannot be after end date.",
                "commitment_next_date_invalid",
                StatusCodes.Status400BadRequest);
        }

        return null;
    }

    internal static FinancialCommitmentOverrideBuildResult BuildOverride(
        string? existingJson,
        FinancialCommitmentDecisionRequest request,
        DateTime utcNow,
        FinancialCommitmentOwnedAccount? account)
    {
        var document = UserFinancialCommitmentProjector.DeserializeOverride(existingJson)
            ?? FinancialCommitmentOverrideDocument.Empty;
        var resets = request.ResetFields?
            .Select(field => field?.Trim())
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal)
            ?? [];
        if (resets.Any(field => !ResettableFields.Contains(field)))
        {
            return FinancialCommitmentOverrideBuildResult.Fail(
                "Reset fields contain an unsupported value.",
                "commitment_reset_field_invalid");
        }

        document = Reset(document, resets);
        var conflictingInput = ValidateMutuallyExclusiveInput(request);
        if (conflictingInput is not null)
        {
            return conflictingInput;
        }

        if (request.AccountId.HasValue && account is null)
        {
            return FinancialCommitmentOverrideBuildResult.NotFound();
        }

        if (request.AccountId.HasValue || request.ClearAccount)
        {
            document = document with
            {
                HasAccount = true,
                AccountId = account?.AccountId,
                LinkedBankAccountId = account?.LinkedBankAccountId,
                AccountDisplayName = account?.DisplayName
            };
        }

        if (request.Label is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 160)
            {
                return FinancialCommitmentOverrideBuildResult.Fail(
                    "Label must contain 1 to 160 characters.",
                    "commitment_label_invalid");
            }

            document = document with { HasLabel = true, Label = request.Label.Trim() };
        }

        if (request.Cadence is not null || request.ClearCadence)
        {
            var cadence = request.ClearCadence
                ? null
                : FinancialCommitmentContractPolicy.NormalizeCadence(request.Cadence);
            if (cadence is not null && !AllowedCadences.Contains(cadence))
            {
                return FinancialCommitmentOverrideBuildResult.Fail(
                    "Cadence is unsupported.",
                    "commitment_cadence_invalid");
            }

            document = document with { HasCadence = true, Cadence = cadence };
        }

        if (request.NextDateUtc.HasValue || request.ClearNextDate)
        {
            var nextDateUtc = request.ClearNextDate ? null : request.NextDateUtc?.UtcDateTime;
            if (nextDateUtc.HasValue && nextDateUtc.Value < utcNow.AddMinutes(-5))
            {
                return FinancialCommitmentOverrideBuildResult.Fail(
                    "Next date cannot be in the past.",
                    "commitment_next_date_invalid");
            }

            document = document with { HasNextDate = true, NextDateUtc = nextDateUtc };
        }

        if (request.NextAmount.HasValue || request.ClearNextAmount)
        {
            if (request.NextAmount is <= 0m or > MaximumAmount)
            {
                return FinancialCommitmentOverrideBuildResult.Fail(
                    "Amount must be greater than zero and within range.",
                    "commitment_amount_invalid");
            }

            document = document with
            {
                HasNextAmount = true,
                NextAmount = request.ClearNextAmount ? null : request.NextAmount
            };
        }

        if (request.Currency is not null || request.ClearCurrency)
        {
            var currency = request.ClearCurrency
                ? null
                : FinancialCommitmentContractPolicy.NormalizeCurrency(request.Currency);
            if (currency is not null && !IsValidCurrency(currency))
            {
                return FinancialCommitmentOverrideBuildResult.Fail(
                    "Currency must be a three-letter ISO code.",
                    "commitment_currency_invalid");
            }

            document = document with { HasCurrency = true, Currency = currency };
        }

        if (request.IsVariableAmount.HasValue || request.ClearVariableAmount)
        {
            document = document with
            {
                HasVariableAmount = true,
                IsVariableAmount = request.ClearVariableAmount ? null : request.IsVariableAmount
            };
        }

        return FinancialCommitmentOverrideBuildResult.Ok(document);
    }

    internal static ServiceError? ValidateActionPayload(
        string action,
        FinancialCommitmentDecisionRequest request)
    {
        var hasCorrectionPayload = request.AccountId.HasValue
            || request.ClearAccount
            || request.Label is not null
            || request.Cadence is not null
            || request.ClearCadence
            || request.NextDateUtc.HasValue
            || request.ClearNextDate
            || request.NextAmount.HasValue
            || request.ClearNextAmount
            || request.Currency is not null
            || request.ClearCurrency
            || request.IsVariableAmount.HasValue
            || request.ClearVariableAmount
            || request.ResetFields?.Any(field => !string.IsNullOrWhiteSpace(field)) == true;

        if (action == "correct" && !hasCorrectionPayload)
        {
            return new ServiceError(
                "At least one correction or reset is required.",
                "commitment_correction_required",
                StatusCodes.Status400BadRequest);
        }

        if (action != "correct" && hasCorrectionPayload)
        {
            return new ServiceError(
                "Correction fields are only valid for the correct action.",
                "commitment_action_fields_invalid",
                StatusCodes.Status400BadRequest);
        }

        return null;
    }

    internal static IReadOnlyList<string> ChangedFields(
        FinancialCommitmentDecisionRequest request,
        string action)
    {
        var fields = new HashSet<string>(request.ResetFields ?? [], StringComparer.Ordinal) { action };
        if (request.AccountId.HasValue || request.ClearAccount) fields.Add("account");
        if (request.Label is not null) fields.Add("label");
        if (request.Cadence is not null || request.ClearCadence) fields.Add("cadence");
        if (request.NextDateUtc.HasValue || request.ClearNextDate) fields.Add("nextDate");
        if (request.NextAmount.HasValue || request.ClearNextAmount) fields.Add("nextAmount");
        if (request.Currency is not null || request.ClearCurrency) fields.Add("currency");
        if (request.IsVariableAmount.HasValue || request.ClearVariableAmount) fields.Add("isVariableAmount");
        return fields.OrderBy(field => field, StringComparer.Ordinal).ToList();
    }

    internal static bool IsValidCurrency(string value)
    {
        return value.Length == 3 && value.All(character => character is >= 'A' and <= 'Z');
    }

    internal static Guid? TryParseManualId(string commitmentId)
    {
        const string prefix = "user_manual:";
        return commitmentId.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(commitmentId[prefix.Length..], "N", out var id)
                ? id
                : null;
    }

    private static FinancialCommitmentOverrideBuildResult? ValidateMutuallyExclusiveInput(
        FinancialCommitmentDecisionRequest request)
    {
        if (request.ClearAccount && request.AccountId.HasValue)
            return FinancialCommitmentOverrideBuildResult.Fail(
                "Account cannot be supplied and cleared together.",
                "commitment_account_invalid");
        if (request.ClearCadence && request.Cadence is not null)
            return FinancialCommitmentOverrideBuildResult.Fail(
                "Cadence cannot be supplied and cleared together.",
                "commitment_cadence_invalid");
        if (request.ClearNextDate && request.NextDateUtc.HasValue)
            return FinancialCommitmentOverrideBuildResult.Fail(
                "Next date cannot be supplied and cleared together.",
                "commitment_next_date_invalid");
        if (request.ClearNextAmount && request.NextAmount.HasValue)
            return FinancialCommitmentOverrideBuildResult.Fail(
                "Amount cannot be supplied and cleared together.",
                "commitment_amount_invalid");
        if (request.ClearCurrency && request.Currency is not null)
            return FinancialCommitmentOverrideBuildResult.Fail(
                "Currency cannot be supplied and cleared together.",
                "commitment_currency_invalid");
        if (request.ClearVariableAmount && request.IsVariableAmount.HasValue)
            return FinancialCommitmentOverrideBuildResult.Fail(
                "Variable state cannot be supplied and cleared together.",
                "commitment_variable_invalid");
        return null;
    }

    private static FinancialCommitmentOverrideDocument Reset(
        FinancialCommitmentOverrideDocument document,
        IReadOnlySet<string> fields)
    {
        if (fields.Contains("account"))
            document = document with { HasAccount = false, AccountId = null, LinkedBankAccountId = null, AccountDisplayName = null };
        if (fields.Contains("label")) document = document with { HasLabel = false, Label = null };
        if (fields.Contains("cadence")) document = document with { HasCadence = false, Cadence = null };
        if (fields.Contains("nextDate")) document = document with { HasNextDate = false, NextDateUtc = null };
        if (fields.Contains("nextAmount")) document = document with { HasNextAmount = false, NextAmount = null };
        if (fields.Contains("currency")) document = document with { HasCurrency = false, Currency = null };
        if (fields.Contains("isVariableAmount"))
            document = document with { HasVariableAmount = false, IsVariableAmount = null };
        return document;
    }
}

internal sealed record FinancialCommitmentOwnedAccount(
    Guid AccountId,
    string DisplayName,
    string Currency,
    Guid? LinkedBankAccountId);

internal sealed record FinancialCommitmentOverrideBuildResult(
    FinancialCommitmentOverrideDocument? Document,
    ServiceError? Error)
{
    internal static FinancialCommitmentOverrideBuildResult Ok(FinancialCommitmentOverrideDocument document) =>
        new(document, null);

    internal static FinancialCommitmentOverrideBuildResult Fail(string message, string code) => new(
        null,
        new ServiceError(message, code, StatusCodes.Status400BadRequest));

    internal static FinancialCommitmentOverrideBuildResult NotFound() => new(
        null,
        new ServiceError(
            "Account not found.",
            "commitment_account_not_found",
            StatusCodes.Status404NotFound));
}
