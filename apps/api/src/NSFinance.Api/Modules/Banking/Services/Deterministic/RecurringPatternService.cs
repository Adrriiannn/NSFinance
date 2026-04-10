using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class RecurringPatternService(
    TransactionNormalizationService normalizationService,
    ILogger<RecurringPatternService> logger) : IRecurringPatternService
{
    private const double MaxMerchantConsistencyScore = 35d;
    private const double MaxIntervalConsistencyScore = 25d;
    private const double MaxAmountConsistencyScore = 15d;
    private const double MaxDirectionConsistencyScore = 10d;
    private const double MaxContinuityScore = 15d;
    private const double MaxPenaltyMagnitude = 40d;

    public Task<RecurringPatternResult> EvaluateAsync(
        Transaction candidate,
        IReadOnlyList<Transaction> historicalTransactions,
        RecurringPatternOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(historicalTransactions);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var reasonCodes = new HashSet<string>(StringComparer.Ordinal);
        if (candidate.Amount == 0m)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.ZeroAmountNotSupported);
            return Task.FromResult(RecurringPatternResult.None(reasonCodes.ToArray()));
        }

        var candidateDescriptor = ResolveDescriptor(candidate, options);
        var candidateDirection = Math.Sign(candidate.Amount);
        var lookbackStartUtc = candidate.BookedAtUtc - options.LookbackWindow;
        var priorMatches = new List<MatchCandidate>();
        var oppositeDirectionRelatedCount = 0;

        foreach (var historical in historicalTransactions)
        {
            ct.ThrowIfCancellationRequested();

            if (historical.Id == candidate.Id
                || historical.BookedAtUtc >= candidate.BookedAtUtc
                || historical.BookedAtUtc < lookbackStartUtc)
            {
                continue;
            }

            if (options.RequireSameCurrency
                && !string.Equals(historical.Currency, candidate.Currency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var descriptor = ResolveDescriptor(historical, options);
            var match = TryBuildMatchCandidate(candidateDescriptor, descriptor, options);
            if (match is null)
            {
                continue;
            }

            var historicalDirection = Math.Sign(historical.Amount);
            if (historicalDirection == 0)
            {
                continue;
            }

            if (historicalDirection != candidateDirection)
            {
                oppositeDirectionRelatedCount++;
                continue;
            }

            priorMatches.Add(new MatchCandidate(
                historical,
                descriptor,
                match.Value.Similarity,
                match.Value.MatchKind));
        }

        if (options.MaxCandidatePoolSize > 0 && priorMatches.Count > options.MaxCandidatePoolSize)
        {
            priorMatches = priorMatches
                .OrderByDescending(x => x.Similarity)
                .ThenByDescending(x => x.Transaction.BookedAtUtc)
                .Take(options.MaxCandidatePoolSize)
                .ToList();
        }

        if (priorMatches.Count < Math.Max(2, options.MinimumPriorMatches))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MinimumPriorMatchesNotMet);
            if (oppositeDirectionRelatedCount > 0)
            {
                reasonCodes.Add(RecurringPatternReasonCodes.MixedDirectionObserved);
            }

            return Task.FromResult(
                RecurringPatternResult.None(
                    reasonCodes: reasonCodes.ToArray(),
                    occurrenceCount: priorMatches.Count + 1,
                    matchedTransactionIds: priorMatches.Select(x => x.Transaction.Id).ToArray()));
        }

        var merchantScore = ScoreMerchantConsistency(priorMatches, reasonCodes);
        var amountScore = ScoreAmountConsistency(candidate, priorMatches, options, reasonCodes, out var amountVarianceHigh);
        var (cadence, intervalScore, intervalVarianceHigh, tooCloseClustering, hasMissingCycleGap) =
            ScoreIntervals(candidate, priorMatches, reasonCodes);
        var continuityScore = ScoreContinuity(candidate, priorMatches, cadence, hasMissingCycleGap, reasonCodes);
        var directionScore = oppositeDirectionRelatedCount > 0
            ? Math.Max(0d, MaxDirectionConsistencyScore - Math.Min(MaxDirectionConsistencyScore, oppositeDirectionRelatedCount * 2d))
            : MaxDirectionConsistencyScore;

        var discretionaryMerchant = candidateDescriptor.MerchantTokens
            .Any(token => options.DiscretionaryMerchantTokens.Contains(token));
        if (discretionaryMerchant)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantDiscretionaryPattern);
        }

        var averageDescriptionSimilarity = priorMatches.Average(match =>
            ComputeJaccard(candidateDescriptor.Tokens, match.Descriptor.Tokens));
        var mixedDescriptions = priorMatches.Count >= 3 && averageDescriptionSimilarity < 0.7d;

        var penalty = 0d;
        if (intervalVarianceHigh)
        {
            penalty -= 12d;
            reasonCodes.Add(RecurringPatternReasonCodes.IntervalVarianceHigh);
        }

        if (amountVarianceHigh)
        {
            penalty -= 12d;
            reasonCodes.Add(RecurringPatternReasonCodes.AmountVarianceHigh);
        }

        if (discretionaryMerchant)
        {
            penalty -= 14d;
        }

        if (tooCloseClustering)
        {
            penalty -= 12d;
            reasonCodes.Add(RecurringPatternReasonCodes.TooCloseClustering);
        }

        if (mixedDescriptions)
        {
            penalty -= 6d;
            reasonCodes.Add(RecurringPatternReasonCodes.MixedDescriptions);
        }

        if (oppositeDirectionRelatedCount > 0)
        {
            penalty -= 18d;
            reasonCodes.Add(RecurringPatternReasonCodes.MixedDirectionObserved);
        }

        penalty = Math.Clamp(penalty, -MaxPenaltyMagnitude, 0d);

        var rawScore = merchantScore + intervalScore + amountScore + directionScore + continuityScore + penalty;
        var confidenceScore = Math.Clamp(rawScore, 0d, 100d);
        var tier = ResolveTier(confidenceScore);

        var blocked = false;
        if (oppositeDirectionRelatedCount > 0)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByMixedDirection);
        }

        if (intervalScore < 5d || intervalVarianceHigh)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByHighIntervalVariance);
        }

        if (amountScore < 4d || amountVarianceHigh)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByHighAmountVariance);
        }

        if (discretionaryMerchant)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByDiscretionaryMerchant);
        }

        var isRecurring = !blocked && tier is RecurringConfidenceTier.Probable or RecurringConfidenceTier.Strong;
        if (blocked)
        {
            tier = RecurringConfidenceTier.None;
            confidenceScore = Math.Min(confidenceScore, 29d);
        }

        var signals = new RecurringSignalBreakdown(
            MerchantConsistencyScore: Math.Round(merchantScore, 2, MidpointRounding.AwayFromZero),
            IntervalConsistencyScore: Math.Round(intervalScore, 2, MidpointRounding.AwayFromZero),
            AmountConsistencyScore: Math.Round(amountScore, 2, MidpointRounding.AwayFromZero),
            DirectionConsistencyScore: Math.Round(directionScore, 2, MidpointRounding.AwayFromZero),
            ContinuityScore: Math.Round(continuityScore, 2, MidpointRounding.AwayFromZero),
            PenaltyScore: Math.Round(penalty, 2, MidpointRounding.AwayFromZero));

        var result = new RecurringPatternResult(
            IsRecurring: isRecurring,
            ConfidenceTier: tier,
            ConfidenceScore: Math.Round(confidenceScore, 2, MidpointRounding.AwayFromZero),
            Cadence: cadence,
            OccurrenceCount: priorMatches.Count + 1,
            MatchedTransactionIds: priorMatches.Select(x => x.Transaction.Id).ToArray(),
            Signals: signals,
            ReasonCodes: reasonCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray());

        if (result.IsRecurring)
        {
            logger.LogInformation(
                "Recurring pattern detected transactionId={TransactionId} score={Score} tier={Tier} cadence={Cadence} matchedCount={MatchedCount} matchedTransactionIds={MatchedTransactionIds} reasons={ReasonCodes}",
                candidate.Id,
                result.ConfidenceScore,
                result.ConfidenceTier,
                result.Cadence,
                result.MatchedTransactionIds.Count,
                string.Join(',', result.MatchedTransactionIds),
                string.Join(',', result.ReasonCodes));
        }
        else
        {
            logger.LogDebug(
                "Recurring pattern not detected transactionId={TransactionId} score={Score} tier={Tier} cadence={Cadence} matchedCount={MatchedCount} reasons={ReasonCodes}",
                candidate.Id,
                result.ConfidenceScore,
                result.ConfidenceTier,
                result.Cadence,
                result.MatchedTransactionIds.Count,
                string.Join(',', result.ReasonCodes));
        }

        return Task.FromResult(result);
    }

    private RecurringPatternTextDescriptor ResolveDescriptor(Transaction transaction, RecurringPatternOptions options)
    {
        if (options.PrecomputedTextByTransactionId is not null
            && options.PrecomputedTextByTransactionId.TryGetValue(transaction.Id, out var precomputed))
        {
            return precomputed;
        }

        var normalizedDescription = normalizationService.NormalizeDescription(transaction.Description);
        var tokens = normalizationService.Tokenize(normalizedDescription);
        var merchantTokens = tokens
            .Where(token =>
                token.Length > 2
                && !options.MerchantStopWords.Contains(token)
                && !token.All(char.IsDigit))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (merchantTokens.Count == 0)
        {
            merchantTokens = tokens
                .Where(token => token.Length > 2 && !token.All(char.IsDigit))
                .Take(4)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new RecurringPatternTextDescriptor(normalizedDescription, tokens, merchantTokens);
    }

    private static MatchDescriptor? TryBuildMatchCandidate(
        RecurringPatternTextDescriptor candidate,
        RecurringPatternTextDescriptor historical,
        RecurringPatternOptions options)
    {
        if (candidate.MerchantTokens.Count > 0
            && historical.MerchantTokens.Count > 0
            && candidate.MerchantTokens.SetEquals(historical.MerchantTokens))
        {
            return new MatchDescriptor(1d, MatchKind.MerchantExact);
        }

        var merchantSimilarity = ComputeJaccard(candidate.MerchantTokens, historical.MerchantTokens);
        if (merchantSimilarity >= options.MerchantFuzzyMatchThreshold)
        {
            return new MatchDescriptor(merchantSimilarity, MatchKind.MerchantFuzzy);
        }

        var descriptionSimilarity = ComputeJaccard(candidate.Tokens, historical.Tokens);
        if (descriptionSimilarity >= options.DescriptionSimilarityThreshold)
        {
            return new MatchDescriptor(descriptionSimilarity * 0.95d, MatchKind.DescriptionSimilarity);
        }

        return null;
    }

    private static double ScoreMerchantConsistency(IReadOnlyList<MatchCandidate> matches, ISet<string> reasonCodes)
    {
        if (matches.Any(match => match.MatchKind == MatchKind.MerchantExact))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantExactMatch);
            return MaxMerchantConsistencyScore;
        }

        var averageSimilarity = matches.Average(match => match.Similarity);
        if (matches.Any(match => match.MatchKind == MatchKind.MerchantFuzzy))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantFuzzyMatch);
        }
        else
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantDescriptionSimilarity);
        }

        if (averageSimilarity >= 0.9d)
        {
            return 32d;
        }

        if (averageSimilarity >= 0.8d)
        {
            return 26d;
        }

        if (averageSimilarity >= 0.7d)
        {
            return 20d;
        }

        return 12d;
    }

    private static double ScoreAmountConsistency(
        Transaction candidate,
        IReadOnlyList<MatchCandidate> matches,
        RecurringPatternOptions options,
        ISet<string> reasonCodes,
        out bool amountVarianceHigh)
    {
        var candidateAbs = Math.Abs(candidate.Amount);
        if (candidateAbs == 0m || matches.Count == 0)
        {
            amountVarianceHigh = true;
            return 0d;
        }

        var tolerance = Math.Max(0.01m, candidateAbs * Math.Max(0.01m, (decimal)options.AmountTolerancePercent));
        var priorAmounts = matches.Select(match => Math.Abs(match.Transaction.Amount)).ToArray();
        var exactCount = priorAmounts.Count(amount => amount == candidateAbs);
        var withinToleranceCount = priorAmounts.Count(amount => Math.Abs(amount - candidateAbs) <= tolerance);
        var exactRatio = exactCount / (double)priorAmounts.Length;
        var toleranceRatio = withinToleranceCount / (double)priorAmounts.Length;

        var mean = priorAmounts.Average(amount => (double)amount);
        var variance = priorAmounts.Average(amount =>
        {
            var delta = (double)amount - mean;
            return delta * delta;
        });
        var coefficientOfVariation = mean <= 0.0001d ? 1d : Math.Sqrt(variance) / mean;

        var score = (exactRatio * 10d) + (toleranceRatio * 5d);
        if (coefficientOfVariation <= 0.05d)
        {
            score = Math.Min(MaxAmountConsistencyScore, score + 2d);
        }
        else if (coefficientOfVariation >= 0.22d)
        {
            score = Math.Max(0d, score - 3d);
        }

        if (toleranceRatio >= 0.75d)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.AmountWithinTolerance);
        }

        amountVarianceHigh = toleranceRatio < 0.5d || coefficientOfVariation > 0.28d;
        return Math.Clamp(score, 0d, MaxAmountConsistencyScore);
    }

    private static (
        RecurringCadence Cadence,
        double IntervalScore,
        bool IntervalVarianceHigh,
        bool TooCloseClustering,
        bool MissingCycleGap) ScoreIntervals(
        Transaction candidate,
        IReadOnlyList<MatchCandidate> matches,
        ISet<string> reasonCodes)
    {
        var ordered = matches
            .Select(match => match.Transaction.BookedAtUtc)
            .Append(candidate.BookedAtUtc)
            .OrderBy(x => x)
            .ToArray();
        if (ordered.Length < 3)
        {
            return (RecurringCadence.Unknown, 0d, true, false, false);
        }

        var intervals = new List<double>(ordered.Length - 1);
        for (var i = 1; i < ordered.Length; i++)
        {
            intervals.Add((ordered[i] - ordered[i - 1]).TotalDays);
        }

        if (intervals.Count == 0)
        {
            return (RecurringCadence.Unknown, 0d, true, false, false);
        }

        var tooClose = intervals.Any(days => days < 2d);
        var cadenceBands = new (RecurringCadence Cadence, double MinDays, double MaxDays, double AnchorDays, string ReasonCode)[]
        {
            (RecurringCadence.Weekly, 6d, 8d, 7d, RecurringPatternReasonCodes.WeeklyIntervalCluster),
            (RecurringCadence.BiWeekly, 12d, 16d, 14d, RecurringPatternReasonCodes.BiWeeklyIntervalCluster),
            (RecurringCadence.Monthly, 26d, 35d, 30d, RecurringPatternReasonCodes.MonthlyIntervalCluster),
            (RecurringCadence.Quarterly, 80d, 100d, 91d, RecurringPatternReasonCodes.QuarterlyIntervalCluster),
            (RecurringCadence.Yearly, 350d, 380d, 365d, RecurringPatternReasonCodes.YearlyIntervalCluster)
        };

        var bestCadence = RecurringCadence.Irregular;
        var bestRatio = 0d;
        var bestVariance = 1d;
        var bestDoubleGapCount = 0;
        string? bestReasonCode = null;

        foreach (var band in cadenceBands)
        {
            var inBand = intervals.Where(days => days >= band.MinDays && days <= band.MaxDays).ToArray();
            if (inBand.Length == 0)
            {
                continue;
            }

            var doubleGapCount = intervals.Count(days => days >= band.MinDays * 1.8d && days <= band.MaxDays * 2.3d);
            var ratio = (inBand.Length + (doubleGapCount * 0.7d)) / intervals.Count;
            var variance = inBand.Average(days =>
            {
                var delta = days - band.AnchorDays;
                return delta * delta;
            });
            var normalizedVariance = Math.Min(1d, Math.Sqrt(variance) / Math.Max(1d, band.AnchorDays * 0.25d));
            if (ratio > bestRatio || (Math.Abs(ratio - bestRatio) < 0.0001d && normalizedVariance < bestVariance))
            {
                bestCadence = band.Cadence;
                bestRatio = ratio;
                bestVariance = normalizedVariance;
                bestDoubleGapCount = doubleGapCount;
                bestReasonCode = band.ReasonCode;
            }
        }

        var overallMean = intervals.Average();
        var overallVariance = intervals.Average(days =>
        {
            var delta = days - overallMean;
            return delta * delta;
        });
        var overallStd = Math.Sqrt(overallVariance);

        if (bestCadence != RecurringCadence.Irregular && bestRatio >= 0.6d)
        {
            if (bestReasonCode is not null)
            {
                reasonCodes.Add(bestReasonCode);
            }

            var score = (bestRatio * 18d) + ((1d - bestVariance) * 7d) - (bestDoubleGapCount * 2d);
            var intervalVarianceHigh = bestRatio < 0.65d || bestVariance > 0.8d;
            var expectedGap = bestCadence switch
            {
                RecurringCadence.Weekly => 7d,
                RecurringCadence.BiWeekly => 14d,
                RecurringCadence.Monthly => 30d,
                RecurringCadence.Quarterly => 91d,
                RecurringCadence.Yearly => 365d,
                _ => 30d
            };
            var missingCycleGap = bestDoubleGapCount > 0 || intervals.Any(days => days > expectedGap * 1.8d);
            return (
                bestCadence,
                Math.Clamp(score, 0d, MaxIntervalConsistencyScore),
                intervalVarianceHigh,
                tooClose,
                missingCycleGap);
        }

        reasonCodes.Add(RecurringPatternReasonCodes.IrregularIntervalPattern);
        var fallbackScore = Math.Max(0d, 10d - Math.Min(10d, overallStd / 3d));
        return (
            RecurringCadence.Irregular,
            Math.Clamp(fallbackScore, 0d, MaxIntervalConsistencyScore),
            true,
            tooClose,
            intervals.Any(days => days > (overallMean * 2d)));
    }

    private static double ScoreContinuity(
        Transaction candidate,
        IReadOnlyList<MatchCandidate> matches,
        RecurringCadence cadence,
        bool hasMissingCycleGap,
        ISet<string> reasonCodes)
    {
        var occurrenceCount = matches.Count + 1;
        var continuityScore = occurrenceCount switch
        {
            >= 5 => MaxContinuityScore,
            4 => 13d,
            3 => 10d,
            _ => 7d
        };

        if (occurrenceCount >= 4)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.FourOrMoreOccurrences);
        }
        else if (occurrenceCount >= 3)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.ThreeOrMoreOccurrences);
        }

        if (hasMissingCycleGap)
        {
            continuityScore = Math.Max(0d, continuityScore - 3d);
            reasonCodes.Add(RecurringPatternReasonCodes.MissingCycleGap);
        }

        var latestPriorDate = matches.Max(match => match.Transaction.BookedAtUtc);
        var daysSinceLatestPrior = (candidate.BookedAtUtc - latestPriorDate).TotalDays;
        var cadenceAnchor = cadence switch
        {
            RecurringCadence.Weekly => 7d,
            RecurringCadence.BiWeekly => 14d,
            RecurringCadence.Monthly => 30d,
            RecurringCadence.Quarterly => 91d,
            RecurringCadence.Yearly => 365d,
            _ => 30d
        };
        if (daysSinceLatestPrior > cadenceAnchor * 1.8d)
        {
            continuityScore = Math.Max(0d, continuityScore - 2d);
        }

        return Math.Clamp(continuityScore, 0d, MaxContinuityScore);
    }

    private static RecurringConfidenceTier ResolveTier(double score)
    {
        return score switch
        {
            >= 70d => RecurringConfidenceTier.Strong,
            >= 50d => RecurringConfidenceTier.Probable,
            >= 30d => RecurringConfidenceTier.Weak,
            _ => RecurringConfidenceTier.None
        };
    }

    private static double ComputeJaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0d;
        }

        var intersection = left.Count(token => right.Contains(token));
        if (intersection == 0)
        {
            return 0d;
        }

        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0d : intersection / (double)union;
    }

    private readonly record struct MatchDescriptor(double Similarity, MatchKind MatchKind);

    private sealed record MatchCandidate(
        Transaction Transaction,
        RecurringPatternTextDescriptor Descriptor,
        double Similarity,
        MatchKind MatchKind);

    private enum MatchKind
    {
        MerchantExact = 0,
        MerchantFuzzy = 1,
        DescriptionSimilarity = 2
    }
}
