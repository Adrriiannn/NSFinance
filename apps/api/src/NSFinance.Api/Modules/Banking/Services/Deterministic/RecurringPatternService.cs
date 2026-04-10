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
        if (candidateDirection == 0)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.ZeroAmountNotSupported);
            return Task.FromResult(RecurringPatternResult.None(reasonCodes.ToArray()));
        }

        var lookbackStartUtc = candidate.BookedAtUtc - options.LookbackWindow;
        var candidatePool = BuildCandidatePool(candidate, historicalTransactions, candidateDescriptor, options);
        var sameDirectionMatches = new List<MatchCandidate>();
        var oppositeDirectionReversals = new List<MatchCandidate>();
        var oppositeDirectionConflicts = new List<MatchCandidate>();

        foreach (var historical in candidatePool)
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

            var candidateMatch = new MatchCandidate(
                historical,
                descriptor,
                match.Value.Similarity,
                match.Value.MatchKind,
                match.Value.SeriesIdentityType,
                match.Value.SignatureSimilarity,
                match.Value.MerchantSimilarity,
                match.Value.DescriptionSimilarity);

            if (historicalDirection == candidateDirection)
            {
                sameDirectionMatches.Add(candidateMatch);
            }
            else if (IsOppositeDirectionReversalLike(candidate, candidateDescriptor, historical, descriptor, options))
            {
                oppositeDirectionReversals.Add(candidateMatch);
                reasonCodes.Add(RecurringPatternReasonCodes.OppositeDirectionReversalObserved);
            }
            else
            {
                oppositeDirectionConflicts.Add(candidateMatch);
                reasonCodes.Add(RecurringPatternReasonCodes.OppositeDirectionConflictObserved);
            }
        }

        if (sameDirectionMatches.Count < Math.Max(2, options.MinimumPriorMatches))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MinimumPriorMatchesNotMet);
            if (oppositeDirectionConflicts.Count > 0)
            {
                reasonCodes.Add(RecurringPatternReasonCodes.MixedDirectionObserved);
            }

            return Task.FromResult(
                RecurringPatternResult.None(
                    reasonCodes: reasonCodes.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    occurrenceCount: sameDirectionMatches.Count + 1,
                    matchedTransactionIds: sameDirectionMatches.Select(x => x.Transaction.Id).ToArray(),
                    directionConflictTransactionIds: oppositeDirectionConflicts.Select(x => x.Transaction.Id).ToArray()));
        }

        var merchantScore = ScoreMerchantConsistency(sameDirectionMatches, reasonCodes, out var identityType);
        var interval = ScoreIntervals(candidate, sameDirectionMatches, reasonCodes);
        var amount = ScoreAmountConsistency(candidate, sameDirectionMatches, options, reasonCodes);
        var continuity = ScoreContinuity(candidate, sameDirectionMatches, interval.Cadence, interval.HasSkippedCycle, reasonCodes);
        var direction = ScoreDirectionConsistency(oppositeDirectionConflicts, oppositeDirectionReversals);
        var discretionaryMerchant = candidateDescriptor.MerchantTokens
            .Any(token => options.DiscretionaryMerchantTokens.Contains(token));

        if (candidateDescriptor.IsMixedUseMerchantFamily)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MixedUseSignatureIsolated);
        }

        var repeatedUsagePattern = IsRepeatedUsagePattern(
            sameDirectionMatches,
            interval,
            merchantScore,
            candidateDescriptor,
            options);
        if (repeatedUsagePattern)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.RecurringPatternLikelyRepeatedUsage);
        }

        var mixedDescriptions = sameDirectionMatches.Count >= 3
            && sameDirectionMatches.Average(x => x.DescriptionSimilarity) < 0.62d;
        var penalty = 0d;

        if (interval.IntervalStabilityTier == RecurringIntervalStabilityTier.Irregular)
        {
            penalty -= 12d;
            reasonCodes.Add(RecurringPatternReasonCodes.IntervalVarianceHigh);
        }
        else if (interval.IntervalStabilityTier == RecurringIntervalStabilityTier.Weak)
        {
            penalty -= 5d;
        }

        if (amount.AmountStabilityTier == RecurringAmountStabilityTier.Chaotic)
        {
            penalty -= 12d;
            reasonCodes.Add(RecurringPatternReasonCodes.AmountVarianceHigh);
        }
        else if (amount.AmountStabilityTier == RecurringAmountStabilityTier.MajorShift)
        {
            penalty -= 5d;
        }
        else if (amount.AmountStabilityTier == RecurringAmountStabilityTier.Shifted)
        {
            penalty -= 2d;
        }

        if (discretionaryMerchant)
        {
            penalty -= repeatedUsagePattern ? 16d : 8d;
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantDiscretionaryPattern);
        }

        if (mixedDescriptions)
        {
            penalty -= 6d;
            reasonCodes.Add(RecurringPatternReasonCodes.MixedDescriptions);
        }

        if (interval.TooCloseClustering)
        {
            penalty -= 10d;
            reasonCodes.Add(RecurringPatternReasonCodes.TooCloseClustering);
        }

        if (direction.ConflictCount > 0)
        {
            penalty -= Math.Min(12d, 4d * direction.ConflictCount);
            reasonCodes.Add(RecurringPatternReasonCodes.MixedDirectionObserved);
        }

        if (interval.HasCadenceDrift)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.CadenceDriftDetected);
        }

        penalty = Math.Clamp(penalty, -MaxPenaltyMagnitude, 0d);
        var rawScore = merchantScore
                       + interval.IntervalConsistencyScore
                       + amount.AmountConsistencyScore
                       + direction.DirectionConsistencyScore
                       + continuity.ContinuityScore
                       + penalty;
        var confidenceScore = Math.Clamp(rawScore, 0d, 100d);
        var tier = ResolveTier(confidenceScore);

        var blocked = false;
        if (direction.HardConflict)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByMixedDirection);
        }

        if (interval.IntervalStabilityTier == RecurringIntervalStabilityTier.Irregular
            && interval.CadenceConfidence < 0.5d)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByHighIntervalVariance);
        }

        if (amount.AmountStabilityTier == RecurringAmountStabilityTier.Chaotic)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByHighAmountVariance);
        }

        if (discretionaryMerchant
            && (repeatedUsagePattern || interval.IntervalStabilityTier == RecurringIntervalStabilityTier.Irregular))
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByDiscretionaryMerchant);
        }

        if (repeatedUsagePattern && interval.CadenceConfidence < 0.72d)
        {
            blocked = true;
            reasonCodes.Add(RecurringPatternReasonCodes.BlockedByRepeatedUsage);
        }

        var isRecurring = !blocked && tier is RecurringConfidenceTier.Probable or RecurringConfidenceTier.Strong;
        if (blocked)
        {
            confidenceScore = Math.Min(confidenceScore, 29d);
            tier = RecurringConfidenceTier.None;
        }

        var result = new RecurringPatternResult(
            IsRecurring: isRecurring,
            ConfidenceTier: tier,
            ConfidenceScore: Math.Round(confidenceScore, 2, MidpointRounding.AwayFromZero),
            Cadence: interval.Cadence,
            CadenceConfidence: Math.Round(interval.CadenceConfidence, 4, MidpointRounding.AwayFromZero),
            SeriesIdentityType: identityType,
            AmountStabilityTier: amount.AmountStabilityTier,
            IntervalStabilityTier: interval.IntervalStabilityTier,
            AmountChangeDetected: amount.AmountChangeDetected,
            MajorAmountShiftDetected: amount.MajorAmountShiftDetected,
            HistoricalTypicalAmount: amount.HistoricalTypicalAmount,
            AmountShiftRatio: amount.AmountShiftRatio,
            HasSkippedCycle: interval.HasSkippedCycle,
            HasCadenceDrift: interval.HasCadenceDrift,
            HasDirectionConflict: direction.ConflictCount > 0,
            DirectionConflictCount: direction.ConflictCount,
            IsRepeatedUsagePattern: repeatedUsagePattern,
            SeriesContinuityStrength: Math.Round(continuity.SeriesContinuityStrength, 4, MidpointRounding.AwayFromZero),
            OccurrenceCount: sameDirectionMatches.Count + 1,
            MatchedTransactionIds: sameDirectionMatches.Select(x => x.Transaction.Id).ToArray(),
            DirectionConflictTransactionIds: oppositeDirectionConflicts.Select(x => x.Transaction.Id).ToArray(),
            Signals: new RecurringSignalBreakdown(
                MerchantConsistencyScore: Math.Round(merchantScore, 2, MidpointRounding.AwayFromZero),
                IntervalConsistencyScore: Math.Round(interval.IntervalConsistencyScore, 2, MidpointRounding.AwayFromZero),
                AmountConsistencyScore: Math.Round(amount.AmountConsistencyScore, 2, MidpointRounding.AwayFromZero),
                DirectionConsistencyScore: Math.Round(direction.DirectionConsistencyScore, 2, MidpointRounding.AwayFromZero),
                ContinuityScore: Math.Round(continuity.ContinuityScore, 2, MidpointRounding.AwayFromZero),
                PenaltyScore: Math.Round(penalty, 2, MidpointRounding.AwayFromZero)),
            ReasonCodes: reasonCodes.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        logger.LogDebug(
            "Recurring evaluation transactionId={TransactionId} recurring={IsRecurring} tier={Tier} score={Score} cadence={Cadence} cadenceConfidence={CadenceConfidence} identityType={IdentityType} amountTier={AmountTier} intervalTier={IntervalTier} skipped={HasSkippedCycle} drift={HasCadenceDrift} repeatedUsage={IsRepeatedUsagePattern} matchedCount={MatchedCount} conflictCount={ConflictCount} reasons={ReasonCodes}",
            candidate.Id,
            result.IsRecurring,
            result.ConfidenceTier,
            result.ConfidenceScore,
            result.Cadence,
            result.CadenceConfidence,
            result.SeriesIdentityType,
            result.AmountStabilityTier,
            result.IntervalStabilityTier,
            result.HasSkippedCycle,
            result.HasCadenceDrift,
            result.IsRepeatedUsagePattern,
            result.MatchedTransactionIds.Count,
            result.DirectionConflictCount,
            string.Join(',', result.ReasonCodes));

        return Task.FromResult(result);
    }

    private IReadOnlyList<Transaction> BuildCandidatePool(
        Transaction candidate,
        IReadOnlyList<Transaction> historicalTransactions,
        RecurringPatternTextDescriptor candidateDescriptor,
        RecurringPatternOptions options)
    {
        if (historicalTransactions.Count <= options.MaxFallbackScanRows)
        {
            return historicalTransactions;
        }

        var lookbackStartUtc = candidate.BookedAtUtc - options.LookbackWindow;
        var rows = new List<Transaction>(Math.Min(options.MaxFallbackScanRows, historicalTransactions.Count));
        var signatureMatches = 0;
        var familyMatches = 0;

        foreach (var historical in historicalTransactions)
        {
            if (historical.Id == candidate.Id
                || historical.BookedAtUtc < lookbackStartUtc
                || historical.BookedAtUtc >= candidate.BookedAtUtc)
            {
                continue;
            }

            if (options.RequireSameCurrency
                && !string.Equals(historical.Currency, candidate.Currency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var descriptor = ResolveDescriptor(historical, options);
            if (!string.IsNullOrWhiteSpace(candidateDescriptor.BillingSignatureKey)
                && string.Equals(candidateDescriptor.BillingSignatureKey, descriptor.BillingSignatureKey, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(historical);
                signatureMatches++;
                if (rows.Count >= options.MaxFallbackScanRows)
                {
                    break;
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidateDescriptor.MerchantFamilyKey)
                && string.Equals(candidateDescriptor.MerchantFamilyKey, descriptor.MerchantFamilyKey, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(historical);
                familyMatches++;
                if (rows.Count >= options.MaxFallbackScanRows)
                {
                    break;
                }
            }
        }

        if (rows.Count >= Math.Max(6, options.MinimumPriorMatches) || (signatureMatches + familyMatches) > 0)
        {
            return rows;
        }

        return historicalTransactions.Take(options.MaxFallbackScanRows).ToArray();
    }

    private RecurringPatternTextDescriptor ResolveDescriptor(Transaction transaction, RecurringPatternOptions options)
    {
        if (options.PrecomputedTextByTransactionId is not null
            && options.PrecomputedTextByTransactionId.TryGetValue(transaction.Id, out var precomputed))
        {
            return precomputed;
        }

        return options.BuildDescriptor(normalizationService, transaction.Description);
    }

    private static MatchDescriptor? TryBuildMatchCandidate(
        RecurringPatternTextDescriptor candidate,
        RecurringPatternTextDescriptor historical,
        RecurringPatternOptions options)
    {
        if (!string.IsNullOrWhiteSpace(candidate.BillingSignatureKey)
            && string.Equals(candidate.BillingSignatureKey, historical.BillingSignatureKey, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchDescriptor(
                Similarity: 1d,
                MatchKind: MatchKind.SignatureExact,
                SeriesIdentityType: RecurringSeriesIdentityType.SignatureExact,
                SignatureSimilarity: 1d,
                MerchantSimilarity: ComputeJaccard(candidate.MerchantTokens, historical.MerchantTokens),
                DescriptionSimilarity: ComputeJaccard(candidate.Tokens, historical.Tokens));
        }

        var signatureSimilarity = ComputeJaccard(candidate.SignatureTokens, historical.SignatureTokens);
        if (signatureSimilarity >= 0.82d)
        {
            return new MatchDescriptor(
                Similarity: signatureSimilarity,
                MatchKind: MatchKind.SignatureFuzzy,
                SeriesIdentityType: RecurringSeriesIdentityType.SignatureFuzzy,
                SignatureSimilarity: signatureSimilarity,
                MerchantSimilarity: ComputeJaccard(candidate.MerchantTokens, historical.MerchantTokens),
                DescriptionSimilarity: ComputeJaccard(candidate.Tokens, historical.Tokens));
        }

        var merchantSimilarity = ComputeJaccard(candidate.MerchantTokens, historical.MerchantTokens);
        var familyExact = !string.IsNullOrWhiteSpace(candidate.MerchantFamilyKey)
                          && string.Equals(candidate.MerchantFamilyKey, historical.MerchantFamilyKey, StringComparison.OrdinalIgnoreCase);
        if (familyExact)
        {
            if (candidate.IsMixedUseMerchantFamily && signatureSimilarity < 0.70d)
            {
                return null;
            }

            return new MatchDescriptor(
                Similarity: Math.Max(0.6d, merchantSimilarity),
                MatchKind: MatchKind.MerchantFamilyExact,
                SeriesIdentityType: RecurringSeriesIdentityType.MerchantFamilyExact,
                SignatureSimilarity: signatureSimilarity,
                MerchantSimilarity: merchantSimilarity,
                DescriptionSimilarity: ComputeJaccard(candidate.Tokens, historical.Tokens));
        }

        if (merchantSimilarity >= options.MerchantFuzzyMatchThreshold)
        {
            return new MatchDescriptor(
                Similarity: merchantSimilarity * 0.95d,
                MatchKind: MatchKind.MerchantFamilyFuzzy,
                SeriesIdentityType: RecurringSeriesIdentityType.MerchantFamilyFuzzy,
                SignatureSimilarity: signatureSimilarity,
                MerchantSimilarity: merchantSimilarity,
                DescriptionSimilarity: ComputeJaccard(candidate.Tokens, historical.Tokens));
        }

        var descriptionSimilarity = ComputeJaccard(candidate.Tokens, historical.Tokens);
        if (descriptionSimilarity >= options.DescriptionSimilarityThreshold)
        {
            return new MatchDescriptor(
                Similarity: descriptionSimilarity * 0.9d,
                MatchKind: MatchKind.DescriptionSimilarity,
                SeriesIdentityType: RecurringSeriesIdentityType.DescriptionLed,
                SignatureSimilarity: signatureSimilarity,
                MerchantSimilarity: merchantSimilarity,
                DescriptionSimilarity: descriptionSimilarity);
        }

        return null;
    }

    private static bool IsOppositeDirectionReversalLike(
        Transaction candidate,
        RecurringPatternTextDescriptor candidateDescriptor,
        Transaction opposite,
        RecurringPatternTextDescriptor oppositeDescriptor,
        RecurringPatternOptions options)
    {
        var candidateAbs = Math.Abs(candidate.Amount);
        var oppositeAbs = Math.Abs(opposite.Amount);
        if (candidateAbs <= 0m || oppositeAbs <= 0m)
        {
            return false;
        }

        var tolerance = Math.Max(0.01m, candidateAbs * (decimal)Math.Max(0.01d, options.ReversalAmountTolerancePercent));
        var amountClose = Math.Abs(candidateAbs - oppositeAbs) <= tolerance;
        var daysGap = Math.Abs((candidate.BookedAtUtc - opposite.BookedAtUtc).TotalDays);
        var dateClose = daysGap <= options.ReversalWindowDays;
        var tokenHint = oppositeDescriptor.Tokens.Any(token => options.ReversalLikeTokens.Contains(token))
                        || candidateDescriptor.Tokens.Any(token => options.ReversalLikeTokens.Contains(token));
        return amountClose && (dateClose || tokenHint);
    }

    private static double ScoreMerchantConsistency(
        IReadOnlyList<MatchCandidate> matches,
        ISet<string> reasonCodes,
        out RecurringSeriesIdentityType identityType)
    {
        var dominantType = matches
            .GroupBy(x => x.SeriesIdentityType)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Average(x => x.Similarity))
            .Select(group => group.Key)
            .FirstOrDefault();
        identityType = dominantType == default ? RecurringSeriesIdentityType.Unknown : dominantType;

        if (matches.Any(match => match.MatchKind == MatchKind.SignatureExact))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.SignatureExactMatch);
        }
        else if (matches.Any(match => match.MatchKind == MatchKind.SignatureFuzzy))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.SignatureFuzzyMatch);
        }
        else if (matches.Any(match => match.MatchKind == MatchKind.MerchantFamilyExact))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantExactMatch);
        }
        else if (matches.Any(match => match.MatchKind == MatchKind.MerchantFamilyFuzzy))
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantFuzzyMatch);
        }
        else
        {
            reasonCodes.Add(RecurringPatternReasonCodes.MerchantDescriptionSimilarity);
        }

        var weightedSimilarity = matches.Average(match =>
        {
            var identityBoost = match.MatchKind switch
            {
                MatchKind.SignatureExact => 0.2d,
                MatchKind.SignatureFuzzy => 0.14d,
                MatchKind.MerchantFamilyExact => 0.09d,
                MatchKind.MerchantFamilyFuzzy => 0.04d,
                _ => 0d
            };
            return Math.Min(1d, match.Similarity + identityBoost);
        });
        return Math.Clamp(weightedSimilarity * MaxMerchantConsistencyScore, 0d, MaxMerchantConsistencyScore);
    }

    private static AmountAnalysis ScoreAmountConsistency(
        Transaction candidate,
        IReadOnlyList<MatchCandidate> matches,
        RecurringPatternOptions options,
        ISet<string> reasonCodes)
    {
        var candidateAbs = Math.Abs(candidate.Amount);
        if (candidateAbs == 0m || matches.Count == 0)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.AmountVarianceHigh);
            return new AmountAnalysis(
                AmountConsistencyScore: 0d,
                AmountStabilityTier: RecurringAmountStabilityTier.Chaotic,
                AmountChangeDetected: false,
                MajorAmountShiftDetected: false,
                HistoricalTypicalAmount: null,
                AmountShiftRatio: null,
                CoefficientOfVariation: 1d);
        }

        var priorAmounts = matches.Select(x => Math.Abs(x.Transaction.Amount)).OrderBy(x => x).ToArray();
        var median = priorAmounts.Length % 2 == 0
            ? (priorAmounts[(priorAmounts.Length / 2) - 1] + priorAmounts[priorAmounts.Length / 2]) / 2m
            : priorAmounts[priorAmounts.Length / 2];
        var mean = priorAmounts.Average(x => (double)x);
        var variance = priorAmounts.Average(x =>
        {
            var delta = (double)x - mean;
            return delta * delta;
        });
        var cv = mean <= 0.0001d ? 1d : Math.Sqrt(variance) / mean;

        var shiftRatio = median <= 0m ? 1d : (double)(candidateAbs / median);
        var normalizedRatio = shiftRatio >= 1d ? shiftRatio : (1d / Math.Max(shiftRatio, 0.0001d));
        var tolerance = Math.Max(0.01m, median * (decimal)Math.Max(0.01d, options.AmountTolerancePercent));
        var nearTolerance = Math.Max(0.01m, median * (decimal)Math.Max(options.AmountTolerancePercent, options.NearStableAmountTolerancePercent));

        var tier = RecurringAmountStabilityTier.Chaotic;
        if (Math.Abs(candidateAbs - median) <= tolerance && cv <= 0.05d)
        {
            tier = RecurringAmountStabilityTier.Exact;
            reasonCodes.Add(RecurringPatternReasonCodes.AmountTierExact);
        }
        else if (Math.Abs(candidateAbs - median) <= nearTolerance && cv <= 0.12d)
        {
            tier = RecurringAmountStabilityTier.NearStable;
            reasonCodes.Add(RecurringPatternReasonCodes.AmountTierNearStable);
        }
        else if (normalizedRatio < options.MajorAmountShiftRatioThreshold && cv <= 0.24d)
        {
            tier = RecurringAmountStabilityTier.Shifted;
            reasonCodes.Add(RecurringPatternReasonCodes.AmountTierShifted);
        }
        else if (cv <= 0.34d)
        {
            tier = RecurringAmountStabilityTier.MajorShift;
            reasonCodes.Add(RecurringPatternReasonCodes.AmountTierMajorShift);
        }
        else
        {
            tier = RecurringAmountStabilityTier.Chaotic;
            reasonCodes.Add(RecurringPatternReasonCodes.AmountTierChaotic);
        }

        var score = tier switch
        {
            RecurringAmountStabilityTier.Exact => 15d,
            RecurringAmountStabilityTier.NearStable => 13d,
            RecurringAmountStabilityTier.Shifted => 9d,
            RecurringAmountStabilityTier.MajorShift => 6.5d,
            _ => 2.5d
        };

        if (tier is RecurringAmountStabilityTier.Exact or RecurringAmountStabilityTier.NearStable)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.AmountWithinTolerance);
        }

        return new AmountAnalysis(
            AmountConsistencyScore: Math.Clamp(score, 0d, MaxAmountConsistencyScore),
            AmountStabilityTier: tier,
            AmountChangeDetected: tier is RecurringAmountStabilityTier.Shifted or RecurringAmountStabilityTier.MajorShift or RecurringAmountStabilityTier.Chaotic,
            MajorAmountShiftDetected: tier == RecurringAmountStabilityTier.MajorShift,
            HistoricalTypicalAmount: median,
            AmountShiftRatio: Math.Round(shiftRatio, 4, MidpointRounding.AwayFromZero),
            CoefficientOfVariation: cv);
    }

    private static IntervalAnalysis ScoreIntervals(
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
            return new IntervalAnalysis(
                Cadence: RecurringCadence.Unknown,
                CadenceConfidence: 0d,
                IntervalConsistencyScore: 0d,
                IntervalStabilityTier: RecurringIntervalStabilityTier.Unknown,
                HasSkippedCycle: false,
                HasCadenceDrift: false,
                TooCloseClustering: false);
        }

        var intervals = new List<double>(ordered.Length - 1);
        for (var i = 1; i < ordered.Length; i++)
        {
            intervals.Add((ordered[i] - ordered[i - 1]).TotalDays);
        }

        var tooClose = intervals.Any(days => days < 2d);
        var cadenceBands = new (RecurringCadence Cadence, double MinDays, double MaxDays, double AnchorDays, string ReasonCode)[]
        {
            (RecurringCadence.Weekly, 5d, 9d, 7d, RecurringPatternReasonCodes.WeeklyIntervalCluster),
            (RecurringCadence.BiWeekly, 11d, 17d, 14d, RecurringPatternReasonCodes.BiWeeklyIntervalCluster),
            (RecurringCadence.Monthly, 24d, 38d, 30d, RecurringPatternReasonCodes.MonthlyIntervalCluster),
            (RecurringCadence.Quarterly, 78d, 106d, 91d, RecurringPatternReasonCodes.QuarterlyIntervalCluster),
            (RecurringCadence.Yearly, 340d, 390d, 365d, RecurringPatternReasonCodes.YearlyIntervalCluster)
        };

        var bestCadence = RecurringCadence.Irregular;
        var bestConfidence = 0d;
        var bestVariance = 1d;
        var bestSkipped = false;
        var bestDrift = false;
        string? bestReason = null;

        foreach (var band in cadenceBands)
        {
            var inBand = intervals.Where(days => days >= band.MinDays && days <= band.MaxDays).ToArray();
            if (inBand.Length == 0)
            {
                continue;
            }

            var skipped = intervals.Count(days =>
                days >= band.MinDays * 1.75d
                && days <= band.MaxDays * 2.35d);
            var coverage = (inBand.Length + (skipped * 0.6d)) / intervals.Count;
            var variance = inBand.Average(days =>
            {
                var delta = days - band.AnchorDays;
                return delta * delta;
            });
            var normalizedVariance = Math.Min(1d, Math.Sqrt(variance) / Math.Max(1d, band.AnchorDays * 0.22d));
            var confidence = Math.Clamp((coverage * 0.7d) + ((1d - normalizedVariance) * 0.3d), 0d, 1d);
            var drift = coverage < 0.9d || normalizedVariance > 0.35d;

            if (confidence > bestConfidence
                || (Math.Abs(confidence - bestConfidence) < 0.0001d && normalizedVariance < bestVariance))
            {
                bestCadence = band.Cadence;
                bestConfidence = confidence;
                bestVariance = normalizedVariance;
                bestSkipped = skipped > 0;
                bestDrift = drift;
                bestReason = band.ReasonCode;
            }
        }

        if (bestCadence == RecurringCadence.Irregular || bestConfidence < 0.45d)
        {
            reasonCodes.Add(RecurringPatternReasonCodes.IrregularIntervalPattern);
            return new IntervalAnalysis(
                Cadence: RecurringCadence.Irregular,
                CadenceConfidence: Math.Clamp(bestConfidence, 0d, 1d),
                IntervalConsistencyScore: Math.Clamp(bestConfidence * MaxIntervalConsistencyScore * 0.4d, 0d, MaxIntervalConsistencyScore),
                IntervalStabilityTier: RecurringIntervalStabilityTier.Irregular,
                HasSkippedCycle: bestSkipped,
                HasCadenceDrift: true,
                TooCloseClustering: tooClose);
        }

        if (bestReason is not null)
        {
            reasonCodes.Add(bestReason);
        }

        var intervalTier = bestConfidence switch
        {
            >= 0.85d => RecurringIntervalStabilityTier.Strong,
            >= 0.68d => RecurringIntervalStabilityTier.Moderate,
            >= 0.52d => RecurringIntervalStabilityTier.Weak,
            _ => RecurringIntervalStabilityTier.Irregular
        };
        return new IntervalAnalysis(
            Cadence: bestCadence,
            CadenceConfidence: bestConfidence,
            IntervalConsistencyScore: Math.Clamp(bestConfidence * MaxIntervalConsistencyScore, 0d, MaxIntervalConsistencyScore),
            IntervalStabilityTier: intervalTier,
            HasSkippedCycle: bestSkipped,
            HasCadenceDrift: bestDrift,
            TooCloseClustering: tooClose);
    }

    private static ContinuityAnalysis ScoreContinuity(
        Transaction candidate,
        IReadOnlyList<MatchCandidate> matches,
        RecurringCadence cadence,
        bool hasSkippedCycle,
        ISet<string> reasonCodes)
    {
        var occurrenceCount = matches.Count + 1;
        var continuityScore = occurrenceCount switch
        {
            >= 6 => MaxContinuityScore,
            5 => 14d,
            4 => 12d,
            3 => 9.5d,
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

        if (hasSkippedCycle)
        {
            continuityScore = Math.Max(0d, continuityScore - 1.5d);
            reasonCodes.Add(RecurringPatternReasonCodes.MissingCycleGap);
        }

        var latestPrior = matches.Max(match => match.Transaction.BookedAtUtc);
        var gapDays = (candidate.BookedAtUtc - latestPrior).TotalDays;
        var expectedGap = cadence switch
        {
            RecurringCadence.Weekly => 7d,
            RecurringCadence.BiWeekly => 14d,
            RecurringCadence.Monthly => 30d,
            RecurringCadence.Quarterly => 91d,
            RecurringCadence.Yearly => 365d,
            _ => 30d
        };
        if (gapDays > expectedGap * 2.4d)
        {
            continuityScore = Math.Max(0d, continuityScore - 3d);
        }

        var strength = Math.Clamp(continuityScore / MaxContinuityScore, 0d, 1d);
        return new ContinuityAnalysis(continuityScore, strength);
    }

    private static DirectionAnalysis ScoreDirectionConsistency(
        IReadOnlyList<MatchCandidate> oppositeDirectionConflicts,
        IReadOnlyList<MatchCandidate> oppositeDirectionReversals)
    {
        var conflictCount = oppositeDirectionConflicts.Count;
        var reversalCount = oppositeDirectionReversals.Count;
        var score = MaxDirectionConsistencyScore - Math.Min(8d, conflictCount * 3d) - Math.Min(2d, reversalCount * 0.5d);
        var hardConflict = conflictCount >= 2;
        return new DirectionAnalysis(
            DirectionConsistencyScore: Math.Clamp(score, 0d, MaxDirectionConsistencyScore),
            ConflictCount: conflictCount,
            ReversalCount: reversalCount,
            HardConflict: hardConflict);
    }

    private static bool IsRepeatedUsagePattern(
        IReadOnlyList<MatchCandidate> sameDirectionMatches,
        IntervalAnalysis interval,
        double merchantScore,
        RecurringPatternTextDescriptor candidateDescriptor,
        RecurringPatternOptions options)
    {
        var enoughOccurrences = sameDirectionMatches.Count + 1 >= 4;
        if (!enoughOccurrences)
        {
            return false;
        }

        var weakCadence = interval.IntervalStabilityTier is RecurringIntervalStabilityTier.Weak or RecurringIntervalStabilityTier.Irregular
                          || interval.CadenceConfidence < 0.62d;
        var strongMerchant = merchantScore >= 22d;
        var discretionaryFamily = candidateDescriptor.MerchantTokens.Any(token => options.DiscretionaryMerchantTokens.Contains(token));
        return weakCadence && strongMerchant && discretionaryFamily;
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

    private readonly record struct MatchDescriptor(
        double Similarity,
        MatchKind MatchKind,
        RecurringSeriesIdentityType SeriesIdentityType,
        double SignatureSimilarity,
        double MerchantSimilarity,
        double DescriptionSimilarity);

    private sealed record MatchCandidate(
        Transaction Transaction,
        RecurringPatternTextDescriptor Descriptor,
        double Similarity,
        MatchKind MatchKind,
        RecurringSeriesIdentityType SeriesIdentityType,
        double SignatureSimilarity,
        double MerchantSimilarity,
        double DescriptionSimilarity);

    private enum MatchKind
    {
        SignatureExact = 0,
        SignatureFuzzy = 1,
        MerchantFamilyExact = 2,
        MerchantFamilyFuzzy = 3,
        DescriptionSimilarity = 4
    }

    private readonly record struct IntervalAnalysis(
        RecurringCadence Cadence,
        double CadenceConfidence,
        double IntervalConsistencyScore,
        RecurringIntervalStabilityTier IntervalStabilityTier,
        bool HasSkippedCycle,
        bool HasCadenceDrift,
        bool TooCloseClustering);

    private readonly record struct AmountAnalysis(
        double AmountConsistencyScore,
        RecurringAmountStabilityTier AmountStabilityTier,
        bool AmountChangeDetected,
        bool MajorAmountShiftDetected,
        decimal? HistoricalTypicalAmount,
        double? AmountShiftRatio,
        double CoefficientOfVariation);

    private readonly record struct DirectionAnalysis(
        double DirectionConsistencyScore,
        int ConflictCount,
        int ReversalCount,
        bool HardConflict);

    private readonly record struct ContinuityAnalysis(double ContinuityScore, double SeriesContinuityStrength);
}
