using NSFinance.Api.Modules.Banking.DTOs;

namespace NSFinance.Api.Modules.Banking.Services;

internal static class FinancialCommitmentProviderMapper
{
    private static readonly TimeSpan ProviderFreshnessWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan FutureTimestampTolerance = TimeSpan.FromMinutes(5);

    internal static FinancialCommitmentDto MapDirectDebit(
        ProviderDirectDebitCommitmentRow row,
        DateTime utcNow)
    {
        var exclusions = BuildCommonExclusions(
            row.FinancialAccountId,
            row.MerchantName,
            row.Reference,
            row.NextPaymentDateUtc,
            row.NextPaymentAmount,
            row.NextPaymentCurrency,
            row.UpdatedUtc,
            utcNow);
        var lifecycle = ResolveLifecycle(row.Status, null, utcNow, exclusions);
        var variableAmount = ResolveDirectDebitVariability(row.MandateType);

        return new FinancialCommitmentDto(
            $"provider_direct_debit:{row.Id:N}",
            "direct_debit",
            lifecycle,
            "provider",
            "confirmed",
            100d,
            "outflow",
            row.FinancialAccountId,
            row.LinkedBankAccountId,
            row.AccountDisplayName,
            FinancialCommitmentContractPolicy.ResolveLabel(row.MerchantName, row.Reference, "Direct debit"),
            null,
            null,
            null,
            FinancialCommitmentContractPolicy.NormalizeUtc(row.PreviousPaymentDateUtc),
            row.PreviousPaymentAmount,
            FinancialCommitmentContractPolicy.NormalizeCurrency(row.PreviousPaymentCurrency),
            FinancialCommitmentContractPolicy.NormalizeUtc(row.NextPaymentDateUtc),
            row.NextPaymentDateUtc.HasValue ? "provider_reported" : "unknown",
            row.NextPaymentAmount,
            FinancialCommitmentContractPolicy.NormalizeCurrency(row.NextPaymentCurrency),
            ResolveAmountCertainty(row.NextPaymentAmount, variableAmount),
            variableAmount,
            FinancialCommitmentContractPolicy.EnsureUtc(row.UpdatedUtc),
            ResolveFreshness(row.UpdatedUtc, utcNow),
            false,
            FinancialCommitmentContractPolicy.NormalizeOptional(row.Status),
            exclusions,
            [new FinancialCommitmentEvidenceDto(
                "provider_direct_debit",
                row.Id,
                FinancialCommitmentContractPolicy.EnsureUtc(row.UpdatedUtc),
                "provider_fact",
                [])]);
    }

    internal static FinancialCommitmentDto MapStandingOrder(
        ProviderStandingOrderCommitmentRow row,
        DateTime utcNow)
    {
        var exclusions = BuildCommonExclusions(
            row.FinancialAccountId,
            row.PayeeName,
            row.Reference,
            row.NextPaymentDateUtc,
            row.NextPaymentAmount,
            row.NextPaymentCurrency,
            row.UpdatedUtc,
            utcNow);
        var lifecycle = ResolveLifecycle(row.Status, row.FinalPaymentDateUtc, utcNow, exclusions);

        return new FinancialCommitmentDto(
            $"provider_standing_order:{row.Id:N}",
            "standing_order",
            lifecycle,
            "provider",
            "confirmed",
            100d,
            "outflow",
            row.FinancialAccountId,
            row.LinkedBankAccountId,
            row.AccountDisplayName,
            FinancialCommitmentContractPolicy.ResolveLabel(row.PayeeName, row.Reference, "Standing order"),
            FinancialCommitmentContractPolicy.NormalizeCadence(row.Frequency),
            FinancialCommitmentContractPolicy.NormalizeUtc(row.FirstPaymentDateUtc),
            FinancialCommitmentContractPolicy.NormalizeUtc(row.FinalPaymentDateUtc),
            null,
            null,
            null,
            FinancialCommitmentContractPolicy.NormalizeUtc(row.NextPaymentDateUtc),
            row.NextPaymentDateUtc.HasValue ? "provider_reported" : "unknown",
            row.NextPaymentAmount,
            FinancialCommitmentContractPolicy.NormalizeCurrency(row.NextPaymentCurrency),
            row.NextPaymentAmount.HasValue ? "provider_reported" : "unknown",
            false,
            FinancialCommitmentContractPolicy.EnsureUtc(row.UpdatedUtc),
            ResolveFreshness(row.UpdatedUtc, utcNow),
            false,
            FinancialCommitmentContractPolicy.NormalizeOptional(row.Status),
            exclusions,
            [new FinancialCommitmentEvidenceDto(
                "provider_standing_order",
                row.Id,
                FinancialCommitmentContractPolicy.EnsureUtc(row.UpdatedUtc),
                "provider_fact",
                [])]);
    }

