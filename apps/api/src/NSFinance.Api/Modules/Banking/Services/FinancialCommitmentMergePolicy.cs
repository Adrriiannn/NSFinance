using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services.Deterministic;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class FinancialCommitmentMergePolicy(TransactionNormalizationService normalizationService)
{
    internal IReadOnlyList<FinancialCommitmentDto> Merge(
        IReadOnlyList<FinancialCommitmentDto> providers,
        IReadOnlyList<FinancialCommitmentDto> inferred)
    {
        var effectiveProviders = providers.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var unmatchedInferred = new List<FinancialCommitmentDto>();
        var descriptorBuilder = new RecurringPatternOptions();

        foreach (var inferredItem in inferred.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var inferredDescriptor = descriptorBuilder.BuildDescriptor(normalizationService, inferredItem.Label);
            var match = effectiveProviders.Values
                .Where(provider => ProviderCanAbsorbInference(provider, inferredItem))
                .Select(provider => new
                {
                    Provider = provider,
                    Descriptor = descriptorBuilder.BuildDescriptor(normalizationService, provider.Label)
                })
                .Where(candidate => IsDescriptorMatch(
                    candidate.Descriptor,
                    inferredDescriptor,
                    candidate.Provider,
                    inferredItem))
                .OrderBy(candidate => DescriptorRank(candidate.Descriptor, inferredDescriptor))
                .ThenBy(candidate => DateDistanceDays(candidate.Provider.NextDateUtc, inferredItem.NextDateUtc))
                .ThenBy(candidate => AmountDistanceRatio(candidate.Provider.NextAmount, inferredItem.NextAmount))
                .ThenBy(candidate => candidate.Provider.Id, StringComparer.Ordinal)
                .Select(candidate => candidate.Provider)
                .FirstOrDefault();

            if (match is null)
            {
                unmatchedInferred.Add(inferredItem);
                continue;
            }

            var mergedEvidence = match.Evidence
                .Concat(inferredItem.Evidence)
                .GroupBy(item => (item.Type, item.SourceRecordId))
                .Select(group => group.First())
                .OrderBy(item => item.Type, StringComparer.Ordinal)
                .ThenByDescending(item => item.ObservedUtc)
                .ThenBy(item => item.SourceRecordId)
                .ToList();
            effectiveProviders[match.Id] = match with { Evidence = mergedEvidence };
        }

        return effectiveProviders.Values
            .Concat(unmatchedInferred)
            .ToList();
    }

    private static bool ProviderCanAbsorbInference(
        FinancialCommitmentDto provider,
        FinancialCommitmentDto inferred)
    {
        if (provider.Source != "provider"
            || provider.Lifecycle is "cancelled" or "expired"
            || provider.AccountId != inferred.AccountId)
        {
            return false;
        }

        return provider.Currency is null
            || inferred.Currency is null
            || string.Equals(provider.Currency, inferred.Currency, StringComparison.Ordinal);
    }

    private static bool IsDescriptorMatch(
        RecurringPatternTextDescriptor provider,
        RecurringPatternTextDescriptor inferred,
        FinancialCommitmentDto providerItem,
        FinancialCommitmentDto inferredItem)
    {
        var exactSignature = !string.IsNullOrWhiteSpace(provider.BillingSignatureKey)
            && string.Equals(
                provider.BillingSignatureKey,
                inferred.BillingSignatureKey,
                StringComparison.OrdinalIgnoreCase);
        if (exactSignature)
        {
            return true;
        }

        var sameFamily = !string.IsNullOrWhiteSpace(provider.MerchantFamilyKey)
            && string.Equals(
                provider.MerchantFamilyKey,
                inferred.MerchantFamilyKey,
                StringComparison.OrdinalIgnoreCase);
        return sameFamily
            && (DateDistanceDays(providerItem.NextDateUtc, inferredItem.NextDateUtc) <= 14d
                || AmountDistanceRatio(providerItem.NextAmount, inferredItem.NextAmount) <= 0.15d);
    }

    private static int DescriptorRank(
        RecurringPatternTextDescriptor provider,
        RecurringPatternTextDescriptor inferred)
    {
        return string.Equals(
            provider.BillingSignatureKey,
            inferred.BillingSignatureKey,
            StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }

    private static double DateDistanceDays(DateTime? left, DateTime? right)
    {
        return left.HasValue && right.HasValue
            ? Math.Abs((FinancialCommitmentContractPolicy.EnsureUtc(left.Value)
                - FinancialCommitmentContractPolicy.EnsureUtc(right.Value)).TotalDays)
            : double.MaxValue;
    }

    private static double AmountDistanceRatio(decimal? left, decimal? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return double.MaxValue;
        }

        var denominator = Math.Max(Math.Abs(left.Value), Math.Abs(right.Value));
        return denominator == 0m
            ? 0d
            : (double)(Math.Abs(Math.Abs(left.Value) - Math.Abs(right.Value)) / denominator);
    }
}
