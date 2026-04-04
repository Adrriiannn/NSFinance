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
                && candidate.Row.BookedAtUtc <= row.BookedAtUtc
                && (row.BookedAtUtc - candidate.Row.BookedAtUtc).TotalHours <= 6
                && !candidate.HasTransferKeyword
                && !candidate.HasSavingsKeyword
                && !candidate.LooksLikeExternalCounterparty
                && Math.Abs(candidate.Row.Amount) > Math.Max(1m, Math.Abs(row.Amount)));

            var repeatedSmallAuxiliaryPatternCount = normalizedRows.Count(candidate =>
                candidate.Row.TransactionId != row.TransactionId
                && candidate.Row.FinancialAccountId == row.FinancialAccountId
                && candidate.IsOutflow
                && Math.Abs(candidate.Row.Amount) <= 5m
                && candidate.Row.BookedAtUtc <= row.BookedAtUtc
                && (row.BookedAtUtc - candidate.Row.BookedAtUtc).TotalDays <= 45
                && !candidate.HasTransferKeyword
                && (candidate.HasStrongSavingsKeyword || candidate.HasSavingsKeyword || candidate.Row.HasProviderTransferHint));

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