    private static List<string> BuildCommonExclusions(
        Guid? financialAccountId,
        string? primaryLabel,
        string? fallbackLabel,
        DateTime? nextDateUtc,
        decimal? nextAmount,
        string? nextCurrency,
        DateTime updatedUtc,
        DateTime utcNow)
    {
        var exclusions = new List<string>();
        if (!financialAccountId.HasValue)
        {
            exclusions.Add("financial_account_mapping_unavailable");
        }

        if (string.IsNullOrWhiteSpace(primaryLabel) && string.IsNullOrWhiteSpace(fallbackLabel))
        {
            exclusions.Add("label_unavailable");
        }

        if (!nextDateUtc.HasValue)
        {
            exclusions.Add("next_date_unavailable");
        }
        else if (FinancialCommitmentContractPolicy.EnsureUtc(nextDateUtc.Value) < utcNow)
        {
            exclusions.Add("next_date_elapsed");
        }

        if (!nextAmount.HasValue)
        {
            exclusions.Add("next_amount_unavailable");
        }
        else if (string.IsNullOrWhiteSpace(nextCurrency))
        {
            exclusions.Add("next_currency_unavailable");
        }

        var normalizedUpdatedUtc = FinancialCommitmentContractPolicy.EnsureUtc(updatedUtc);
        if (normalizedUpdatedUtc > utcNow.Add(FutureTimestampTolerance))
        {
            exclusions.Add("future_source_timestamp");
        }
        else if (utcNow - normalizedUpdatedUtc > ProviderFreshnessWindow)
        {
            exclusions.Add("stale_provider_source");
        }

        return exclusions;
    }

    private static string ResolveLifecycle(
        string? providerStatus,
        DateTime? finalPaymentDateUtc,
        DateTime utcNow,
        ICollection<string> exclusions)
    {
        var status = FinancialCommitmentContractPolicy.NormalizeToken(providerStatus);
        var lifecycle = status switch
        {
            "active" or "enabled" or "authorized" or "authorised" => "active",
            "paused" or "suspended" or "on_hold" => "paused",
            "cancelled" or "canceled" or "revoked" or "inactive" or "disabled" => "cancelled",
            "expired" => "expired",
            "failed" or "rejected" or "error" => "needs_review",
            "pending" or "pending_authorization" or "pending_authorisation" => "pending",
            _ => "unknown"
        };

        if (lifecycle == "unknown")
        {
            exclusions.Add(string.IsNullOrWhiteSpace(providerStatus)
                ? "provider_status_unavailable"
                : "provider_status_unrecognized");
        }

        if (finalPaymentDateUtc.HasValue
            && FinancialCommitmentContractPolicy.EnsureUtc(finalPaymentDateUtc.Value) < utcNow
            && lifecycle is not "cancelled")
        {
            exclusions.Add("final_date_elapsed");
            lifecycle = "expired";
        }

        return lifecycle;
    }

    private static string ResolveFreshness(DateTime updatedUtc, DateTime utcNow)
    {
        var normalizedUpdatedUtc = FinancialCommitmentContractPolicy.EnsureUtc(updatedUtc);
        if (normalizedUpdatedUtc > utcNow.Add(FutureTimestampTolerance))
        {
            return "unknown";
        }

        return utcNow - normalizedUpdatedUtc <= ProviderFreshnessWindow
            ? "fresh"
            : "stale";
    }

    private static bool? ResolveDirectDebitVariability(string? mandateType)
    {
        var normalized = FinancialCommitmentContractPolicy.NormalizeToken(mandateType);
        if (normalized.Contains("variable", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.Contains("fixed", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    private static string ResolveAmountCertainty(decimal? amount, bool? isVariable)
    {
        if (!amount.HasValue)
        {
            return "unknown";
        }

        return isVariable == true ? "variable" : "provider_reported";
    }
}
