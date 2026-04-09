using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class DeterministicClassificationPersistenceService(
    AppDbContext dbContext,
    TransactionNormalizationService normalizationService,
    TransactionFeatureExtractor featureExtractor,
    TransferPairingEngine transferPairingEngine,
    SavingsRoutingPolicy savingsRoutingPolicy,
    SavingsTransferClassifier savingsTransferClassifier,
    DeterministicClassificationRetryPlanner retryPlanner,
    DeterministicCategorizationMetrics metrics,
    ILogger<DeterministicClassificationPersistenceService> logger)
{
    private const int CounterpartyDeferExpiryHours = 48;
    private const int ContextDeferExpiryHours = 24;

    public async Task<DeterministicCategorizationSummary> EvaluateWindowAsync(
        Guid userId,
        DateTime selectionStartUtc,
        DateTime selectionEndUtc,
        DateTime contextStartUtc,
        DateTime contextEndUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var linkedAccounts = await LoadLinkedAccountScopeAsync(userId, cancellationToken);
        if (linkedAccounts.Count == 0)
        {
            return EmptySummary();
        }

        var contextRows = await LoadTransactionsWithContextAsync(
            linkedAccounts.Keys,
            contextStartUtc,
            contextEndUtc,
            cancellationToken);

        var targetIds = contextRows
            .Where(x => x.BookedAtUtc >= selectionStartUtc && x.BookedAtUtc <= selectionEndUtc)
            .Select(x => x.Id)
            .ToArray();

        return await EvaluateInternalAsync(
            linkedAccounts,
            contextRows,
            targetIds,
            now,
            cancellationToken);
    }

    public async Task<DeterministicCategorizationSummary> EvaluateTransactionsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> transactionIds,
        DateTime contextStartUtc,
        DateTime contextEndUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return EmptySummary();
        }

        var linkedAccounts = await LoadLinkedAccountScopeAsync(userId, cancellationToken);
        if (linkedAccounts.Count == 0)
        {
            return EmptySummary();
        }

        var contextRows = await LoadTransactionsWithContextAsync(
            linkedAccounts.Keys,
            contextStartUtc,
            contextEndUtc,
            cancellationToken);

        return await EvaluateInternalAsync(
            linkedAccounts,
            contextRows,
            transactionIds,
            now,
            cancellationToken);
    }

    private async Task<DeterministicCategorizationSummary> EvaluateInternalAsync(
        IReadOnlyDictionary<Guid, LinkedAccountDeterministicContext> linkedAccountsByFinancialAccountId,
        IReadOnlyList<Transaction> contextRows,
        IReadOnlyCollection<Guid> targetTransactionIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (targetTransactionIds.Count == 0 || contextRows.Count == 0)
        {
            return EmptySummary();
        }

        var stopwatch = Stopwatch.StartNew();

        var allTransactionIds = contextRows.Select(x => x.Id).ToArray();
        var normalizedByTransactionId = await LoadNormalizedContextAsync(allTransactionIds, cancellationToken);
        var stableSequenceByTransactionId = contextRows
            .OrderBy(x => x.BookedAtUtc)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Select((row, index) => new { row.Id, StableSequence = (long)index + 1L })
            .ToDictionary(x => x.Id, x => x.StableSequence);
        var accountIds = linkedAccountsByFinancialAccountId.Keys.ToArray();
        var hasCounterpartyByAccountId = accountIds.ToDictionary(
            accountId => accountId,
            accountId => accountIds.Any(other => other != accountId));
        var hasFullSameUserCounterpartyUniverse = linkedAccountsByFinancialAccountId.Count > 1;

        var featureInputs = contextRows
            .Select(row =>
            {
                normalizedByTransactionId.TryGetValue(row.Id, out var normalized);
                linkedAccountsByFinancialAccountId.TryGetValue(row.FinancialAccountId, out var providerContext);
                return new TransactionFeatureExtractor.TransactionFeatureInputRow(
                    row.Id,
                    row.FinancialAccountId,
                    row.Amount,
                    row.Currency,
                    row.BookedAtUtc,
                    row.CreatedUtc,
                    row.Description,
                    providerContext?.ProviderId,
                    providerContext?.ProviderDisplayName,
                    normalized?.TransactionType,
                    normalized?.TransactionStatus,
                    HasProviderTransferHint(normalized?.TransactionType),
                    hasCounterpartyByAccountId.TryGetValue(row.FinancialAccountId, out var hasCounterparty) && hasCounterparty,
                    stableSequenceByTransactionId.TryGetValue(row.Id, out var stableSequence) ? stableSequence : 0L);
            })
            .ToList();
        var featuresById = featureExtractor.BuildFeatures(featureInputs);

        var byId = contextRows.ToDictionary(x => x.Id);
        var targetSet = targetTransactionIds.ToHashSet();
        var linkedPairs = BuildLinkedPairMap(contextRows, byId);
        var pairedIds = linkedPairs.Keys.ToHashSet();
        var pairingAnalysis = transferPairingEngine.AnalyzeUnpairedTransactions(featuresById, pairedIds);
        var deterministicResolvedPairCount = pairingAnalysis.ResolvedPairDecisions
            .Values
            .Select(x => $"{x.DebitTransactionId:N}:{x.CreditTransactionId:N}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        var rowsEvaluated = 0;
        var rowsTerminal = 0;
        var rowsClassifiedBankTransfer = 0;
        var rowsClassifiedSavingsTransfer = 0;
        var rowsNoMatch = 0;
        var rowsDeferredCounterparty = 0;
        var rowsDeferredContext = 0;
        var rowsRejectedAmbiguous = 0;
        var rowsRetryQueued = 0;
        var rowsReclassifiedLegacySavingsToCanonical = 0;
        var rowsLegacySavingsReferencesRemaining = 0;
        var rowsSavingsOutcomeMaterializedAsTransferWarning = 0;
        var changes = 0;

        var outcomesByTransactionId = new Dictionary<Guid, DeterministicClassificationOutcome>();
        foreach (var transactionId in targetSet)
        {
            if (!byId.TryGetValue(transactionId, out var transaction) || !featuresById.TryGetValue(transactionId, out var feature))
            {
                continue;
            }

            var outcome = BuildOutcome(
                transaction,
                feature,
                linkedPairs,
                pairingAnalysis.ResolvedPairDecisions,
                pairingAnalysis.PendingDecisions,
                now,
                hasFullSameUserCounterpartyUniverse);

            rowsEvaluated++;
            outcomesByTransactionId[transactionId] = outcome;

            if (outcome.Terminal)
            {
                rowsTerminal++;
            }

            if (outcome.RelationshipType == "internal_transfer" && outcome.Status == DeterministicClassificationStatus.ClassifiedMatchedRule)
            {
                rowsClassifiedBankTransfer++;
            }
            else if (outcome.RelationshipType == "savings_transfer" && outcome.Status == DeterministicClassificationStatus.ClassifiedMatchedRule)
            {
                rowsClassifiedSavingsTransfer++;
            }

            switch (outcome.Status)
            {
                case DeterministicClassificationStatus.EvaluatedNoMatchingRule:
                    rowsNoMatch++;
                    break;
                case DeterministicClassificationStatus.DeferredWaitingForCounterparty:
                    rowsDeferredCounterparty++;
                    break;
                case DeterministicClassificationStatus.DeferredWaitingForMoreContext:
                    rowsDeferredContext++;
                    break;
                case DeterministicClassificationStatus.RejectedAmbiguousMatch:
                    rowsRejectedAmbiguous++;
                    break;
            }

            if (outcome.RetryEligible)
            {
                rowsRetryQueued++;
            }

            var hadLegacySavingsReferenceBeforeApply =
                transaction.DeterministicClassificationSubcategoryId == DeterministicCategorizationConstants.LegacyTransferDomainSavingsTransferSubcategoryId
                || transaction.TaxonomySubcategoryId == DeterministicCategorizationConstants.LegacyTransferDomainSavingsTransferSubcategoryId;
            var isSavingsMatchedOutcome =
                outcome.Status == DeterministicClassificationStatus.ClassifiedMatchedRule
                && string.Equals(outcome.RelationshipType, "savings_transfer", StringComparison.Ordinal);
            var landedOnCanonicalSavingsTarget =
                isSavingsMatchedOutcome
                && outcome.ClassificationCategoryId == DeterministicCategorizationConstants.SavingsAndInvestmentsCategoryId
                && outcome.ClassificationSubcategoryId == DeterministicCategorizationConstants.GeneralSavingsTransferSubcategoryId;

            if (landedOnCanonicalSavingsTarget)
            {
                logger.LogDebug(
                    "Deterministic savings outcome landed on canonical savings target transactionId={TransactionId} categoryId={CategoryId} subcategoryId={SubcategoryId}",
                    transaction.Id,
                    outcome.ClassificationCategoryId,
                    outcome.ClassificationSubcategoryId);
            }

            var changedByOutcome = ApplyClassificationOutcome(transaction, feature, outcome, now);
            if (changedByOutcome)
            {
                changes++;
            }

            if (changedByOutcome && landedOnCanonicalSavingsTarget && hadLegacySavingsReferenceBeforeApply)
            {
                rowsReclassifiedLegacySavingsToCanonical++;
            }

            if (transaction.DeterministicClassificationSubcategoryId == DeterministicCategorizationConstants.LegacyTransferDomainSavingsTransferSubcategoryId
                || transaction.TaxonomySubcategoryId == DeterministicCategorizationConstants.LegacyTransferDomainSavingsTransferSubcategoryId)
            {
                rowsLegacySavingsReferencesRemaining++;
            }

            if (isSavingsMatchedOutcome && transaction.TaxonomyDomainId == ExpenseTaxonomyService.TransferDomainId)
            {
                rowsSavingsOutcomeMaterializedAsTransferWarning++;
                logger.LogWarning(
                    "Savings outcome resolved but transaction taxonomy still points to Transfers transactionId={TransactionId} taxonomyDomainId={TaxonomyDomainId} taxonomyCategoryId={TaxonomyCategoryId} taxonomySubcategoryId={TaxonomySubcategoryId} metadataUpdatedUtc={MetadataUpdatedUtc}",
                    transaction.Id,
                    transaction.TaxonomyDomainId,
                    transaction.TaxonomyCategoryId,
                    transaction.TaxonomySubcategoryId,
                    transaction.MetadataUpdatedUtc);
            }

            logger.LogDebug(
                "Deterministic classification decision transactionId={TransactionId} finalState={FinalState} ruleKey={RuleKey} reasonCode={ReasonCode} linkedTransactionId={LinkedTransactionId} score={MatchScore}",
                transaction.Id,
                outcome.Status,
                outcome.RuleKey,
                outcome.ReasonCode,
                outcome.LinkedTransactionId,
                outcome.MatchScore);
        }

        if (rowsReclassifiedLegacySavingsToCanonical > 0
            || rowsLegacySavingsReferencesRemaining > 0
            || rowsSavingsOutcomeMaterializedAsTransferWarning > 0)
        {
            logger.LogInformation(
                "Savings taxonomy rehome diagnostics rowsReclassifiedLegacyToCanonical={RowsReclassifiedLegacyToCanonical} rowsWithLegacySavingsReferencesRemaining={RowsWithLegacySavingsReferencesRemaining} savingsOutcomeMaterializedAsTransferWarnings={SavingsOutcomeMaterializedAsTransferWarnings}",
                rowsReclassifiedLegacySavingsToCanonical,
                rowsLegacySavingsReferencesRemaining,
                rowsSavingsOutcomeMaterializedAsTransferWarning);
        }

        var relationshipRowsUpserted = await ApplyRelationshipMetadataAsync(
            outcomesByTransactionId,
            linkedAccountsByFinancialAccountId.ToDictionary(x => x.Key, x => x.Value.ConnectionId),
            now,
            cancellationToken);

        stopwatch.Stop();
        RecordMetrics(
            stopwatch.Elapsed.TotalMilliseconds,
            rowsEvaluated,
            rowsTerminal,
            rowsClassifiedBankTransfer,
            rowsClassifiedSavingsTransfer,
            rowsDeferredCounterparty + rowsDeferredContext,
            rowsRejectedAmbiguous,
            pairingAnalysis.CandidateEdgeCount,
            (linkedPairs.Count / 2) + deterministicResolvedPairCount);

        return new DeterministicCategorizationSummary(
            RowsSelected: targetSet.Count,
            RowsEvaluated: rowsEvaluated,
            RowsTerminal: rowsTerminal,
            RowsClassifiedBankTransfer: rowsClassifiedBankTransfer,
            RowsClassifiedSavingsTransfer: rowsClassifiedSavingsTransfer,
            RowsNoMatch: rowsNoMatch,
            RowsDeferredCounterparty: rowsDeferredCounterparty,
            RowsDeferredContext: rowsDeferredContext,
            RowsRejectedAmbiguous: rowsRejectedAmbiguous,
            RowsRetryQueued: rowsRetryQueued,
            PairingAttemptCount: pairingAnalysis.CandidateEdgeCount,
            PairingSuccessCount: (linkedPairs.Count / 2) + deterministicResolvedPairCount,
            RelationshipRowsUpserted: relationshipRowsUpserted,
            HasChanges: changes > 0 || relationshipRowsUpserted > 0);
    }

    private DeterministicClassificationOutcome BuildOutcome(
        Transaction transaction,
        DeterministicTransactionFeature feature,
        IReadOnlyDictionary<Guid, Guid> linkedPairs,
        IReadOnlyDictionary<Guid, TransferPairDecision> resolvedPairDecisions,
        IReadOnlyDictionary<Guid, TransferPendingDecision> pendingDecisions,
        DateTime now,
        bool hasFullSameUserCounterpartyUniverse)
    {
        linkedPairs.TryGetValue(transaction.Id, out var linkedCounterpartId);
        var isLinkedInternal =
            transaction.TransferKind == TransactionTransferKind.LinkedInternal
            && transaction.LinkedTransferTransactionId.HasValue
            && linkedCounterpartId == transaction.LinkedTransferTransactionId.Value;

        if (isLinkedInternal)
        {
            var score = transaction.TransferMatchConfidenceScore ?? 10;
            var reasonCode = ResolveLinkedReasonCode(transaction.TransferMatchReason);
            var groupId = SavingsTransferClassifier.BuildPairGroupId(transaction.Id, linkedCounterpartId);
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.ClassifiedMatchedRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: "bank_transfer.linked_pair_v3",
                ReasonCode: reasonCode,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "bank_account_transfer",
                    pass = "legacy_linked_pair",
                    linkedCounterpartId,
                    transaction.TransferMatchReason,
                    score
                }),
                MatchScore: score,
                ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
                ClassificationSubcategoryId: ExpenseTaxonomyService.TransferDefaultSubcategoryId,
                LinkedTransactionId: linkedCounterpartId,
                RelationshipType: "internal_transfer",
                RelationshipGroupId: groupId);
        }

        if (resolvedPairDecisions.TryGetValue(transaction.Id, out var resolvedDecision))
        {
            var counterpartId = resolvedDecision.DebitTransactionId == transaction.Id
                ? resolvedDecision.CreditTransactionId
                : resolvedDecision.DebitTransactionId;
            var groupId = SavingsTransferClassifier.BuildPairGroupId(transaction.Id, counterpartId);
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.ClassifiedMatchedRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: resolvedDecision.RuleKey,
                ReasonCode: resolvedDecision.ReasonCode,
                EvidenceJson: resolvedDecision.EvidenceJson,
                MatchScore: resolvedDecision.Score,
                ClassificationCategoryId: ExpenseTaxonomyService.TransferDefaultCategoryId,
                ClassificationSubcategoryId: ExpenseTaxonomyService.TransferDefaultSubcategoryId,
                LinkedTransactionId: counterpartId,
                RelationshipType: "internal_transfer",
                RelationshipGroupId: groupId);
        }

        var hasPendingTransferDecision = pendingDecisions.TryGetValue(transaction.Id, out var pendingDecision);
        if (hasPendingTransferDecision
            && pendingDecision!.Status != DeterministicClassificationStatus.EvaluatedNoMatchingRule)
        {
            if (TryFinalizeExpiredDeferredDecision(
                    transaction,
                    feature,
                    pendingDecision,
                    now,
                    hasFullSameUserCounterpartyUniverse,
                    out var finalizedDeferredDecision))
            {
                return finalizedDeferredDecision;
            }

            return BuildTransferPendingOutcome(pendingDecision);
        }

        var legacySavings =
            transaction.TransferKind is TransactionTransferKind.SavingsRoundup
                or TransactionTransferKind.SavingsManualDeposit
                or TransactionTransferKind.SavingsManualWithdrawal;

        var savingsRoutingDecision = savingsRoutingPolicy.Evaluate(feature, legacySavings);

        if (savingsRoutingDecision.ShouldEvaluate)
        {
            var savingsOutcome = savingsTransferClassifier.Classify(
                feature,
                savingsRoutingDecision,
                hasLegacySavingsMarker: legacySavings);

            if (savingsOutcome is not null)
            {
                return savingsOutcome;
            }
        }

        if (hasPendingTransferDecision)
        {
            if (TryFinalizeExpiredDeferredDecision(
                    transaction,
                    feature,
                    pendingDecision!,
                    now,
                    hasFullSameUserCounterpartyUniverse,
                    out var finalizedDeferredDecision))
            {
                return finalizedDeferredDecision;
            }

            return BuildTransferPendingOutcome(pendingDecision!);
        }

        if (!savingsRoutingDecision.ShouldEvaluate
            && string.Equals(savingsRoutingDecision.BlockedReason, "merchant_likelihood_veto", StringComparison.Ordinal))
        {
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.EvaluatedNoMatchingRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: "savings_transfer.rejected_merchant_veto_v5",
                ReasonCode: DeterministicClassificationReasonCodes.SavingsRejectedMerchantLikelihoodVeto,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "savings_transfer",
                    routeDecision = DeterministicClassificationReasonCodes.SavingsRejectedMerchantLikelihoodVeto,
                    savingsRoutingAllowed = savingsRoutingDecision.ShouldEvaluate,
                    savingsRoutingTier = savingsRoutingDecision.RoutingTier,
                    providerStructuralSupport = savingsRoutingDecision.ProviderStructuralSupport,
                    providerProductSupport = savingsRoutingDecision.ProviderProductSupport,
                    contextualSupport = savingsRoutingDecision.ContextualSupport,
                    positiveSavingsEvidence = savingsRoutingDecision.PositiveSavingsEvidence,
                    repetitionStrength = savingsRoutingDecision.RepetitionStrength,
                    strongPhraseSupport = savingsRoutingDecision.StrongPhraseSupport,
                    weakSupportOnlySignalsPresent = savingsRoutingDecision.WeakSupportOnlySignalsPresent,
                    legacySupportOnly = savingsRoutingDecision.LegacySupportOnly,
                    merchantLikelihoodScore = savingsRoutingDecision.MerchantLikelihoodScore,
                    merchantLikelihoodVeto = savingsRoutingDecision.MerchantLikelihoodVeto,
                    merchantVetoOverridden = savingsRoutingDecision.MerchantVetoOverridden,
                    merchantEvidenceClasses = savingsRoutingDecision.MerchantEvidenceClasses,
                    positiveEvidenceClasses = savingsRoutingDecision.PositiveEvidenceClasses,
                    amountRiskModifier = savingsRoutingDecision.AmountRiskModifier,
                    externalCounterpartyRisk = savingsRoutingDecision.ExternalCounterpartyRisk,
                    routingBlockedReason = savingsRoutingDecision.BlockedReason,
                    finalReasonCode = DeterministicClassificationReasonCodes.SavingsRejectedMerchantLikelihoodVeto,
                    transferSignals = feature.HasTransferKeyword || feature.HasProviderTransferHint,
                    savingsSignals = feature.HasSavingsKeyword || feature.HasStrongSavingsKeyword,
                    feature.NearbyMerchantOutflowCount,
                    feature.RepeatedSmallAuxiliaryOutflowPatternCount,
                    providerSpecificReferenceTokenCount = feature.NarrativeSignals.ProviderSpecificReferenceTokens.Count,
                    paymentSystemMarkerCount = feature.NarrativeSignals.PaymentSystemMarkers.Count,
                    merchantLikeTokenCount = feature.NarrativeSignals.MerchantLikeTokens.Count,
                    legacySignalSupportOnly = legacySavings
                }),
                MatchScore: null,
                ClassificationCategoryId: null,
                ClassificationSubcategoryId: null,
                LinkedTransactionId: null,
                RelationshipType: null,
                RelationshipGroupId: null);
        }

        if (savingsRoutingDecision.ShouldEvaluate)
        {
            return new DeterministicClassificationOutcome(
                DeterministicClassificationStatus.EvaluatedNoMatchingRule,
                Terminal: true,
                RetryEligible: false,
                RuleKey: "savings_transfer.rejected_insufficient_context_v5",
                ReasonCode: DeterministicClassificationReasonCodes.SavingsRejectedInsufficientContext,
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    family = "savings_transfer",
                    routeDecision = DeterministicClassificationReasonCodes.SavingsRejectedInsufficientContext,
                    savingsRoutingAllowed = savingsRoutingDecision.ShouldEvaluate,
                    savingsRoutingTier = savingsRoutingDecision.RoutingTier,
                    providerStructuralSupport = savingsRoutingDecision.ProviderStructuralSupport,
                    providerProductSupport = savingsRoutingDecision.ProviderProductSupport,
                    contextualSupport = savingsRoutingDecision.ContextualSupport,
                    positiveSavingsEvidence = savingsRoutingDecision.PositiveSavingsEvidence,
                    repetitionStrength = savingsRoutingDecision.RepetitionStrength,
                    strongPhraseSupport = savingsRoutingDecision.StrongPhraseSupport,
                    weakSupportOnlySignalsPresent = savingsRoutingDecision.WeakSupportOnlySignalsPresent,
                    legacySupportOnly = savingsRoutingDecision.LegacySupportOnly,
                    merchantLikelihoodScore = savingsRoutingDecision.MerchantLikelihoodScore,
                    merchantLikelihoodVeto = savingsRoutingDecision.MerchantLikelihoodVeto,
                    merchantVetoOverridden = savingsRoutingDecision.MerchantVetoOverridden,
                    merchantEvidenceClasses = savingsRoutingDecision.MerchantEvidenceClasses,
                    positiveEvidenceClasses = savingsRoutingDecision.PositiveEvidenceClasses,
                    amountRiskModifier = savingsRoutingDecision.AmountRiskModifier,
                    externalCounterpartyRisk = savingsRoutingDecision.ExternalCounterpartyRisk,
                    routingBlockedReason = savingsRoutingDecision.BlockedReason,
                    finalReasonCode = DeterministicClassificationReasonCodes.SavingsRejectedInsufficientContext,
                    transferSignals = feature.HasTransferKeyword || feature.HasProviderTransferHint,
                    savingsSignals = feature.HasSavingsKeyword || feature.HasStrongSavingsKeyword,
                    feature.NearbyMerchantOutflowCount,
                    feature.RepeatedSmallAuxiliaryOutflowPatternCount,
                    providerSpecificReferenceTokenCount = feature.NarrativeSignals.ProviderSpecificReferenceTokens.Count,
                    paymentSystemMarkerCount = feature.NarrativeSignals.PaymentSystemMarkers.Count,
                    merchantLikeTokenCount = feature.NarrativeSignals.MerchantLikeTokens.Count,
                    legacySignalSupportOnly = legacySavings
                }),
                MatchScore: null,
                ClassificationCategoryId: null,
                ClassificationSubcategoryId: null,
                LinkedTransactionId: null,
                RelationshipType: null,
                RelationshipGroupId: null);
        }

        return new DeterministicClassificationOutcome(
            DeterministicClassificationStatus.EvaluatedNoMatchingRule,
            Terminal: true,
            RetryEligible: false,
            RuleKey: "generic.no_matching_supported_family_v3",
            ReasonCode: DeterministicClassificationReasonCodes.EvaluatedUnsupportedFamily,
            EvidenceJson: JsonSerializer.Serialize(new
            {
                family = "none",
                savingsRoutingAllowed = savingsRoutingDecision.ShouldEvaluate,
                savingsRoutingTier = savingsRoutingDecision.RoutingTier,
                providerStructuralSupport = savingsRoutingDecision.ProviderStructuralSupport,
                providerProductSupport = savingsRoutingDecision.ProviderProductSupport,
                contextualSupport = savingsRoutingDecision.ContextualSupport,
                positiveSavingsEvidence = savingsRoutingDecision.PositiveSavingsEvidence,
                repetitionStrength = savingsRoutingDecision.RepetitionStrength,
                strongPhraseSupport = savingsRoutingDecision.StrongPhraseSupport,
                weakSupportOnlySignalsPresent = savingsRoutingDecision.WeakSupportOnlySignalsPresent,
                legacySupportOnly = savingsRoutingDecision.LegacySupportOnly,
                merchantLikelihoodScore = savingsRoutingDecision.MerchantLikelihoodScore,
                merchantLikelihoodVeto = savingsRoutingDecision.MerchantLikelihoodVeto,
                merchantVetoOverridden = savingsRoutingDecision.MerchantVetoOverridden,
                merchantEvidenceClasses = savingsRoutingDecision.MerchantEvidenceClasses,
                positiveEvidenceClasses = savingsRoutingDecision.PositiveEvidenceClasses,
                amountRiskModifier = savingsRoutingDecision.AmountRiskModifier,
                externalCounterpartyRisk = savingsRoutingDecision.ExternalCounterpartyRisk,
                routingBlockedReason = savingsRoutingDecision.BlockedReason,
                finalReasonCode = DeterministicClassificationReasonCodes.EvaluatedUnsupportedFamily,
                transferKeyword = feature.HasTransferKeyword,
                savingsKeyword = feature.HasSavingsKeyword,
                providerHint = feature.HasProviderTransferHint,
                providerSpecificReferenceTokenCount = feature.NarrativeSignals.ProviderSpecificReferenceTokens.Count,
                paymentSystemMarkerCount = feature.NarrativeSignals.PaymentSystemMarkers.Count,
                merchantLikeTokenCount = feature.NarrativeSignals.MerchantLikeTokens.Count
            }),
            MatchScore: null,
            ClassificationCategoryId: null,
            ClassificationSubcategoryId: null,
            LinkedTransactionId: null,
            RelationshipType: null,
            RelationshipGroupId: null);
    }

    private static DeterministicClassificationOutcome BuildTransferPendingOutcome(TransferPendingDecision pending)
    {
        return new DeterministicClassificationOutcome(
            pending.Status,
            Terminal: DeterministicClassificationRetryPlanner.IsTerminal(pending.Status),
            RetryEligible: pending.RetryEligible,
            RuleKey: "bank_transfer.deferred_or_rejected_v3",
            ReasonCode: pending.ReasonCode,
            EvidenceJson: pending.EvidenceJson,
            MatchScore: null,
            ClassificationCategoryId: null,
            ClassificationSubcategoryId: null,
            LinkedTransactionId: null,
            RelationshipType: null,
            RelationshipGroupId: null);
    }

    private static bool TryFinalizeExpiredDeferredDecision(
        Transaction transaction,
        DeterministicTransactionFeature feature,
        TransferPendingDecision pendingDecision,
        DateTime now,
        bool hasFullSameUserCounterpartyUniverse,
        out DeterministicClassificationOutcome finalizedOutcome)
    {
        finalizedOutcome = default!;
        var isDeferred = pendingDecision.Status is DeterministicClassificationStatus.DeferredWaitingForCounterparty
            or DeterministicClassificationStatus.DeferredWaitingForMoreContext;
        if (!isDeferred)
        {
            return false;
        }

        // Keep true pending/unbooked rows deferred for future settlement context.
        if (feature.IsPending || !feature.IsBooked)
        {
            return false;
        }

        var deferAgeHours = Math.Max(0d, (now - transaction.BookedAtUtc).TotalHours);
        var expirationHours = pendingDecision.Status == DeterministicClassificationStatus.DeferredWaitingForCounterparty
            ? CounterpartyDeferExpiryHours
            : ContextDeferExpiryHours;
        var deferExpired = deferAgeHours >= expirationHours;
        var fullUniversePresent = hasFullSameUserCounterpartyUniverse && feature.HasCounterpartyAccounts;
        if (!deferExpired && !fullUniversePresent)
        {
            return false;
        }

        var finalizeAsAmbiguous = pendingDecision.IsDuplicateClusterMember
                                  || pendingDecision.CandidateCount > 1
                                  || pendingDecision.Status == DeterministicClassificationStatus.DeferredWaitingForMoreContext;
        var finalStatus = finalizeAsAmbiguous
            ? DeterministicClassificationStatus.RejectedAmbiguousMatch
            : DeterministicClassificationStatus.EvaluatedNoMatchingRule;
        var reasonCode = finalizeAsAmbiguous
            ? DeterministicClassificationReasonCodes.TransferDeferredExpiredAmbiguous
            : DeterministicClassificationReasonCodes.TransferDeferredExpiredNoCounterpart;
        var ruleKey = "bank_transfer.deferred_expiry_terminalized_v1";
        var evidence = JsonSerializer.Serialize(new
        {
            family = "bank_account_transfer",
            resolutionOutcome = "deferred_invalidated_to_terminal",
            originalDeferredStatus = pendingDecision.Status.ToString(),
            pendingDecision.ReasonCode,
            pendingDecision.CandidateCount,
            pendingDecision.IsDuplicateClusterMember,
            pendingDecision.DuplicateClusterSize,
            deferAgeHours = Math.Round(deferAgeHours, 2, MidpointRounding.AwayFromZero),
            deferExpirationHours = expirationHours,
            deferExpired,
            fullCounterpartyUniversePresent = fullUniversePresent,
            feature.HasCounterpartyAccounts,
            feature.IsBooked,
            feature.IsPending,
            finalStatus = finalStatus.ToString(),
            finalReasonCode = reasonCode,
            pendingEvidence = pendingDecision.EvidenceJson
        });

        finalizedOutcome = new DeterministicClassificationOutcome(
            finalStatus,
            Terminal: true,
            RetryEligible: false,
            RuleKey: ruleKey,
            ReasonCode: reasonCode,
            EvidenceJson: evidence,
            MatchScore: null,
            ClassificationCategoryId: null,
            ClassificationSubcategoryId: null,
            LinkedTransactionId: null,
            RelationshipType: null,
            RelationshipGroupId: null);
        return true;
    }

    private bool ApplyClassificationOutcome(
        Transaction transaction,
        DeterministicTransactionFeature feature,
        DeterministicClassificationOutcome outcome,
        DateTime now)
    {
        var terminal = outcome.Terminal;
        var retryEligible = retryPlanner.IsRetryEligible(outcome.Status, feature.HasCounterpartyAccounts, feature.IsPending)
                            || outcome.RetryEligible;
        var sourceSignature = normalizationService.BuildSourceSignature(
            transaction.Amount,
            transaction.Currency,
            transaction.BookedAtUtc,
            feature.NormalizedDescription,
            outcome.LinkedTransactionId ?? transaction.LinkedTransferTransactionId);
        var shouldMaterializeLegacyInternalTransfer =
            outcome.Status == DeterministicClassificationStatus.ClassifiedMatchedRule
            && string.Equals(outcome.RelationshipType, "internal_transfer", StringComparison.Ordinal)
            && outcome.LinkedTransactionId.HasValue;
        var shouldMaterializeSavingsTransfer =
            outcome.Status == DeterministicClassificationStatus.ClassifiedMatchedRule
            && string.Equals(outcome.RelationshipType, "savings_transfer", StringComparison.Ordinal)
            && !transaction.MetadataUpdatedUtc.HasValue;

        var materializedTransferCategoryId = outcome.ClassificationCategoryId ?? ExpenseTaxonomyService.TransferDefaultCategoryId;
        var materializedTransferSubcategoryId = outcome.ClassificationSubcategoryId ?? ExpenseTaxonomyService.TransferDefaultSubcategoryId;
        var materializedSavingsCategoryId = outcome.ClassificationCategoryId ?? DeterministicCategorizationConstants.SavingsAndInvestmentsCategoryId;
        var materializedSavingsSubcategoryId = outcome.ClassificationSubcategoryId ?? DeterministicCategorizationConstants.GeneralSavingsTransferSubcategoryId;

        var targetTransferKind = shouldMaterializeLegacyInternalTransfer
            ? TransactionTransferKind.LinkedInternal
            : transaction.TransferKind;
        var targetLinkedTransferTransactionId = shouldMaterializeLegacyInternalTransfer
            ? outcome.LinkedTransactionId
            : transaction.LinkedTransferTransactionId;
        var targetLinkedTransferMatchedUtc = shouldMaterializeLegacyInternalTransfer
            ? transaction.LinkedTransferMatchedUtc ?? now
            : transaction.LinkedTransferMatchedUtc;
        var targetTaxonomyDomainId = shouldMaterializeLegacyInternalTransfer
            ? ExpenseTaxonomyService.TransferDomainId
            : shouldMaterializeSavingsTransfer
                ? ExpenseTaxonomyService.SavingsAndInvestmentsDomainId
            : transaction.TaxonomyDomainId;
        var targetTaxonomyCategoryId = shouldMaterializeLegacyInternalTransfer
            ? materializedTransferCategoryId
            : shouldMaterializeSavingsTransfer
                ? materializedSavingsCategoryId
            : transaction.TaxonomyCategoryId;
        var targetTaxonomySubcategoryId = shouldMaterializeLegacyInternalTransfer
            ? materializedTransferSubcategoryId
            : shouldMaterializeSavingsTransfer
                ? materializedSavingsSubcategoryId
            : transaction.TaxonomySubcategoryId;
        var targetTransferMatchReason = shouldMaterializeLegacyInternalTransfer
            ? outcome.ReasonCode ?? outcome.RuleKey
            : transaction.TransferMatchReason;
        var targetTransferMatchConfidenceScore = shouldMaterializeLegacyInternalTransfer
            ? outcome.MatchScore
            : transaction.TransferMatchConfidenceScore;
        var targetTransferMatchConfidenceTier = shouldMaterializeLegacyInternalTransfer
            ? "deterministic_match"
            : transaction.TransferMatchConfidenceTier;

        var changed =
            transaction.DeterministicClassificationStatus != outcome.Status
            || transaction.DeterministicClassificationVersion != DeterministicCategorizationConstants.CurrentClassificationVersion
            || !string.Equals(transaction.DeterministicClassificationRuleKey, outcome.RuleKey, StringComparison.Ordinal)
            || transaction.DeterministicClassificationCategoryId != outcome.ClassificationCategoryId
            || transaction.DeterministicClassificationSubcategoryId != outcome.ClassificationSubcategoryId
            || transaction.DeterministicLinkedTransactionId != outcome.LinkedTransactionId
            || !string.Equals(transaction.DeterministicRelationshipType, outcome.RelationshipType, StringComparison.Ordinal)
            || transaction.DeterministicRelationshipGroupId != outcome.RelationshipGroupId
            || transaction.DeterministicMatchScore != outcome.MatchScore
            || !string.Equals(transaction.DeterministicReasonCode, outcome.ReasonCode, StringComparison.Ordinal)
            || !string.Equals(transaction.DeterministicReasonDetailJson, outcome.EvidenceJson, StringComparison.Ordinal)
            || transaction.DeterministicClassificationTerminal != terminal
            || transaction.DeterministicDeferredRetryEligible != retryEligible
            || transaction.NeedsDeterministicReclassification
            || !string.Equals(transaction.DeterministicSourceSignature, sourceSignature, StringComparison.Ordinal)
            || transaction.TransferKind != targetTransferKind
            || transaction.LinkedTransferTransactionId != targetLinkedTransferTransactionId
            || transaction.LinkedTransferMatchedUtc != targetLinkedTransferMatchedUtc
            || transaction.TaxonomyDomainId != targetTaxonomyDomainId
            || transaction.TaxonomyCategoryId != targetTaxonomyCategoryId
            || transaction.TaxonomySubcategoryId != targetTaxonomySubcategoryId
            || transaction.TransferMatchConfidenceScore != targetTransferMatchConfidenceScore
            || !string.Equals(transaction.TransferMatchConfidenceTier, targetTransferMatchConfidenceTier, StringComparison.Ordinal)
            || !string.Equals(transaction.TransferMatchReason, targetTransferMatchReason, StringComparison.Ordinal);

        if (!changed)
        {
            return false;
        }

        transaction.DeterministicClassificationStatus = outcome.Status;
        transaction.DeterministicClassificationVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;
        transaction.DeterministicClassificationRuleKey = outcome.RuleKey;
        transaction.DeterministicClassificationCategoryId = outcome.ClassificationCategoryId;
        transaction.DeterministicClassificationSubcategoryId = outcome.ClassificationSubcategoryId;
        transaction.DeterministicLinkedTransactionId = outcome.LinkedTransactionId;
        transaction.DeterministicRelationshipType = outcome.RelationshipType;
        transaction.DeterministicRelationshipGroupId = outcome.RelationshipGroupId;
        transaction.DeterministicMatchScore = outcome.MatchScore;
        transaction.DeterministicReasonCode = outcome.ReasonCode;
        transaction.DeterministicReasonDetailJson = outcome.EvidenceJson;
        transaction.DeterministicClassificationTerminal = terminal;
        transaction.DeterministicDeferredRetryEligible = retryEligible;
        transaction.DeterministicLastRetryConsideredUtc = now;
        transaction.DeterministicClassificationEvaluatedUtc = now;
        transaction.NeedsDeterministicReclassification = false;
        transaction.DeterministicSourceSignature = sourceSignature;
        transaction.TransferKind = targetTransferKind;
        transaction.LinkedTransferTransactionId = targetLinkedTransferTransactionId;
        transaction.LinkedTransferMatchedUtc = targetLinkedTransferMatchedUtc;
        transaction.TaxonomyDomainId = targetTaxonomyDomainId;
        transaction.TaxonomyCategoryId = targetTaxonomyCategoryId;
        transaction.TaxonomySubcategoryId = targetTaxonomySubcategoryId;
        transaction.TransferMatchConfidenceScore = targetTransferMatchConfidenceScore;
        transaction.TransferMatchConfidenceTier = targetTransferMatchConfidenceTier;
        transaction.TransferMatchReason = targetTransferMatchReason;

        // Keep historical compatibility for existing enrichment/version progress pathways.
        transaction.DeterministicEnrichmentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;
        transaction.LastDeterministicEnrichedUtc = now;

        return true;
    }

    private async Task<int> ApplyRelationshipMetadataAsync(
        IReadOnlyDictionary<Guid, DeterministicClassificationOutcome> outcomesByTransactionId,
        IReadOnlyDictionary<Guid, Guid> accountToConnectionId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (outcomesByTransactionId.Count == 0)
        {
            return 0;
        }

        var transactionIds = outcomesByTransactionId.Keys.ToArray();
        var relationshipRows = await dbContext.TransactionRelationships
            .Where(x =>
                transactionIds.Contains(x.SourceTransactionId)
                || (x.TargetTransactionId.HasValue && transactionIds.Contains(x.TargetTransactionId.Value)))
            .ToListAsync(cancellationToken);

        var touched = 0;
        foreach (var relationship in relationshipRows)
        {
            if (!outcomesByTransactionId.TryGetValue(relationship.SourceTransactionId, out var outcome))
            {
                continue;
            }

            var pairingStatus = outcome.Status switch
            {
                DeterministicClassificationStatus.ClassifiedMatchedRule => "paired",
                DeterministicClassificationStatus.DeferredWaitingForCounterparty => "deferred_counterparty",
                DeterministicClassificationStatus.DeferredWaitingForMoreContext => "deferred_context",
                DeterministicClassificationStatus.RejectedAmbiguousMatch => "rejected_ambiguous",
                _ => "evaluated"
            };

            accountToConnectionId.TryGetValue(relationship.SourceFinancialAccountId, out var sourceConnectionId);
            Guid? targetConnectionId = null;
            if (relationship.TargetFinancialAccountId.HasValue
                && accountToConnectionId.TryGetValue(relationship.TargetFinancialAccountId.Value, out var resolvedTargetConnection))
            {
                targetConnectionId = resolvedTargetConnection;
            }

            var pairedUtc = outcome.Status == DeterministicClassificationStatus.ClassifiedMatchedRule ? now : (DateTime?)null;
            var changed = false;

            if (relationship.RelationshipGroupId != outcome.RelationshipGroupId)
            {
                relationship.RelationshipGroupId = outcome.RelationshipGroupId;
                changed = true;
            }

            if (!string.Equals(relationship.DeterministicRelationshipType, outcome.RelationshipType, StringComparison.Ordinal))
            {
                relationship.DeterministicRelationshipType = outcome.RelationshipType;
                changed = true;
            }

            if (!string.Equals(relationship.PairingStatus, pairingStatus, StringComparison.Ordinal))
            {
                relationship.PairingStatus = pairingStatus;
                changed = true;
            }

            if (!string.Equals(relationship.PairingRuleKey, outcome.RuleKey, StringComparison.Ordinal))
            {
                relationship.PairingRuleKey = outcome.RuleKey;
                changed = true;
            }

            if (!string.Equals(relationship.PairingEvidenceJson, outcome.EvidenceJson, StringComparison.Ordinal))
            {
                relationship.PairingEvidenceJson = outcome.EvidenceJson;
                changed = true;
            }

            if (relationship.SourceConnectionId != sourceConnectionId)
            {
                relationship.SourceConnectionId = sourceConnectionId;
                changed = true;
            }

            if (relationship.TargetConnectionId != targetConnectionId)
            {
                relationship.TargetConnectionId = targetConnectionId;
                changed = true;
            }

            if (relationship.PairedUtc != pairedUtc)
            {
                relationship.PairedUtc = pairedUtc;
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            relationship.UpdatedUtc = now;
            touched++;
        }

        return touched;
    }

    private static bool HasProviderTransferHint(string? transactionType)
    {
        var normalized = transactionType?.Trim().ToUpperInvariant();
        return normalized is "TRANSFER" or "PAYMENT";
    }

    private static Dictionary<Guid, Guid> BuildLinkedPairMap(IReadOnlyList<Transaction> rows, IReadOnlyDictionary<Guid, Transaction> byId)
    {
        var map = new Dictionary<Guid, Guid>();
        foreach (var row in rows)
        {
            if (row.TransferKind != TransactionTransferKind.LinkedInternal || !row.LinkedTransferTransactionId.HasValue)
            {
                continue;
            }

            if (!byId.TryGetValue(row.LinkedTransferTransactionId.Value, out var counterpart))
            {
                continue;
            }

            if (counterpart.LinkedTransferTransactionId != row.Id)
            {
                continue;
            }

            map[row.Id] = counterpart.Id;
        }

        return map;
    }

    private static string ResolveLinkedReasonCode(string? transferMatchReason)
    {
        var normalized = transferMatchReason?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DeterministicClassificationReasonCodes.MatchedExactInverseAmount;
        }

        if (normalized.Contains("mutual", StringComparison.Ordinal))
        {
            return DeterministicClassificationReasonCodes.MatchedMutualBestCandidate;
        }

        return DeterministicClassificationReasonCodes.MatchedExactInverseAmount;
    }

    private static DeterministicCategorizationSummary EmptySummary()
    {
        return new DeterministicCategorizationSummary(
            RowsSelected: 0,
            RowsEvaluated: 0,
            RowsTerminal: 0,
            RowsClassifiedBankTransfer: 0,
            RowsClassifiedSavingsTransfer: 0,
            RowsNoMatch: 0,
            RowsDeferredCounterparty: 0,
            RowsDeferredContext: 0,
            RowsRejectedAmbiguous: 0,
            RowsRetryQueued: 0,
            PairingAttemptCount: 0,
            PairingSuccessCount: 0,
            RelationshipRowsUpserted: 0,
            HasChanges: false);
    }

    private void RecordMetrics(
        double elapsedMs,
        int rowsEvaluated,
        int rowsTerminal,
        int rowsClassifiedBankTransfer,
        int rowsClassifiedSavingsTransfer,
        int rowsDeferred,
        int rowsAmbiguous,
        int pairingAttempts,
        int pairingSuccesses)
    {
        metrics.EvalTotal.Add(rowsEvaluated);
        metrics.EvalDurationMs.Record(elapsedMs);
        metrics.ClassifiedTotal.Add(rowsClassifiedBankTransfer, new KeyValuePair<string, object?>("family", "bank_account_transfer"));
        metrics.ClassifiedTotal.Add(rowsClassifiedSavingsTransfer, new KeyValuePair<string, object?>("family", "savings_transfer"));
        if (rowsDeferred > 0)
        {
            metrics.DeferredTotal.Add(rowsDeferred, new KeyValuePair<string, object?>("reason", "deferred"));
        }

        if (rowsAmbiguous > 0)
        {
            metrics.AmbiguousTotal.Add(rowsAmbiguous);
        }

        var terminalRatio = rowsEvaluated == 0 ? 1d : Math.Clamp(rowsTerminal / (double)rowsEvaluated, 0d, 1d);
        metrics.TerminalRatio.Record(terminalRatio);

        if (pairingAttempts > 0)
        {
            var pairingRatio = Math.Clamp(pairingSuccesses / (double)pairingAttempts, 0d, 1d);
            metrics.PairingSuccessRatio.Record(pairingRatio);
        }
    }

    private sealed record LinkedAccountDeterministicContext(
        Guid ConnectionId,
        string? ProviderId,
        string? ProviderDisplayName);

    private async Task<Dictionary<Guid, LinkedAccountDeterministicContext>> LoadLinkedAccountScopeAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && x.Connection != null
                && x.Connection.UserId == userId)
            .Select(x => new
            {
                FinancialAccountId = x.FinancialAccountId!.Value,
                x.ConnectionId,
                ProviderId = x.Connection != null ? x.Connection.ProviderId : null,
                ProviderDisplayName = x.Connection != null ? x.Connection.ProviderDisplayName : null
            })
            .Distinct()
            .ToDictionaryAsync(
                x => x.FinancialAccountId,
                x => new LinkedAccountDeterministicContext(
                    x.ConnectionId,
                    x.ProviderId,
                    x.ProviderDisplayName),
                cancellationToken);
    }

    private async Task<List<Transaction>> LoadTransactionsWithContextAsync(
        IReadOnlyCollection<Guid> financialAccountIds,
        DateTime contextStartUtc,
        DateTime contextEndUtc,
        CancellationToken cancellationToken)
    {
        return await dbContext.Transactions
            .Where(x =>
                financialAccountIds.Contains(x.FinancialAccountId)
                && x.BookedAtUtc >= contextStartUtc
                && x.BookedAtUtc <= contextEndUtc)
            .OrderByDescending(x => x.BookedAtUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);
    }

    private sealed record NormalizedContextRow(string? TransactionType, string? TransactionStatus);

    private async Task<Dictionary<Guid, NormalizedContextRow>> LoadNormalizedContextAsync(
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.NormalizedBankTransactions
            .AsNoTracking()
            .Where(x => x.ProjectedTransactionId.HasValue && transactionIds.Contains(x.ProjectedTransactionId.Value))
            .Select(x => new
            {
                TransactionId = x.ProjectedTransactionId!.Value,
                x.TransactionType,
                x.TransactionStatus,
                x.LastNormalizedUtc
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.TransactionId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(x => x.LastNormalizedUtc).First();
                    return new NormalizedContextRow(latest.TransactionType, latest.TransactionStatus);
                });
    }
}
