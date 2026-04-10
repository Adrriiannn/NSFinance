namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class TransactionFeatureExtractor(
    TransactionNormalizationService normalizationService,
    ProviderCapabilityRegistry providerCapabilityRegistry,
    NarrativeSignalExtractor narrativeSignalExtractor)
{
    public sealed record TransactionFeatureInputRow(
        Guid TransactionId,
        Guid FinancialAccountId,
        decimal Amount,
        string Currency,
        DateTime BookedAtUtc,
        DateTime CreatedUtc,
        string Description,
        string? ProviderId,
        string? ProviderDisplayName,
        string? TransactionType,
        string? TransactionStatus,
        bool HasProviderTransferHint,
        bool HasCounterpartyAccounts,
        long StableSequence);

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
                var providerCapabilities = providerCapabilityRegistry.Resolve(row.ProviderId, row.ProviderDisplayName);
                var normalizedDescription = normalizationService.NormalizeDescription(row.Description);
                var tokens = normalizationService.Tokenize(normalizedDescription);
                var narrativeSignals = narrativeSignalExtractor.Extract(
                    row.Description,
                    normalizedDescription,
                    providerCapabilities);
                var transferKeyword = normalizationService.HasTransferKeyword(normalizedDescription, tokens);
                var savingsKeyword = normalizationService.HasSavingsKeyword(normalizedDescription, tokens);
                var providerTransferHint = row.HasProviderTransferHint
                                           || (providerCapabilities.SupportsProviderSpecificTransferMarkers
                                               && (narrativeSignals.ProviderSpecificReferenceTokens.Count > 0
                                                   || narrativeSignals.PaymentSystemMarkers.Count > 0));
                var merchantLikelihoodScore = ComputeMerchantLikelihoodScore(
                    normalizedDescription,
                    narrativeSignals,
                    providerCapabilities);

                return new
                {
                    Row = row,
                    ProviderCapabilities = providerCapabilities,
                    NormalizedDescription = normalizedDescription,
                    Tokens = tokens,
                    NarrativeSignals = narrativeSignals,
                    HasTransferKeyword = transferKeyword,
                    HasSavingsKeyword = savingsKeyword,
                    HasStrongSavingsKeyword = normalizationService.HasStrongSavingsKeyword(normalizedDescription),
                    HasWeakSavingsSupportKeyword = normalizationService.HasWeakSavingsSupportKeyword(tokens),
                    LooksLikeExternalCounterparty = normalizationService.LooksLikeExternalCounterparty(normalizedDescription, tokens),
                    IsBooked = IsBooked(row.TransactionStatus),
                    IsPending = IsPending(row.TransactionStatus),
                    DayKey = $"{Math.Abs(row.Amount):0.00}|{row.Currency.ToUpperInvariant()}|{row.BookedAtUtc:yyyy-MM-dd}",
                    IsOutflow = row.Amount < 0m,
                    IsInflow = row.Amount > 0m,
                    HasProviderTransferHint = providerTransferHint,
                    MerchantLikelihoodScore = merchantLikelihoodScore
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
                && mainSpend.MerchantLikelihoodScore >= 2
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
                && candidate.MerchantLikelihoodScore >= 2
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
                && (
                    hasNearbyLikelyMainSpendByTransactionId.TryGetValue(candidate.Row.TransactionId, out var hasNearbyMainSpend)
                    && hasNearbyMainSpend
                    || candidate.HasStrongSavingsKeyword
                    || candidate.HasProviderTransferHint
                    || candidate.NarrativeSignals.ProviderSpecificReferenceTokens.Count > 0));

            var accountHint = ResolveAccountHint(current.NarrativeSignals, current.NormalizedDescription);
            var hasHighConfidenceReferenceSignals = current.NarrativeSignals.HighConfidenceTokens.Count > 0;
            var hasMediumConfidenceReferenceSignals = current.NarrativeSignals.SignalConfidenceMap
                .Any(x => x.Value == NarrativeSignalConfidenceTier.MediumConfidence);
            var hasProviderSpecificTransferMarker = current.NarrativeSignals.ProviderSpecificReferenceTokens.Count > 0
                                                    || current.NarrativeSignals.PaymentSystemMarkers.Count > 0;
            var merchantLikelihoodVeto = current.MerchantLikelihoodScore >= 4;

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
                accountHint,
                current.IsBooked,
                current.IsPending,
                current.HasProviderTransferHint,
                current.ProviderCapabilities.ProviderKey,
                current.ProviderCapabilities.TimestampPrecision,
                current.ProviderCapabilities.SupportsMachineReferenceTokens,
                current.ProviderCapabilities.SupportsPaymentSystemMarkers,
                current.ProviderCapabilities.SupportsReliableCounterpartyReferenceFragments,
                current.ProviderCapabilities.SupportsProviderSpecificTransferMarkers,
                current.NarrativeSignals,
                hasHighConfidenceReferenceSignals,
                hasMediumConfidenceReferenceSignals,
                hasProviderSpecificTransferMarker,
                current.MerchantLikelihoodScore,
                merchantLikelihoodVeto,
                row.StableSequence,
                Math.Max(0, nearbyCount - 1),
                Math.Max(0, nearbyOutflowCount - 1),
                Math.Max(0, nearbyInflowCount - 1),
                nearbyMerchantOutflowCount,
                repeatedSmallAuxiliaryPatternCount,
                current.LooksLikeExternalCounterparty,
                row.Amount < 0m ? "outflow" : row.Amount > 0m ? "inflow" : "neutral",
                row.HasCounterpartyAccounts,
                normalizationService.ComputeReferenceEntropy(current.NormalizedDescription),
                RecurringPatternResult.None());
        }

        return features;
    }

    private static int ComputeMerchantLikelihoodScore(
        string normalizedDescription,
        NarrativeSignalSet narrativeSignals,
        DeterministicProviderCapabilities providerCapabilities)
    {
        var score = 0;
        var merchantSignals = narrativeSignals.MerchantLikeTokens;
        foreach (var signal in merchantSignals)
        {
            score += signal switch
            {
                "merchant_processor_shape" => 3,
                "merchant_card_present_shape" => 3,
                "merchant_subscription_company_shape" => 3,
                "merchant_retail_descriptor_shape" => 2,
                "merchant_company_suffix_shape" => 2,
                "merchant_uppercase_descriptor_shape" => 2,
                "merchant_spend_descriptor" => 1,
                _ => 1
            };
        }

        if ((merchantSignals.Contains("merchant_processor_shape")
             || merchantSignals.Contains("merchant_card_present_shape"))
            && (merchantSignals.Contains("merchant_retail_descriptor_shape")
                || merchantSignals.Contains("merchant_subscription_company_shape")))
        {
            score += 1;
        }

        if (ContainsWord(normalizedDescription, "card")
            || ContainsWord(normalizedDescription, "contactless")
            || ContainsWord(normalizedDescription, "pos")
            || ContainsWord(normalizedDescription, "terminal"))
        {
            score += 1;
        }

        if (merchantSignals.Count > 0
            && narrativeSignals.HighConfidenceTokens.Count == 0
            && narrativeSignals.ProviderSpecificReferenceTokens.Count == 0
            && narrativeSignals.PaymentSystemMarkers.Count == 0)
        {
            score += 1;
        }

        score += providerCapabilities.MerchantDescriptorReliability switch
        {
            DeterministicMerchantDescriptorReliability.High => 1,
            DeterministicMerchantDescriptorReliability.Low => -1,
            _ => 0
        };

        return Math.Max(0, score);
    }

    private static bool ContainsWord(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            haystack,
            $@"\b{System.Text.RegularExpressions.Regex.Escape(needle)}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string? ResolveAccountHint(
        NarrativeSignalSet narrativeSignals,
        string normalizedDescription)
    {
        var accountLike = narrativeSignals.AccountLikeTokens
            .Where(token => token.Any(char.IsDigit))
            .OrderByDescending(token => token.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(accountLike))
        {
            var digits = new string(accountLike.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4)
            {
                return digits[^4..];
            }
        }

        var fallbackDigits = new string(normalizedDescription.Where(char.IsDigit).ToArray());
        if (fallbackDigits.Length >= 4)
        {
            return fallbackDigits[^4..];
        }

        return null;
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
