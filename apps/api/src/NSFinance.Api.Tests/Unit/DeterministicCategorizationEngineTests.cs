using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class DeterministicCategorizationEngineTests
{
    [Fact]
    public void NormalizeDescription_RemovesNoiseAndStandardizesWhitespace()
    {
        var normalization = new TransactionNormalizationService();

        var normalized = normalization.NormalizeDescription("  FLEXIBLE-CASH!!   Round-Up   Transfer   ");

        Assert.Equal("flexible cash round up transfer", normalized);
    }

    [Fact]
    public void HasSavingsKeyword_DetectsStrongPhraseSignals()
    {
        var normalization = new TransactionNormalizationService();
        var normalized = normalization.NormalizeDescription("Monthly round-up to flexible cash pocket");
        var tokens = normalization.Tokenize(normalized);

        var detected = normalization.HasSavingsKeyword(normalized, tokens);

        Assert.True(detected);
        Assert.True(normalization.HasStrongSavingsKeyword(normalized));
    }

    [Theory]
    [InlineData("Cash move to somewhere")]
    [InlineData("Flexible payment")]
    [InlineData("Fund allocation")]
    [InlineData("Pot transfer")]
    public void HasSavingsKeyword_GenericKeywordOnly_IsWeakSupportOnly(string description)
    {
        var normalization = new TransactionNormalizationService();
        var normalized = normalization.NormalizeDescription(description);
        var tokens = normalization.Tokenize(normalized);

        Assert.False(normalization.HasSavingsKeyword(normalized, tokens));
        Assert.True(normalization.HasWeakSavingsSupportKeyword(tokens));
    }

    [Fact]
    public void ExtractAccountHint_ReturnsTrailingDigitsOnly()
    {
        var normalization = new TransactionNormalizationService();

        var hint = normalization.ExtractAccountHint("transfer to account ending 7788 from current");

        Assert.Equal("7788", hint);
    }

    [Fact]
    public void ProviderCapabilityRegistry_ResolvesAibAndNatWestProfiles()
    {
        var registry = new ProviderCapabilityRegistry();
        var aib = registry.Resolve("ob-aib", "AIB");
        var natwest = registry.Resolve("ob-rbs", "RBS");
        var generic = registry.Resolve("ob-barclays", "Barclays");

        Assert.Equal("aib", aib.ProviderKey);
        Assert.True(aib.SupportsMachineReferenceTokens);
        Assert.Equal(DeterministicProviderTimestampPrecision.DateOnly, aib.TimestampPrecision);

        Assert.Equal("natwest_family", natwest.ProviderKey);
        Assert.True(natwest.SupportsPaymentSystemMarkers);
        Assert.True(natwest.SupportsProviderSpecificTransferMarkers);

        Assert.Equal("generic_known_provider", generic.ProviderKey);
        Assert.False(generic.IsProviderSpecificRule);
    }

    [Fact]
    public void NarrativeSignalExtractor_AibAndNatWestSignals_AreClassifiedByConfidence()
    {
        var extractor = new NarrativeSignalExtractor();
        var aibSignals = extractor.Extract(
            "AIB transfer IEABC998877 originator ref IEABC998877",
            "aib transfer ieabc998877 originator ref ieabc998877",
            new ProviderCapabilityRegistry().Resolve("ob-aib", "AIB"));
        var natwestSignals = extractor.Extract(
            "FP TO SAVINGS REF POT FPID ZXCVB12345",
            "fp to savings ref pot fpid zxcvb12345",
            new ProviderCapabilityRegistry().Resolve("ob-rbs", "RBS"));

        Assert.Contains("ieabc998877", aibSignals.ProviderSpecificReferenceTokens);
        Assert.Contains("ieabc998877", aibSignals.HighConfidenceTokens);
        Assert.Equal(NarrativeSignalConfidenceTier.HighConfidence, aibSignals.SignalConfidenceMap["ieabc998877"]);

        Assert.Contains("fpid", natwestSignals.PaymentSystemMarkers);
        Assert.Contains("zxcvb12345", natwestSignals.ProviderSpecificReferenceTokens);
        Assert.Equal(NarrativeSignalConfidenceTier.HighConfidence, natwestSignals.SignalConfidenceMap["zxcvb12345"]);
    }

    [Fact]
    public void FeatureExtractor_ComputesDirectionStatusCurrencyAndNearbyCounts()
    {
        var extractor = new TransactionFeatureExtractor(
            new TransactionNormalizationService(),
            new ProviderCapabilityRegistry(),
            new NarrativeSignalExtractor());
        var accountId = Guid.NewGuid();
        var bookedAt = new DateTime(2026, 03, 28, 10, 00, 00, DateTimeKind.Utc);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var rows = new List<TransactionFeatureExtractor.TransactionFeatureInputRow>
        {
            new(
                firstId,
                accountId,
                -100m,
                "gbp",
                bookedAt,
                bookedAt,
                "Bank transfer to savings 7788",
                "ob-aib",
                "AIB",
                "TRANSFER",
                "booked",
                HasProviderTransferHint: true,
                HasCounterpartyAccounts: true,
                StableSequence: 1L),
            new(
                secondId,
                Guid.NewGuid(),
                100m,
                "GBP",
                bookedAt.AddMinutes(30),
                bookedAt.AddMinutes(30),
                "Transfer from current 7788",
                "ob-revolut",
                "Revolut",
                "TRANSFER",
                "pending",
                HasProviderTransferHint: true,
                HasCounterpartyAccounts: true,
                StableSequence: 2L)
        };

        var features = extractor.BuildFeatures(rows);
        var outflow = features[firstId];
        var inflow = features[secondId];

        Assert.True(outflow.IsOutflow);
        Assert.False(outflow.IsInflow);
        Assert.True(outflow.IsBooked);
        Assert.False(outflow.IsPending);
        Assert.Equal("GBP", outflow.Currency);
        Assert.True(outflow.HasTransferKeyword);
        Assert.Equal(1, outflow.NearbySameAmountCount);
        Assert.Equal("outflow", outflow.Direction);

        Assert.True(inflow.IsInflow);
        Assert.False(inflow.IsOutflow);
        Assert.False(inflow.IsBooked);
        Assert.True(inflow.IsPending);
        Assert.Equal(1, inflow.NearbySameAmountCount);
        Assert.Equal("inflow", inflow.Direction);
    }

    [Fact]
    public void FeatureExtractor_ContextNearbySpend_IsSymmetricForPostingOrder()
    {
        var extractor = new TransactionFeatureExtractor(
            new TransactionNormalizationService(),
            new ProviderCapabilityRegistry(),
            new NarrativeSignalExtractor());
        var accountId = Guid.NewGuid();
        var baseUtc = new DateTime(2026, 03, 30, 10, 0, 0, DateTimeKind.Utc);
        var savingsId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var rows = new List<TransactionFeatureExtractor.TransactionFeatureInputRow>
        {
            new(
                savingsId,
                accountId,
                -0.60m,
                "EUR",
                baseUtc,
                baseUtc,
                "Aux jar sweep",
                "ob-revolut",
                "Revolut",
                "DEBIT",
                "booked",
                HasProviderTransferHint: false,
                HasCounterpartyAccounts: true,
                StableSequence: 1L),
            new(
                merchantId,
                accountId,
                -14.20m,
                "EUR",
                baseUtc.AddMinutes(6),
                baseUtc.AddMinutes(6),
                "Card purchase groceries",
                "ob-revolut",
                "Revolut",
                "DEBIT",
                "booked",
                HasProviderTransferHint: false,
                HasCounterpartyAccounts: true,
                StableSequence: 2L)
        };

        var features = extractor.BuildFeatures(rows);
        var savings = features[savingsId];

        Assert.True(savings.NearbyMerchantOutflowCount > 0);
    }

    [Fact]
    public void TransactionFeatureExtractor_NearbyMerchantSupport_IsSymmetricAroundCandidate()
    {
        var extractor = new TransactionFeatureExtractor(
            new TransactionNormalizationService(),
            new ProviderCapabilityRegistry(),
            new NarrativeSignalExtractor());
        var accountId = Guid.NewGuid();
        var savingsId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var baseUtc = new DateTime(2026, 03, 30, 12, 0, 0, DateTimeKind.Utc);
        var rows = new List<TransactionFeatureExtractor.TransactionFeatureInputRow>
        {
            new(
                savingsId,
                accountId,
                -0.45m,
                "EUR",
                baseUtc,
                baseUtc,
                "Aux move",
                "ob-revolut",
                "Revolut",
                "DEBIT",
                "booked",
                HasProviderTransferHint: false,
                HasCounterpartyAccounts: true,
                StableSequence: 1L),
            new(
                merchantId,
                accountId,
                -20m,
                "EUR",
                baseUtc.AddMinutes(5),
                baseUtc.AddMinutes(5),
                "Main purchase",
                "ob-revolut",
                "Revolut",
                "DEBIT",
                "booked",
                HasProviderTransferHint: false,
                HasCounterpartyAccounts: true,
                StableSequence: 2L)
        };

        var features = extractor.BuildFeatures(rows);
        Assert.True(features[savingsId].NearbyMerchantOutflowCount > 0);
    }

    [Fact]
    public void TransactionFeatureExtractor_RepeatedAuxiliarySupport_IsNotBackwardOnly()
    {
        var extractor = new TransactionFeatureExtractor(
            new TransactionNormalizationService(),
            new ProviderCapabilityRegistry(),
            new NarrativeSignalExtractor());
        var accountId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var futureAuxId = Guid.NewGuid();
        var futureMerchantId = Guid.NewGuid();
        var baseUtc = new DateTime(2026, 03, 20, 10, 0, 0, DateTimeKind.Utc);

        var rows = new List<TransactionFeatureExtractor.TransactionFeatureInputRow>
        {
            new(
                currentId,
                accountId,
                -0.55m,
                "EUR",
                baseUtc,
                baseUtc,
                "Aux pocket move now",
                "ob-revolut",
                "Revolut",
                "DEBIT",
                "booked",
                HasProviderTransferHint: false,
                HasCounterpartyAccounts: true,
                StableSequence: 1L),
            new(
                futureAuxId,
                accountId,
                -0.65m,
                "EUR",
                baseUtc.AddDays(1),
                baseUtc.AddDays(1),
                "Aux pocket move future",
                "ob-revolut",
                "Revolut",
                "TRANSFER",
                "booked",
                HasProviderTransferHint: true,
                HasCounterpartyAccounts: true,
                StableSequence: 2L),
            new(
                futureMerchantId,
                accountId,
                -17.25m,
                "EUR",
                baseUtc.AddDays(1).AddMinutes(2),
                baseUtc.AddDays(1).AddMinutes(2),
                "Main groceries future",
                "ob-revolut",
                "Revolut",
                "DEBIT",
                "booked",
                HasProviderTransferHint: false,
                HasCounterpartyAccounts: true,
                StableSequence: 3L)
        };

        var features = extractor.BuildFeatures(rows);
        Assert.True(features[currentId].RepeatedSmallAuxiliaryOutflowPatternCount > 0);
    }

    [Fact]
    public void TransferPairing_NoCandidatesAndPending_DefersForMoreContext()
    {
        var engine = new TransferPairingEngine();
        var feature = CreateFeature(
            signedAmount: -50m,
            bookedAtUtc: new DateTime(2026, 03, 20, 8, 0, 0, DateTimeKind.Utc),
            hasTransferKeyword: true,
            isBooked: false,
            isPending: true,
            hasCounterpartyAccounts: true);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature> { [feature.TransactionId] = feature },
            new HashSet<Guid>());

        var decision = analysis.PendingDecisions[feature.TransactionId];
        Assert.Equal(DeterministicClassificationStatus.DeferredWaitingForMoreContext, decision.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.DeferredPendingBookedContext, decision.ReasonCode);
        Assert.True(decision.RetryEligible);
    }

    [Fact]
    public void TransferPairing_SavingsKeywordOnly_DoesNotEnterTransferFamily()
    {
        var engine = new TransferPairingEngine();
        var feature = CreateFeature(
            signedAmount: -8m,
            hasTransferKeyword: false,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: false,
            hasProviderTransferHint: false,
            accountHint: null,
            tokens: ["savings", "bucket"]);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature> { [feature.TransactionId] = feature },
            new HashSet<Guid>());

        Assert.Empty(analysis.PendingDecisions);
        Assert.Empty(analysis.ResolvedPairDecisions);
    }

    [Fact]
    public void TransferPairing_NoCandidatesAndNoCounterparty_EvaluatesAsNoMatch()
    {
        var engine = new TransferPairingEngine();
        var feature = CreateFeature(
            signedAmount: -80m,
            bookedAtUtc: new DateTime(2026, 03, 20, 8, 0, 0, DateTimeKind.Utc),
            hasTransferKeyword: true,
            isBooked: true,
            isPending: false,
            hasCounterpartyAccounts: false);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature> { [feature.TransactionId] = feature },
            new HashSet<Guid>());

        var decision = analysis.PendingDecisions[feature.TransactionId];
        Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, decision.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.EvaluatedInsufficientSignals, decision.ReasonCode);
        Assert.False(decision.RetryEligible);
    }

    [Fact]
    public void TransferPairing_WeakTransferLikeWithoutExplicitCounterpartySignal_EvaluatesAsNoMatch()
    {
        var engine = new TransferPairingEngine();
        var feature = CreateFeature(
            signedAmount: -125m,
            hasTransferKeyword: true,
            hasSavingsKeyword: false,
            accountHint: null,
            tokens: ["transfer", "memo"],
            isBooked: true,
            isPending: false,
            hasCounterpartyAccounts: true);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature> { [feature.TransactionId] = feature },
            new HashSet<Guid>());

        var decision = analysis.PendingDecisions[feature.TransactionId];
        Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, decision.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.EvaluatedInsufficientSignals, decision.ReasonCode);
        Assert.False(decision.RetryEligible);
    }

    [Fact]
    public void TransferPairing_AmbiguousCandidates_AreRejected()
    {
        var engine = new TransferPairingEngine();
        var bookedAt = new DateTime(2026, 03, 19, 11, 0, 0, DateTimeKind.Utc);
        var source = CreateFeature(
            signedAmount: -120m,
            bookedAtUtc: bookedAt,
            hasTransferKeyword: true,
            accountHint: "3344",
            tokens: ["transfer", "to", "3344"]);
        var candidateA = CreateFeature(
            signedAmount: 120m,
            bookedAtUtc: bookedAt.AddMinutes(15),
            hasTransferKeyword: true,
            accountHint: "3344",
            tokens: ["transfer", "to", "3344"]);
        var candidateB = CreateFeature(
            signedAmount: 120m,
            bookedAtUtc: bookedAt.AddMinutes(17),
            hasTransferKeyword: true,
            accountHint: "3344",
            tokens: ["transfer", "to", "3344"]);

        var features = new Dictionary<Guid, DeterministicTransactionFeature>
        {
            [source.TransactionId] = source,
            [candidateA.TransactionId] = candidateA,
            [candidateB.TransactionId] = candidateB
        };

        var analysis = engine.AnalyzeUnpairedTransactions(features, new HashSet<Guid>());

        var decision = analysis.PendingDecisions[source.TransactionId];
        Assert.Equal(DeterministicClassificationStatus.RejectedAmbiguousMatch, decision.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.RejectedAmbiguousCandidates, decision.ReasonCode);
        Assert.False(decision.RetryEligible);
    }

    [Fact]
    public void TransferPairing_BookedMutualCandidate_IsResolvedAsDeterministicPair()
    {
        var engine = new TransferPairingEngine();
        var bookedAt = new DateTime(2026, 03, 18, 10, 0, 0, DateTimeKind.Utc);
        var debit = CreateFeature(
            signedAmount: -300m,
            bookedAtUtc: bookedAt,
            hasTransferKeyword: true,
            accountHint: "4455",
            tokens: ["transfer", "4455"],
            isBooked: true,
            isPending: false);
        var credit = CreateFeature(
            signedAmount: 300m,
            bookedAtUtc: bookedAt.AddMinutes(20),
            hasTransferKeyword: true,
            accountHint: "4455",
            tokens: ["transfer", "4455"],
            isBooked: true,
            isPending: false);

        var features = new Dictionary<Guid, DeterministicTransactionFeature>
        {
            [debit.TransactionId] = debit,
            [credit.TransactionId] = credit
        };

        var analysis = engine.AnalyzeUnpairedTransactions(features, new HashSet<Guid>());

        Assert.True(analysis.ResolvedPairDecisions.ContainsKey(debit.TransactionId));
        Assert.True(analysis.ResolvedPairDecisions.ContainsKey(credit.TransactionId));
        var decision = analysis.ResolvedPairDecisions[debit.TransactionId];
        Assert.Equal(DeterministicClassificationReasonCodes.MatchedExactInverseAmount, decision.ReasonCode);
    }

    [Fact]
    public void SavingsClassifier_StrongProviderSignal_ClassifiesWithoutCounterpartPair()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -4.75m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            hasProviderTransferHint: true,
            tokens: ["pocket", "move"]);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, outcome!.Status);
        Assert.Equal("savings_transfer", outcome.RelationshipType);
        Assert.Null(outcome.LinkedTransactionId);
        Assert.True(outcome.Terminal);
    }

    [Fact]
    public void SavingsRoutingPolicy_StrongContextWithoutPositiveEvidence_DoesNotAllowEvaluation()
    {
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -1.25m,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 2,
            hasProviderTransferHint: false,
            hasWeakSavingsSupportKeyword: false);

        var decision = policy.Evaluate(source, hasLegacySavingsMarker: false);

        Assert.False(decision.ShouldEvaluate);
        Assert.True(decision.ContextualSupport);
        Assert.Equal(2, decision.RepetitionStrength);
    }

    [Fact]
    public void SavingsRoutingPolicy_WeakKeywordOnly_DoesNotAllowEvaluation()
    {
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -2.10m,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            hasWeakSavingsSupportKeyword: true,
            nearbyMerchantOutflowCount: 0,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            hasProviderTransferHint: false,
            tokens: ["cash", "fund"]);

        var decision = policy.Evaluate(source, hasLegacySavingsMarker: false);

        Assert.False(decision.ShouldEvaluate);
        Assert.True(decision.WeakSupportOnlySignalsPresent);
    }

    [Fact]
    public void SavingsClassifier_OneSidedStrongSignal_ClassifiesFromContext()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -10m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            tokens: ["round", "up", "pocket"],
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 4,
            hasProviderTransferHint: true);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, outcome!.Status);
        Assert.Equal("savings_transfer", outcome.RelationshipType);
        Assert.Null(outcome.LinkedTransactionId);
    }

    [Fact]
    public void SavingsClassifier_ContextWithoutSavingsProductEvidence_DoesNotClassify()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -1.75m,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            hasProviderTransferHint: false,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 3,
            hasCounterpartyAccounts: true,
            tokens: ["auxiliary", "movement"]);

        var decision = policy.Evaluate(source, hasLegacySavingsMarker: false);
        var outcome = classifier.Classify(source, decision, hasLegacySavingsMarker: false);

        Assert.False(decision.ShouldEvaluate);
        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_ContextualSignalWithoutPositiveEvidence_DoesNotClassify()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -0.75m,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 2,
            hasCounterpartyAccounts: true,
            tokens: ["auxiliary", "sweep"]);

        var decision = policy.Evaluate(source, hasLegacySavingsMarker: false);
        var outcome = classifier.Classify(source, decision, hasLegacySavingsMarker: false);

        Assert.False(decision.ShouldEvaluate);
        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_StrongProviderSignal_AllowsLargerManualSavingsMove()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -165m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            hasProviderTransferHint: true,
            nearbyMerchantOutflowCount: 0,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            tokens: ["vault", "monthly", "transfer"]);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, outcome!.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.SavingsProviderStructuralSignal, outcome.ReasonCode);
    }

    [Fact]
    public void SavingsClassifier_ContextualSavings_NotBlockedByMainSpendPostingAfterCandidate()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -0.62m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            nearbyMerchantOutflowCount: 1,
            repeatedSmallAuxiliaryOutflowPatternCount: 2,
            hasProviderTransferHint: true,
            hasProviderSpecificTransferMarker: true,
            tokens: ["savings", "vault", "round"]);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.NotNull(outcome);
    }

    [Fact]
    public void SavingsClassifier_SparseHistory_WithStrongProviderContext_CanStillClassify()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -12.4m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            nearbyMerchantOutflowCount: 1,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            hasProviderTransferHint: true,
            tokens: ["savings", "vault"]);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, outcome!.Status);
    }

    [Fact]
    public void SavingsClassifier_RepetitionStrength_GraduatesSupport()
    {
        var policy = new SavingsRoutingPolicy();
        var weak = CreateFeature(
            signedAmount: -1m,
            nearbyMerchantOutflowCount: 1,
            repeatedSmallAuxiliaryOutflowPatternCount: 1);
        var medium = CreateFeature(
            signedAmount: -1m,
            nearbyMerchantOutflowCount: 1,
            repeatedSmallAuxiliaryOutflowPatternCount: 2);
        var strong = CreateFeature(
            signedAmount: -1m,
            nearbyMerchantOutflowCount: 1,
            repeatedSmallAuxiliaryOutflowPatternCount: 4);

        Assert.Equal(1, policy.Evaluate(weak, hasLegacySavingsMarker: false).RepetitionStrength);
        Assert.Equal(2, policy.Evaluate(medium, hasLegacySavingsMarker: false).RepetitionStrength);
        Assert.Equal(3, policy.Evaluate(strong, hasLegacySavingsMarker: false).RepetitionStrength);
    }

    [Fact]
    public void SavingsClassifier_WeakUnpairedSignal_DoesNotClassify()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -12m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: false,
            tokens: ["savings", "move"]);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_NameOnlySignalWithoutContext_DoesNotClassify()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -9m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            tokens: ["vault", "to"],
            nearbyMerchantOutflowCount: 0,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            hasProviderTransferHint: false);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_ExternalCounterpartyRisk_DoesNotClassifyAsSavings()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -3m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            looksLikeExternalCounterparty: true,
            nearbyMerchantOutflowCount: 3,
            repeatedSmallAuxiliaryOutflowPatternCount: 4,
            hasProviderTransferHint: true);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_PairStyleShapeWithoutSavingsContext_DoesNotClassify()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -150m,
            hasTransferKeyword: true,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            accountHint: "4421",
            hasProviderTransferHint: true,
            nearbyMerchantOutflowCount: 0,
            repeatedSmallAuxiliaryOutflowPatternCount: 0);

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_DefaultPath_DoesNotUseCounterpartPairing()
    {
        var classifier = new SavingsTransferClassifier();
        var source = CreateFeature(
            signedAmount: -95m,
            hasTransferKeyword: true,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            accountHint: "5521",
            hasProviderTransferHint: true,
            nearbyMerchantOutflowCount: 0,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            tokens: ["transfer", "5521"]);

        var outcome = classifier.Classify(source, hasLegacySavingsMarker: false);

        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_DefaultPath_DoesNotUseLinkedTransferAsGenericSavingsShortcut()
    {
        var counterpartMethod = typeof(SavingsTransferClassifier).GetMethod(
            "FindSavingsCounterpart",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        Assert.Null(counterpartMethod);

        var classifySignatures = typeof(SavingsTransferClassifier)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Classify")
            .Select(method => string.Join(",", method.GetParameters().Select(parameter => parameter.Name)))
            .ToArray();
        Assert.DoesNotContain(classifySignatures, signature => signature.Contains("linkedTransactionId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TransferPairing_DuplicateClusterStablePairs_AreResolved()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 25, 8, 0, 0, DateTimeKind.Utc);
        var debitA = CreateFeature(
            signedAmount: -1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            accountHint: "9901",
            tokens: ["transfer", "ref", "9901"]);
        var debitB = CreateFeature(
            signedAmount: -1m,
            bookedAtUtc: baseUtc.AddMinutes(8),
            hasTransferKeyword: true,
            accountHint: "9902",
            tokens: ["transfer", "ref", "9902"]);
        var creditA = CreateFeature(
            signedAmount: 1m,
            bookedAtUtc: baseUtc.AddMinutes(2),
            hasTransferKeyword: true,
            accountHint: "9901",
            tokens: ["transfer", "ref", "9901"]);
        var creditB = CreateFeature(
            signedAmount: 1m,
            bookedAtUtc: baseUtc.AddMinutes(10),
            hasTransferKeyword: true,
            accountHint: "9902",
            tokens: ["transfer", "ref", "9902"]);

        var features = new Dictionary<Guid, DeterministicTransactionFeature>
        {
            [debitA.TransactionId] = debitA,
            [debitB.TransactionId] = debitB,
            [creditA.TransactionId] = creditA,
            [creditB.TransactionId] = creditB
        };

        var analysis = engine.AnalyzeUnpairedTransactions(features, new HashSet<Guid>());

        Assert.Equal(4, analysis.ResolvedPairDecisions.Count);
        Assert.All(new[] { debitA.TransactionId, debitB.TransactionId, creditA.TransactionId, creditB.TransactionId }, id =>
            Assert.True(analysis.ResolvedPairDecisions.ContainsKey(id)));
    }

    [Fact]
    public void TransferPairing_AmbiguousDuplicateCluster_ResolvesByHighConfidenceReferenceOverlap()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 25, 8, 0, 0, DateTimeKind.Utc);
        var debitA = CreateFeature(
            signedAmount: -50m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "aib",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            narrativeSignals: CreateNarrativeSignals(machineReferenceTokens: ["ieabc12345"]),
            hasHighConfidenceReferenceSignals: true,
            stableSequence: 1L);
        var debitB = CreateFeature(
            signedAmount: -50m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "aib",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            narrativeSignals: CreateNarrativeSignals(machineReferenceTokens: ["iedef67890"]),
            hasHighConfidenceReferenceSignals: true,
            stableSequence: 2L);
        var creditA = CreateFeature(
            signedAmount: 50m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "revolut",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.PreciseDateTime,
            narrativeSignals: CreateNarrativeSignals(machineReferenceTokens: ["ieabc12345"]),
            hasHighConfidenceReferenceSignals: true,
            stableSequence: 3L);
        var creditB = CreateFeature(
            signedAmount: 50m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "revolut",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.PreciseDateTime,
            narrativeSignals: CreateNarrativeSignals(machineReferenceTokens: ["iedef67890"]),
            hasHighConfidenceReferenceSignals: true,
            stableSequence: 4L);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature>
            {
                [debitA.TransactionId] = debitA,
                [debitB.TransactionId] = debitB,
                [creditA.TransactionId] = creditA,
                [creditB.TransactionId] = creditB
            },
            new HashSet<Guid>());

        Assert.Equal(4, analysis.ResolvedPairDecisions.Count);
        Assert.Equal(creditA.TransactionId, analysis.ResolvedPairDecisions[debitA.TransactionId].CreditTransactionId);
        Assert.Equal(creditB.TransactionId, analysis.ResolvedPairDecisions[debitB.TransactionId].CreditTransactionId);
    }

    [Fact]
    public void TransferPairing_DuplicateCluster_UsesStableOrderingOnlyAsLastTieBreaker()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 25, 8, 0, 0, DateTimeKind.Utc);
        var debitA = CreateFeature(
            signedAmount: -1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            accountHint: "1234",
            stableSequence: 10L);
        var debitB = CreateFeature(
            signedAmount: -1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            accountHint: "1234",
            stableSequence: 20L);
        var creditA = CreateFeature(
            signedAmount: 1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            accountHint: "1234",
            stableSequence: 11L);
        var creditB = CreateFeature(
            signedAmount: 1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            accountHint: "1234",
            stableSequence: 21L);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature>
            {
                [debitA.TransactionId] = debitA,
                [debitB.TransactionId] = debitB,
                [creditA.TransactionId] = creditA,
                [creditB.TransactionId] = creditB
            },
            new HashSet<Guid>());

        var evidenceJson = analysis.ResolvedPairDecisions[debitA.TransactionId].EvidenceJson;
        using var evidence = JsonDocument.Parse(evidenceJson);
        Assert.True(evidence.RootElement.TryGetProperty("stableOrderingUsed", out var stableOrderingNode));
        Assert.True(stableOrderingNode.GetBoolean());
    }

    [Fact]
    public void TransferPairing_PersonalNameOnlyOverlap_DoesNotProveSameAccountTransfer()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 20, 8, 0, 0, DateTimeKind.Utc);
        var source = CreateFeature(
            signedAmount: -75m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            hasCounterpartyAccounts: true,
            narrativeSignals: CreateNarrativeSignals(beneficiaryNameTokens: ["john", "smith"]),
            hasMediumConfidenceReferenceSignals: true,
            stableSequence: 1L);
        var candidate = CreateFeature(
            signedAmount: 75m,
            bookedAtUtc: baseUtc.AddMinutes(5),
            hasTransferKeyword: true,
            hasCounterpartyAccounts: true,
            narrativeSignals: CreateNarrativeSignals(beneficiaryNameTokens: ["john", "smith"]),
            hasMediumConfidenceReferenceSignals: true,
            stableSequence: 2L);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature>
            {
                [source.TransactionId] = source,
                [candidate.TransactionId] = candidate
            },
            new HashSet<Guid>());

        Assert.Empty(analysis.ResolvedPairDecisions);
        Assert.Equal(
            DeterministicClassificationStatus.EvaluatedNoMatchingRule,
            analysis.PendingDecisions[source.TransactionId].Status);
    }

    [Fact]
    public void TransferPairing_MachineReferenceOverlap_OutweighsNearerTimeCandidate()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 21, 8, 0, 0, DateTimeKind.Utc);
        var debit = CreateFeature(
            signedAmount: -120m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            narrativeSignals: CreateNarrativeSignals(machineReferenceTokens: ["abc123xyz9"]),
            hasHighConfidenceReferenceSignals: true,
            stableSequence: 1L);
        var creditNearNoRef = CreateFeature(
            signedAmount: 120m,
            bookedAtUtc: baseUtc.AddMinutes(30),
            hasTransferKeyword: true,
            stableSequence: 2L);
        var creditFarWithRef = CreateFeature(
            signedAmount: 120m,
            bookedAtUtc: baseUtc.AddHours(10),
            hasTransferKeyword: true,
            narrativeSignals: CreateNarrativeSignals(machineReferenceTokens: ["abc123xyz9"]),
            hasHighConfidenceReferenceSignals: true,
            stableSequence: 3L);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature>
            {
                [debit.TransactionId] = debit,
                [creditNearNoRef.TransactionId] = creditNearNoRef,
                [creditFarWithRef.TransactionId] = creditFarWithRef
            },
            new HashSet<Guid>());

        Assert.True(analysis.ResolvedPairDecisions.ContainsKey(debit.TransactionId));
        Assert.Equal(
            creditFarWithRef.TransactionId,
            analysis.ResolvedPairDecisions[debit.TransactionId].CreditTransactionId);
    }

    [Fact]
    public void TransferPairing_CoarseTimestampProvider_DownweightsTimeDistance()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 22, 0, 0, 0, DateTimeKind.Utc);
        var debit = CreateFeature(
            signedAmount: -30m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            narrativeSignals: CreateNarrativeSignals(providerSpecificReferenceTokens: ["ie-ref-7788"]),
            hasHighConfidenceReferenceSignals: true,
            hasProviderSpecificTransferMarker: true,
            stableSequence: 1L);
        var creditCloseNoRef = CreateFeature(
            signedAmount: 30m,
            bookedAtUtc: baseUtc.AddHours(1),
            hasTransferKeyword: true,
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            stableSequence: 2L);
        var creditFarWithRef = CreateFeature(
            signedAmount: 30m,
            bookedAtUtc: baseUtc.AddHours(20),
            hasTransferKeyword: true,
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            narrativeSignals: CreateNarrativeSignals(providerSpecificReferenceTokens: ["ie-ref-7788"]),
            hasHighConfidenceReferenceSignals: true,
            hasProviderSpecificTransferMarker: true,
            stableSequence: 3L);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature>
            {
                [debit.TransactionId] = debit,
                [creditCloseNoRef.TransactionId] = creditCloseNoRef,
                [creditFarWithRef.TransactionId] = creditFarWithRef
            },
            new HashSet<Guid>());

        Assert.Equal(
            creditFarWithRef.TransactionId,
            analysis.ResolvedPairDecisions[debit.TransactionId].CreditTransactionId);
    }

    [Fact]
    public void TransferPairing_AibLikeMachineReferenceDuplicates_ResolveWithoutHardcoding()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 24, 0, 0, 0, DateTimeKind.Utc);
        var debitOne = CreateFeature(
            signedAmount: -1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "aib",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            narrativeSignals: CreateNarrativeSignals(providerSpecificReferenceTokens: ["ieaa001199"], machineReferenceTokens: ["ieaa001199"]),
            hasHighConfidenceReferenceSignals: true,
            hasProviderSpecificTransferMarker: true,
            stableSequence: 1L);
        var debitTwo = CreateFeature(
            signedAmount: -1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "aib",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.DateOnly,
            narrativeSignals: CreateNarrativeSignals(providerSpecificReferenceTokens: ["iebb001199"], machineReferenceTokens: ["iebb001199"]),
            hasHighConfidenceReferenceSignals: true,
            hasProviderSpecificTransferMarker: true,
            stableSequence: 2L);
        var creditOne = CreateFeature(
            signedAmount: 1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "revolut",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.PreciseDateTime,
            narrativeSignals: CreateNarrativeSignals(providerSpecificReferenceTokens: ["ieaa001199"], machineReferenceTokens: ["ieaa001199"]),
            hasHighConfidenceReferenceSignals: true,
            hasProviderSpecificTransferMarker: true,
            stableSequence: 3L);
        var creditTwo = CreateFeature(
            signedAmount: 1m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "revolut",
            providerTimestampPrecision: DeterministicProviderTimestampPrecision.PreciseDateTime,
            narrativeSignals: CreateNarrativeSignals(providerSpecificReferenceTokens: ["iebb001199"], machineReferenceTokens: ["iebb001199"]),
            hasHighConfidenceReferenceSignals: true,
            hasProviderSpecificTransferMarker: true,
            stableSequence: 4L);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature>
            {
                [debitOne.TransactionId] = debitOne,
                [debitTwo.TransactionId] = debitTwo,
                [creditOne.TransactionId] = creditOne,
                [creditTwo.TransactionId] = creditTwo
            },
            new HashSet<Guid>());

        Assert.Equal(4, analysis.ResolvedPairDecisions.Count);
        Assert.Equal(creditOne.TransactionId, analysis.ResolvedPairDecisions[debitOne.TransactionId].CreditTransactionId);
        Assert.Equal(creditTwo.TransactionId, analysis.ResolvedPairDecisions[debitTwo.TransactionId].CreditTransactionId);
    }

    [Fact]
    public void TransferPairing_ProviderWithoutMachineReferences_FallsBackSafelyWithoutOvermatch()
    {
        var engine = new TransferPairingEngine();
        var baseUtc = new DateTime(2026, 03, 24, 9, 0, 0, DateTimeKind.Utc);
        var debitOne = CreateFeature(
            signedAmount: -20m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "generic",
            stableSequence: 1L);
        var debitTwo = CreateFeature(
            signedAmount: -20m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "generic",
            stableSequence: 2L);
        var creditOne = CreateFeature(
            signedAmount: 20m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "generic",
            stableSequence: 3L);
        var creditTwo = CreateFeature(
            signedAmount: 20m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "generic",
            stableSequence: 4L);
        var creditThree = CreateFeature(
            signedAmount: 20m,
            bookedAtUtc: baseUtc,
            hasTransferKeyword: true,
            providerKey: "generic",
            stableSequence: 5L);

        var analysis = engine.AnalyzeUnpairedTransactions(
            new Dictionary<Guid, DeterministicTransactionFeature>
            {
                [debitOne.TransactionId] = debitOne,
                [debitTwo.TransactionId] = debitTwo,
                [creditOne.TransactionId] = creditOne,
                [creditTwo.TransactionId] = creditTwo,
                [creditThree.TransactionId] = creditThree
            },
            new HashSet<Guid>());

        Assert.Empty(analysis.ResolvedPairDecisions);
        Assert.True(analysis.PendingDecisions.Values.Any(x => x.Status == DeterministicClassificationStatus.RejectedAmbiguousMatch));
    }

    [Fact]
    public void SavingsClassifier_MerchantLikeSmallCharge_DoesNotClassifyFromContextOnly()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -1.49m,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            merchantLikelihoodScore: 6,
            merchantLikelihoodVeto: true,
            narrativeSignals: CreateNarrativeSignals(merchantLikeTokens: ["subscription", "processor_separator"]));

        var decision = policy.Evaluate(source, hasLegacySavingsMarker: false);
        var outcome = classifier.Classify(source, decision, hasLegacySavingsMarker: false);

        Assert.False(decision.ShouldEvaluate);
        Assert.Equal("merchant_likelihood_veto", decision.BlockedReason);
        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_SubscriptionStyleDescriptor_RequiresStrongPositiveEvidence()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -4.99m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: false,
            hasProviderTransferHint: false,
            nearbyMerchantOutflowCount: 2,
            merchantLikelihoodScore: 5,
            merchantLikelihoodVeto: true,
            narrativeSignals: CreateNarrativeSignals(
                beneficiaryNameTokens: ["streaming"],
                merchantLikeTokens: ["subscription", "software"]));

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.Null(outcome);
    }

    [Fact]
    public void SavingsClassifier_StrongProviderProductSignal_ClassifiesEvenForLargeAmount()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -2000m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            hasProviderTransferHint: true,
            hasProviderSpecificTransferMarker: true,
            merchantLikelihoodScore: 5,
            merchantLikelihoodVeto: true,
            narrativeSignals: CreateNarrativeSignals(
                providerSpecificReferenceTokens: ["savings_label"],
                merchantLikeTokens: ["services"]));

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.NotNull(outcome);
        Assert.Equal("savings_transfer", outcome!.RelationshipType);
    }

    [Fact]
    public void SavingsClassifier_ContextualSavings_WithRealProviderSupport_Classifies()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -2.15m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            hasProviderTransferHint: true,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 2,
            narrativeSignals: CreateNarrativeSignals(providerSpecificReferenceTokens: ["savings_label"]));

        var outcome = classifier.Classify(source, policy.Evaluate(source, hasLegacySavingsMarker: false), hasLegacySavingsMarker: false);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, outcome!.Status);
    }

    [Fact]
    public void SavingsClassifier_MerchantVeto_DoesNotBlockTrueSavingsWithStrongExplicitEvidence()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -12m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            hasProviderTransferHint: true,
            hasProviderSpecificTransferMarker: true,
            merchantLikelihoodScore: 7,
            merchantLikelihoodVeto: true,
            narrativeSignals: CreateNarrativeSignals(
                providerSpecificReferenceTokens: ["savings_label"],
                merchantLikeTokens: ["subscription"]));

        var routing = policy.Evaluate(source, hasLegacySavingsMarker: false);
        var outcome = classifier.Classify(source, routing, hasLegacySavingsMarker: false);

        Assert.True(routing.MerchantLikelihoodVeto);
        Assert.True(routing.MerchantVetoOverridden);
        Assert.NotNull(outcome);
    }

    [Fact]
    public void SavingsClassifier_WeakGenericWordingPlusNearbySpend_DoesNotClassify()
    {
        var classifier = new SavingsTransferClassifier();
        var policy = new SavingsRoutingPolicy();
        var source = CreateFeature(
            signedAmount: -1.10m,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            hasWeakSavingsSupportKeyword: true,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            narrativeSignals: CreateNarrativeSignals(freeTextReferenceTokens: ["fund"]));

        var decision = policy.Evaluate(source, hasLegacySavingsMarker: false);
        var outcome = classifier.Classify(source, decision, hasLegacySavingsMarker: false);

        Assert.False(decision.ShouldEvaluate);
        Assert.Null(outcome);
    }

    [Theory]
    [InlineData(DeterministicClassificationStatus.ClassifiedMatchedRule, true)]
    [InlineData(DeterministicClassificationStatus.EvaluatedNoMatchingRule, true)]
    [InlineData(DeterministicClassificationStatus.RejectedAmbiguousMatch, true)]
    [InlineData(DeterministicClassificationStatus.DeferredWaitingForCounterparty, false)]
    [InlineData(DeterministicClassificationStatus.NotEvaluated, false)]
    public void RetryPlanner_TerminalStateContract_IsStable(
        DeterministicClassificationStatus status,
        bool expectedTerminal)
    {
        Assert.Equal(expectedTerminal, DeterministicClassificationRetryPlanner.IsTerminal(status));
    }

    [Fact]
    public void RetryPlanner_DeferredCounterparty_RequiresCounterpartyAccounts()
    {
        var planner = new DeterministicClassificationRetryPlanner();

        var eligibleWithCounterparty = planner.IsRetryEligible(
            DeterministicClassificationStatus.DeferredWaitingForCounterparty,
            hasCounterpartyAccounts: true,
            isPending: false);
        var ineligibleWithoutCounterparty = planner.IsRetryEligible(
            DeterministicClassificationStatus.DeferredWaitingForCounterparty,
            hasCounterpartyAccounts: false,
            isPending: false);

        Assert.True(eligibleWithCounterparty);
        Assert.False(ineligibleWithoutCounterparty);
    }

    [Fact]
    public void BuildOutcome_TransferPendingResolved_BeforeSavingsRuns()
    {
        using var dbContext = CreateDbContext();
        var service = CreatePersistenceService(dbContext);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = Guid.NewGuid(),
            Amount = -3.21m,
            Currency = "EUR",
            Description = "Round up move",
            BookedAtUtc = new DateTime(2026, 03, 20, 10, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2026, 03, 20, 10, 0, 0, DateTimeKind.Utc)
        };
        var feature = CreateFeature(
            signedAmount: -3.21m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            hasProviderTransferHint: true,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 3,
            accountHint: "4455",
            tokens: ["round", "up", "pocket", "4455"]);
        var pending = new TransferPendingDecision(
            transaction.Id,
            DeterministicClassificationStatus.DeferredWaitingForCounterparty,
            DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
            RetryEligible: true,
            CandidateFamily: "bank_account_transfer",
            CandidateCount: 0,
            TopCandidateTransactionId: null,
            TopCandidateScore: null,
            IsDuplicateClusterMember: false,
            DuplicateClusterSize: 0,
            EvidenceJson: "{}");

        var outcome = InvokeBuildOutcome(
            service,
            transaction,
            feature,
            linkedPairs: new Dictionary<Guid, Guid>(),
            resolvedPairDecisions: new Dictionary<Guid, TransferPairDecision>(),
            pendingDecisions: new Dictionary<Guid, TransferPendingDecision>
            {
                [transaction.Id] = pending
            });

        Assert.Equal(DeterministicClassificationStatus.DeferredWaitingForCounterparty, outcome.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.DeferredMissingCounterparty, outcome.ReasonCode);
        Assert.Null(outcome.RelationshipType);
    }

    [Fact]
    public void BuildOutcome_TransferPending_IsEvaluatedBeforeSavingsReturn()
    {
        using var dbContext = CreateDbContext();
        var service = CreatePersistenceService(dbContext);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = Guid.NewGuid(),
            Amount = -2.20m,
            Currency = "EUR",
            Description = "Round up with transfer cues 4455",
            BookedAtUtc = new DateTime(2026, 03, 22, 10, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2026, 03, 22, 10, 0, 0, DateTimeKind.Utc)
        };
        var feature = CreateFeature(
            signedAmount: -2.20m,
            hasTransferKeyword: true,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            hasProviderTransferHint: true,
            nearbyMerchantOutflowCount: 2,
            repeatedSmallAuxiliaryOutflowPatternCount: 3,
            accountHint: "4455",
            hasCounterpartyAccounts: true,
            tokens: ["transfer", "round", "up", "4455"]);
        var pending = new TransferPendingDecision(
            transaction.Id,
            DeterministicClassificationStatus.DeferredWaitingForCounterparty,
            DeterministicClassificationReasonCodes.DeferredMissingCounterparty,
            RetryEligible: true,
            CandidateFamily: "bank_account_transfer",
            CandidateCount: 0,
            TopCandidateTransactionId: null,
            TopCandidateScore: null,
            IsDuplicateClusterMember: false,
            DuplicateClusterSize: 0,
            EvidenceJson: "{}");

        var outcome = InvokeBuildOutcome(
            service,
            transaction,
            feature,
            linkedPairs: new Dictionary<Guid, Guid>(),
            resolvedPairDecisions: new Dictionary<Guid, TransferPairDecision>(),
            pendingDecisions: new Dictionary<Guid, TransferPendingDecision> { [transaction.Id] = pending });

        Assert.Equal(DeterministicClassificationStatus.DeferredWaitingForCounterparty, outcome.Status);
        Assert.Equal("bank_transfer.deferred_or_rejected_v3", outcome.RuleKey);
        Assert.Null(outcome.RelationshipType);
    }

    [Fact]
    public void BuildOutcome_LegacySavings_DoesNotForceSavingsRouting()
    {
        using var dbContext = CreateDbContext();
        var service = CreatePersistenceService(dbContext);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = Guid.NewGuid(),
            Amount = -7.00m,
            Currency = "EUR",
            Description = "Manual move",
            BookedAtUtc = new DateTime(2026, 03, 19, 9, 30, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2026, 03, 19, 9, 30, 0, DateTimeKind.Utc),
            TransferKind = TransactionTransferKind.SavingsRoundup
        };
        var feature = CreateFeature(
            signedAmount: -7.00m,
            hasTransferKeyword: false,
            hasSavingsKeyword: false,
            hasStrongSavingsKeyword: false,
            hasProviderTransferHint: false,
            nearbyMerchantOutflowCount: 0,
            repeatedSmallAuxiliaryOutflowPatternCount: 0,
            tokens: ["manual", "move"]);

        var outcome = InvokeBuildOutcome(
            service,
            transaction,
            feature,
            linkedPairs: new Dictionary<Guid, Guid>(),
            resolvedPairDecisions: new Dictionary<Guid, TransferPairDecision>(),
            pendingDecisions: new Dictionary<Guid, TransferPendingDecision>());

        Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, outcome.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.EvaluatedUnsupportedFamily, outcome.ReasonCode);
        Assert.Null(outcome.RelationshipType);
    }

    [Fact]
    public void ReasonCodes_StaleSavingsAliases_AreObsoleteOrUnused()
    {
        var staleNames = new[]
        {
            nameof(DeterministicClassificationReasonCodes.MatchedSavingsKeywordSignal),
            nameof(DeterministicClassificationReasonCodes.MatchedSavingsOneSidedSignal),
            nameof(DeterministicClassificationReasonCodes.DeferredStrongSavingsMissingCounterparty)
        };

        foreach (var staleName in staleNames)
        {
            var field = typeof(DeterministicClassificationReasonCodes).GetField(staleName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            var obsolete = field!.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(obsolete);
        }
    }

    private static DeterministicClassificationOutcome InvokeBuildOutcome(
        DeterministicClassificationPersistenceService service,
        Transaction transaction,
        DeterministicTransactionFeature feature,
        IReadOnlyDictionary<Guid, Guid> linkedPairs,
        IReadOnlyDictionary<Guid, TransferPairDecision> resolvedPairDecisions,
        IReadOnlyDictionary<Guid, TransferPendingDecision> pendingDecisions)
    {
        var method = typeof(DeterministicClassificationPersistenceService).GetMethod(
            "BuildOutcome",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(service, [transaction, feature, linkedPairs, resolvedPairDecisions, pendingDecisions]);
        Assert.NotNull(result);
        return Assert.IsType<DeterministicClassificationOutcome>(result);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"deterministic-build-outcome-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static DeterministicClassificationPersistenceService CreatePersistenceService(AppDbContext dbContext)
    {
        var normalizationService = new TransactionNormalizationService();
        var providerCapabilityRegistry = new ProviderCapabilityRegistry();
        var narrativeSignalExtractor = new NarrativeSignalExtractor();
        var featureExtractor = new TransactionFeatureExtractor(
            normalizationService,
            providerCapabilityRegistry,
            narrativeSignalExtractor);
        var transferPairingEngine = new TransferPairingEngine();
        var savingsRoutingPolicy = new SavingsRoutingPolicy();
        var savingsTransferClassifier = new SavingsTransferClassifier();
        var retryPlanner = new DeterministicClassificationRetryPlanner();
        var metrics = new DeterministicCategorizationMetrics();

        return new DeterministicClassificationPersistenceService(
            dbContext,
            normalizationService,
            featureExtractor,
            transferPairingEngine,
            savingsRoutingPolicy,
            savingsTransferClassifier,
            retryPlanner,
            metrics,
            NullLogger<DeterministicClassificationPersistenceService>.Instance);
    }

    private static NarrativeSignalSet CreateNarrativeSignals(
        IEnumerable<string>? machineReferenceTokens = null,
        IEnumerable<string>? accountLikeTokens = null,
        IEnumerable<string>? paymentSystemMarkers = null,
        IEnumerable<string>? providerSpecificReferenceTokens = null,
        IEnumerable<string>? beneficiaryNameTokens = null,
        IEnumerable<string>? freeTextReferenceTokens = null,
        IEnumerable<string>? merchantLikeTokens = null)
    {
        var machine = (machineReferenceTokens ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var account = (accountLikeTokens ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var payment = (paymentSystemMarkers ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providerSpecific = (providerSpecificReferenceTokens ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var beneficiary = (beneficiaryNameTokens ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var freeText = (freeTextReferenceTokens ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merchantLike = (merchantLikeTokens ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var confidence = new Dictionary<string, NarrativeSignalConfidenceTier>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in machine.Concat(account).Concat(payment).Concat(providerSpecific))
        {
            confidence[token] = NarrativeSignalConfidenceTier.HighConfidence;
        }

        foreach (var token in beneficiary)
        {
            confidence[token] = NarrativeSignalConfidenceTier.MediumConfidence;
        }

        foreach (var token in freeText)
        {
            if (!confidence.ContainsKey(token))
            {
                confidence[token] = NarrativeSignalConfidenceTier.LowConfidence;
            }
        }

        foreach (var token in merchantLike)
        {
            if (!confidence.ContainsKey(token))
            {
                confidence[token] = NarrativeSignalConfidenceTier.MediumConfidence;
            }
        }

        return new NarrativeSignalSet(
            machine,
            account,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            payment,
            beneficiary,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            freeText,
            providerSpecific,
            merchantLike,
            confidence);
    }

    private static DeterministicTransactionFeature CreateFeature(
        decimal signedAmount,
        DateTime? bookedAtUtc = null,
        bool hasTransferKeyword = false,
        bool hasSavingsKeyword = false,
        bool hasStrongSavingsKeyword = false,
        bool hasWeakSavingsSupportKeyword = false,
        string? accountHint = null,
        IEnumerable<string>? tokens = null,
        bool isBooked = true,
        bool isPending = false,
        bool hasCounterpartyAccounts = true,
        bool hasProviderTransferHint = false,
        int sameAmountSameDayOutflowCount = 0,
        int sameAmountSameDayInflowCount = 0,
        int nearbyMerchantOutflowCount = 0,
        int repeatedSmallAuxiliaryOutflowPatternCount = 0,
        bool looksLikeExternalCounterparty = false,
        string providerKey = "generic",
        DeterministicProviderTimestampPrecision providerTimestampPrecision = DeterministicProviderTimestampPrecision.Unknown,
        NarrativeSignalSet? narrativeSignals = null,
        bool hasHighConfidenceReferenceSignals = false,
        bool hasMediumConfidenceReferenceSignals = false,
        bool hasProviderSpecificTransferMarker = false,
        int merchantLikelihoodScore = 0,
        bool merchantLikelihoodVeto = false,
        long stableSequence = 0,
        bool providerSupportsMachineReferenceTokens = false,
        bool providerSupportsPaymentSystemMarkers = false,
        bool providerSupportsReliableCounterpartyReferenceFragments = false,
        bool providerSupportsProviderSpecificTransferMarkers = false)
    {
        var resolvedSignals = narrativeSignals ?? NarrativeSignalSet.Empty;
        return new DeterministicTransactionFeature(
            TransactionId: Guid.NewGuid(),
            FinancialAccountId: Guid.NewGuid(),
            SignedAmount: signedAmount,
            AbsoluteAmount: Math.Abs(signedAmount),
            IsOutflow: signedAmount < 0m,
            IsInflow: signedAmount > 0m,
            Currency: "GBP",
            BookedAtUtc: bookedAtUtc ?? new DateTime(2026, 03, 20, 8, 0, 0, DateTimeKind.Utc),
            NormalizedDescription: "transfer sample",
            Tokens: (tokens ?? ["transfer"]).ToHashSet(StringComparer.OrdinalIgnoreCase),
            HasTransferKeyword: hasTransferKeyword,
            HasSavingsKeyword: hasSavingsKeyword,
            HasStrongSavingsKeyword: hasStrongSavingsKeyword,
            HasWeakSavingsSupportKeyword: hasWeakSavingsSupportKeyword,
            AccountHint: accountHint,
            IsBooked: isBooked,
            IsPending: isPending,
            HasProviderTransferHint: hasProviderTransferHint,
            ProviderKey: providerKey,
            ProviderTimestampPrecision: providerTimestampPrecision,
            ProviderSupportsMachineReferenceTokens: providerSupportsMachineReferenceTokens,
            ProviderSupportsPaymentSystemMarkers: providerSupportsPaymentSystemMarkers,
            ProviderSupportsReliableCounterpartyReferenceFragments: providerSupportsReliableCounterpartyReferenceFragments,
            ProviderSupportsProviderSpecificTransferMarkers: providerSupportsProviderSpecificTransferMarkers,
            NarrativeSignals: resolvedSignals,
            HasHighConfidenceReferenceSignals: hasHighConfidenceReferenceSignals
                                               || resolvedSignals.HighConfidenceTokens.Count > 0,
            HasMediumConfidenceReferenceSignals: hasMediumConfidenceReferenceSignals
                                                 || resolvedSignals.SignalConfidenceMap.Values.Any(value =>
                                                     value == NarrativeSignalConfidenceTier.MediumConfidence),
            HasProviderSpecificTransferMarker: hasProviderSpecificTransferMarker
                                               || resolvedSignals.ProviderSpecificReferenceTokens.Count > 0
                                               || resolvedSignals.PaymentSystemMarkers.Count > 0,
            MerchantLikelihoodScore: merchantLikelihoodScore,
            MerchantLikelihoodVeto: merchantLikelihoodVeto,
            StableSequence: stableSequence,
            NearbySameAmountCount: 0,
            SameAmountSameDayOutflowCount: sameAmountSameDayOutflowCount,
            SameAmountSameDayInflowCount: sameAmountSameDayInflowCount,
            NearbyMerchantOutflowCount: nearbyMerchantOutflowCount,
            RepeatedSmallAuxiliaryOutflowPatternCount: repeatedSmallAuxiliaryOutflowPatternCount,
            LooksLikeExternalCounterparty: looksLikeExternalCounterparty,
            Direction: signedAmount < 0m ? "outflow" : signedAmount > 0m ? "inflow" : "neutral",
            HasCounterpartyAccounts: hasCounterpartyAccounts,
            ReferenceEntropy: 0.6d);
    }
}
