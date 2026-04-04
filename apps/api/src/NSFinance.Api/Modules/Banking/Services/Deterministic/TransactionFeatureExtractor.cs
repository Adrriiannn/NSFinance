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

        var normalizedRows = rows
            .Select(row =>
            {
                var normalizedDescription = normalizationService.NormalizeDescription(row.Description);
                var tokens = normalizationService.Tokenize(normalizedDescription);
                var transferKeyword = normalizationService.HasTransferKeyword(normalizedDescription, tokens);
                var savingsKeyword = normalizationService.HasSavingsKeyword(normalizedDescription, tokens);

                return new
                {
                    Row = row,
                    NormalizedDescription = normalizedDescription,
                    Tokens = tokens,
                    HasTransferKeyword = transferKeyword,
                    HasSavingsKeyword = savingsKeyword,
                    HasStrongSavingsKeyword = normalizationService.HasStrongSavingsKeyword(normalizedDescription),
                    HasWeakSavingsSupportKeyword = normalizationService.HasWeakSavingsSupportKeyword(tokens),
                    LooksLikeExternalCounterparty = normalizationService.LooksLikeExternalCounterparty(normalizedDescription, tokens),
                    IsBooked = IsBooked(row.TransactionStatus),
                    IsPending = IsPending(row.TransactionStatus),
                    DayKey = $"{Math.Abs(row.Amount):0.00}|{row.Currency.ToUpperInvariant()}|{row.BookedAtUtc:yyyy-MM-dd}",
                    IsOutflow = row.Amount < 0m,
                    IsInflow = row.Amount > 0m
                };
            })
            .ToList();

        var nearbyCounts = normalizedRows
            .GroupBy(x => x.DayKey)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var nearbyOutflowCountsByDayKey = normalizedRows
            .Where(x => x.IsOutflow)
            .GroupBy(x => x.DayKey)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var nearbyInflowCountsByDayKey = normalizedRows
            .Where(x => x.IsInflow)
            .GroupBy(x => x.DayKey)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var hasNearbyLikelyMainSpendByTransactionId = normalizedRows.ToDictionary(
            candidate => candidate.Row.TransactionId,
            candidate => normalizedRows.Any(mainSpend =>
                mainSpend.Row.TransactionId != candidate.Row.TransactionId
                && mainSpend.Row.FinancialAccountId == candidate.Row.FinancialAccountId
                && mainSpend.IsOutflow
                && !mainSpend.HasTransferKeyword
                && !mainSpend.HasSavingsKeyword
                && !mainSpend.HasStrongSavingsKeyword
                && !mainSpend.LooksLikeExternalCounterparty
                && Math.Abs(mainSpend.Row.Amount) > Math.Max(1m, Math.Abs(candidate.Row.Amount))
                && Math.Abs((mainSpend.Row.BookedAtUtc - candidate.Row.BookedAtUtc).TotalHours) <= 6d));

        var features = new Dictionary<Guid, DeterministicTransactionFeature>(rows.Count);
        foreach (var current in normalizedRows)
        {
            var row = current.Row;
            nearbyCounts.TryGetValue(current.DayKey, out var nearbyCount);
            nearbyOutflowCountsByDayKey.TryGetValue(current.DayKey, out var nearbyOutflowCount);
            nearbyInflowCountsByDayKey.TryGetValue(current.DayKey, out var nearbyInflowCount);

            var nearbyMerchantOutflowCount = normalizedRows.Count(candidate =>
                candidate.Row.TransactionId != row.TransactionId
                && candidate.Row.FinancialAccountId == row.FinancialAccountId
                && candidate.IsOutflow
                && Math.Abs((candidate.Row.BookedAtUtc - row.BookedAtUtc).TotalHours) <= 6d
                && !candidate.HasTransferKeyword
                && !candidate.HasSavingsKeyword
                && !candidate.HasStrongSavingsKeyword
                && !candidate.LooksLikeExternalCounterparty
                && Math.Abs(candidate.Row.Amount) > Math.Max(1m, Math.Abs(row.Amount)));

            var repeatedSmallAuxiliaryPatternCount = normalizedRows.Count(candidate =>
                candidate.Row.TransactionId != row.TransactionId
                && candidate.Row.FinancialAccountId == row.FinancialAccountId
                && candidate.IsOutflow
                && Math.Abs(candidate.Row.Amount) <= Math.Max(20m, Math.Abs(row.Amount) * 2m)
                && Math.Abs((candidate.Row.BookedAtUtc - row.BookedAtUtc).TotalDays) <= 45d
                && !candidate.HasTransferKeyword
                && !candidate.LooksLikeExternalCounterparty
                && (hasNearbyLikelyMainSpendByTransactionId.TryGetValue(candidate.Row.TransactionId, out var hasNearbyMainSpend) && hasNearbyMainSpend
                    || candidate.HasStrongSavingsKeyword
                    || candidate.Row.HasProviderTransferHint));

            features[row.TransactionId] = new DeterministicTransactionFeature(
                row.TransactionId,
                row.FinancialAccountId,
                row.Amount,
                Math.Abs(row.Amount),
                row.Amount < 0m,
                row.Amount > 0m,
                row.Currency.ToUpperInvariant(),
                row.BookedAtUtc,
                current.NormalizedDescription,
                current.Tokens,
                current.HasTransferKeyword,
                current.HasSavingsKeyword,
                current.HasStrongSavingsKeyword,
                current.HasWeakSavingsSupportKeyword,
                normalizationService.ExtractAccountHint(current.NormalizedDescription),
                current.IsBooked,
                current.IsPending,
                row.HasProviderTransferHint,
                Math.Max(0, nearbyCount - 1),
                Math.Max(0, nearbyOutflowCount - 1),
                Math.Max(0, nearbyInflowCount - 1),
                nearbyMerchantOutflowCount,
                repeatedSmallAuxiliaryPatternCount,
                current.LooksLikeExternalCounterparty,
                row.Amount < 0m ? "outflow" : row.Amount > 0m ? "inflow" : "neutral",
                row.HasCounterpartyAccounts,
                normalizationService.ComputeReferenceEntropy(current.NormalizedDescription));
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
