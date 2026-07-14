using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

internal static class ManualFinancialCommitmentFactory
{
    internal static UserFinancialCommitment Create(
        Guid id,
        Guid userId,
        CreateManualFinancialCommitmentRequest request,
        FinancialCommitmentOwnedAccount? account,
        string? currency,
        DateTime utcNow)
    {
        var startsAtUtc = request.StartsAtUtc?.UtcDateTime;
        var endsAtUtc = request.EndsAtUtc?.UtcDateTime;
        var nextDateUtc = request.NextDateUtc?.UtcDateTime;
        var cadence = FinancialCommitmentContractPolicy.NormalizeCadence(request.Cadence);
        var exclusions = new List<string>();
        if (!nextDateUtc.HasValue)
        {
            exclusions.Add("next_date_unavailable");
        }

        if (!request.NextAmount.HasValue)
        {
            exclusions.Add("next_amount_unavailable");
        }
        else if (currency is null)
        {
            exclusions.Add("next_currency_unavailable");
        }

        var snapshot = new FinancialCommitmentDto(
            $"user_manual:{id:N}",
            "manual",
            "active",
            "user",
            "confirmed",
            100d,
            "outflow",
            account?.AccountId,
            account?.LinkedBankAccountId,
            account?.DisplayName ?? string.Empty,
            request.Label!.Trim(),
            cadence,
            startsAtUtc,
            endsAtUtc,
            null,
            null,
            null,
            nextDateUtc,
            nextDateUtc.HasValue ? "user_provided" : "unknown",
            request.NextAmount,
            currency,
            request.IsVariableAmount == true
                ? "variable"
                : request.NextAmount.HasValue
                    ? "user_provided"
                    : "unknown",
            request.IsVariableAmount,
            utcNow,
            "current",
            false,
            null,
            exclusions,
            [new FinancialCommitmentEvidenceDto(
                "user_manual",
                id,
                utcNow,
                "user_decision",
                ["created_by_user"])]);

        return new UserFinancialCommitment
        {
            Id = id,
            UserId = userId,
            OriginType = "manual",
            State = "active",
            DecisionMode = "manual",
            LastAction = "create",
            SnapshotJson = UserFinancialCommitmentProjector.SerializeSnapshot(snapshot),
            EffectiveAccountId = snapshot.AccountId,
            EffectiveNextDateUtc = snapshot.NextDateUtc,
            Revision = 1,
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow,
            ConfirmedUtc = utcNow
        };
    }
}
