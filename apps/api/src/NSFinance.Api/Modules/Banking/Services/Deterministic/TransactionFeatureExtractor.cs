namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class TransactionFeatureExtractor(TransactionNormalizationService normalizationService)
{
    public sealed record TransactionFeatureInputRow(
        Guid TransactionId,
        Guid FinancialAccountId,
        decimal Amount,
        string Currency,
        DateTime BookedAtUtc,
        string Description,
        string? TransactionType,
        string? TransactionStatus,
        bool HasProviderTransferHint,
        bool HasCounterpartyAccounts);

    public IReadOnlyDictionary<Guid, DeterministicTransactionFeature> BuildFeatures(
        IReadOnlyList<TransactionFeatureInputRow> rows)
    {
        if (rows.Count == 0)
        {
            return new Dictionary<Guid, DeterministicTransactionFeature>();
        }

        var nearbyCounts = rows
            .GroupBy(x => $"{Math.Abs(x.Amount):0.00}|{x.Currency.ToUpperInvariant()}|{x.BookedAtUtc:yyyy-MM-dd}")
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var features = new Dictionary<Guid, DeterministicTransactionFeature>(rows.Count);
        foreach (var row in rows)
        {
            var normalizedDescription = normalizationService.NormalizeDescription(row.Description);
            var tokens = normalizationService.Tokenize(normalizedDescription);
            var isBooked = IsBooked(row.TransactionStatus);
            var isPending = IsPending(row.TransactionStatus);
            var nearbyKey = $"{Math.Abs(row.Amount):0.00}|{row.Currency.ToUpperInvariant()}|{row.BookedAtUtc:yyyy-MM-dd}";
            nearbyCounts.TryGetValue(nearbyKey, out var nearbyCount);

            features[row.TransactionId] = new DeterministicTransactionFeature(
                row.TransactionId,
                row.FinancialAccountId,
                row.Amount,
                Math.Abs(row.Amount),
                row.Amount < 0m,
                row.Amount > 0m,
                row.Currency.ToUpperInvariant(),
                row.BookedAtUtc,
                normalizedDescription,
                tokens,
                normalizationService.HasTransferKeyword(normalizedDescription, tokens),
                normalizationService.HasSavingsKeyword(normalizedDescription, tokens),
                normalizationService.HasStrongSavingsKeyword(normalizedDescription),
                normalizationService.ExtractAccountHint(normalizedDescription),
                isBooked,
                isPending,
                row.HasProviderTransferHint,
                Math.Max(0, nearbyCount - 1),
                row.HasCounterpartyAccounts,
                normalizationService.ComputeReferenceEntropy(normalizedDescription));
        }

        return features;
    }

    private static bool IsBooked(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized is "booked" or "posted" or "settled";
    }

    private static bool IsPending(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized is "pending" or "authorised" or "authorized";
    }
}
