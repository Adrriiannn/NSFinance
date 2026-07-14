using System.Text.Json;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

internal static class UserFinancialCommitmentProjector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    internal static string SerializeSnapshot(FinancialCommitmentDto snapshot)
    {
        return JsonSerializer.Serialize(snapshot, SerializerOptions);
    }

    internal static string? SerializeOverride(FinancialCommitmentOverrideDocument? document)
    {
        return document is null ? null : JsonSerializer.Serialize(document, SerializerOptions);
    }

    internal static FinancialCommitmentDto? DeserializeSnapshot(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FinancialCommitmentDto>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static FinancialCommitmentOverrideDocument? DeserializeOverride(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FinancialCommitmentOverrideDocument>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool TryProject(
        FinancialCommitmentDto? liveSource,
        UserFinancialCommitment decision,
        bool includeDismissed,
        out FinancialCommitmentDto? projected)
    {
        projected = null;
        if (decision.State == "dismissed" && !includeDismissed)
        {
            return true;
        }

        var snapshot = DeserializeSnapshot(decision.SnapshotJson);

        var sourceMissing = liveSource is null && decision.OriginType != "manual";
        var effective = liveSource ?? snapshot;
        if (effective is null)
        {
            return false;
        }

        if (decision.DecisionMode == "corrected")
        {
            var overrideDocument = DeserializeOverride(decision.OverrideJson);
            if (overrideDocument is null)
            {
                return false;
            }

            effective = ApplyOverride(effective, overrideDocument, decision.UpdatedUtc);
        }
        else if (decision.DecisionMode == "confirmed" && effective.Source == "inferred")
        {
            effective = effective with
            {
                Lifecycle = "active",
                Confidence = "user_confirmed",
                Exclusions = effective.Exclusions
                    .Where(reason => reason != "requires_user_confirmation")
                    .ToList()
            };
        }

        if (sourceMissing)
        {
            effective = effective with
            {
                Lifecycle = "needs_review",
                Freshness = "unknown",
                Exclusions = effective.Exclusions
                    .Append("source_commitment_unavailable")
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            };
        }

        var reasonCode = decision.State == "dismissed"
            ? "dismissed_by_user"
            : decision.LastAction == "reactivate"
                ? "reactivated_by_user"
                : decision.DecisionMode switch
                {
                    "manual" => "created_by_user",
                    "corrected" => "corrected_by_user",
                    _ => "confirmed_by_user"
                };
        var evidenceType = decision.DecisionMode switch
        {
            "manual" => "user_manual",
            "corrected" => "user_correction",
            _ => "user_confirmation"
        };
        var evidence = effective.Evidence
            .Append(new FinancialCommitmentEvidenceDto(
                evidenceType,
                decision.Id,
                FinancialCommitmentContractPolicy.EnsureUtc(decision.UpdatedUtc),
                "user_decision",
                [reasonCode]))
            .GroupBy(item => (item.Type, item.SourceRecordId))
            .Select(group => group.Last())
            .OrderBy(item => item.Type, StringComparer.Ordinal)
            .ThenByDescending(item => item.ObservedUtc)
            .ToList();

        projected = effective with
        {
            Lifecycle = decision.State == "dismissed" ? "dismissed" : effective.Lifecycle,
            Evidence = evidence
        };
        return true;
    }

    private static FinancialCommitmentDto ApplyOverride(
        FinancialCommitmentDto source,
        FinancialCommitmentOverrideDocument? document,
        DateTime updatedUtc)
    {
        if (document is null)
        {
            return source with
            {
                Source = "user_override",
                Confidence = "user_confirmed",
                SourceUpdatedUtc = FinancialCommitmentContractPolicy.EnsureUtc(updatedUtc),
                Freshness = "current"
            };
        }

        var exclusions = source.Exclusions.ToHashSet(StringComparer.Ordinal);
        var accountId = source.AccountId;
        var linkedBankAccountId = source.LinkedBankAccountId;
        var accountDisplayName = source.AccountDisplayName;
        if (document.HasAccount)
        {
            accountId = document.AccountId;
            linkedBankAccountId = document.LinkedBankAccountId;
            accountDisplayName = document.AccountDisplayName ?? string.Empty;
            if (accountId.HasValue)
            {
                exclusions.Remove("financial_account_mapping_unavailable");
            }
            else
            {
                exclusions.Add("financial_account_mapping_unavailable");
            }
        }

        var label = source.Label;
        if (document.HasLabel && !string.IsNullOrWhiteSpace(document.Label))
        {
            label = document.Label.Trim();
            exclusions.Remove("label_unavailable");
        }

        var cadence = document.HasCadence ? document.Cadence : source.Cadence;
        var nextDateUtc = document.HasNextDate ? document.NextDateUtc : source.NextDateUtc;
        var dateCertainty = source.DateCertainty;
        if (document.HasNextDate)
        {
            exclusions.Remove("next_date_elapsed");
            exclusions.Remove("estimated_next_date_elapsed");
            if (nextDateUtc.HasValue)
            {
                exclusions.Remove("next_date_unavailable");
                dateCertainty = "user_provided";
            }
            else
            {
                exclusions.Add("next_date_unavailable");
                dateCertainty = "unknown";
            }
        }

        var nextAmount = document.HasNextAmount ? document.NextAmount : source.NextAmount;
        var currency = document.HasCurrency ? document.Currency : source.Currency;
        var variableAmount = document.HasVariableAmount
            ? document.IsVariableAmount
            : source.IsVariableAmount;
        if (document.HasNextAmount)
        {
            if (nextAmount.HasValue)
            {
                exclusions.Remove("next_amount_unavailable");
            }
            else
            {
                exclusions.Add("next_amount_unavailable");
            }
        }

        if (document.HasCurrency)
        {
            if (!string.IsNullOrWhiteSpace(currency))
            {
                exclusions.Remove("next_currency_unavailable");
            }
            else if (nextAmount.HasValue)
            {
                exclusions.Add("next_currency_unavailable");
            }
        }

        var amountCertainty = variableAmount == true
            ? "variable"
            : nextAmount.HasValue
                ? "user_provided"
                : "unknown";

        return source with
        {
            Source = "user_override",
            Confidence = "user_confirmed",
            AccountId = accountId,
            LinkedBankAccountId = linkedBankAccountId,
            AccountDisplayName = accountDisplayName,
            Label = label,
            Cadence = cadence,
            NextDateUtc = nextDateUtc,
            DateCertainty = dateCertainty,
            NextAmount = nextAmount,
            Currency = currency,
            AmountCertainty = amountCertainty,
            IsVariableAmount = variableAmount,
            SourceUpdatedUtc = FinancialCommitmentContractPolicy.EnsureUtc(updatedUtc),
            Freshness = "current",
            Exclusions = exclusions.OrderBy(reason => reason, StringComparer.Ordinal).ToList()
        };
    }
}

internal sealed record FinancialCommitmentOverrideDocument(
    bool HasAccount,
    Guid? AccountId,
    Guid? LinkedBankAccountId,
    string? AccountDisplayName,
    bool HasLabel,
    string? Label,
    bool HasCadence,
    string? Cadence,
    bool HasNextDate,
    DateTime? NextDateUtc,
    bool HasNextAmount,
    decimal? NextAmount,
    bool HasCurrency,
    string? Currency,
    bool HasVariableAmount,
    bool? IsVariableAmount)
{
    internal static FinancialCommitmentOverrideDocument Empty { get; } = new(
        false,
        null,
        null,
        null,
        false,
        null,
        false,
        null,
        false,
        null,
        false,
        null,
        false,
        null,
        false,
        null);
}
