using NSFinance.Api.Modules.Banking.Services.Deterministic;
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

    [Fact]
    public void ExtractAccountHint_ReturnsTrailingDigitsOnly()
    {
        var normalization = new TransactionNormalizationService();

        var hint = normalization.ExtractAccountHint("transfer to account ending 7788 from current");

        Assert.Equal("7788", hint);
    }

    [Fact]
    public void FeatureExtractor_ComputesDirectionStatusCurrencyAndNearbyCounts()
    {
        var extractor = new TransactionFeatureExtractor(new TransactionNormalizationService());
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
                "Bank transfer to savings 7788",
                "TRANSFER",
                "booked",
                HasProviderTransferHint: true,
                HasCounterpartyAccounts: true),
            new(
                secondId,
                Guid.NewGuid(),
                100m,
                "GBP",
                bookedAt.AddMinutes(30),
                "Transfer from current 7788",
                "TRANSFER",
                "pending",
                HasProviderTransferHint: true,
                HasCounterpartyAccounts: true)
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

        Assert.True(inflow.IsInflow);
        Assert.False(inflow.IsOutflow);
        Assert.False(inflow.IsBooked);
        Assert.True(inflow.IsPending);
        Assert.Equal(1, inflow.NearbySameAmountCount);
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
    public void TransferPairing_BookedMutualCandidate_DefersWaitingForCounterparty()
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

        var debitDecision = analysis.PendingDecisions[debit.TransactionId];
        Assert.Equal(DeterministicClassificationStatus.DeferredWaitingForCounterparty, debitDecision.Status);
        Assert.Equal(DeterministicClassificationReasonCodes.DeferredMissingCounterparty, debitDecision.ReasonCode);
        Assert.True(debitDecision.RetryEligible);
    }

    [Fact]
    public void SavingsClassifier_PairedCounterpart_MarksMatchedSavingsTransfer()
    {
        var classifier = new SavingsTransferClassifier();
        var source = CreateFeature(
            signedAmount: -45m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            tokens: ["vault", "transfer"]);
        var counterpart = CreateFeature(
            signedAmount: 45m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: false,
            tokens: ["vault", "transfer"]);

        var features = new Dictionary<Guid, DeterministicTransactionFeature>
        {
            [source.TransactionId] = source,
            [counterpart.TransactionId] = counterpart
        };

        var outcome = classifier.Classify(source, features, linkedTransactionId: null);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, outcome!.Status);
        Assert.Equal("savings_transfer", outcome.RelationshipType);
        Assert.Equal(counterpart.TransactionId, outcome.LinkedTransactionId);
        Assert.True(outcome.Terminal);
    }

    [Fact]
    public void SavingsClassifier_OneSidedStrongSignal_MarksMatchedWithoutCounterpart()
    {
        var classifier = new SavingsTransferClassifier();
        var source = CreateFeature(
            signedAmount: -10m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: true,
            tokens: ["round", "up", "pocket"]);

        var outcome = classifier.Classify(
            source,
            new Dictionary<Guid, DeterministicTransactionFeature> { [source.TransactionId] = source },
            linkedTransactionId: null);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, outcome!.Status);
        Assert.Equal("savings_transfer", outcome.RelationshipType);
        Assert.Null(outcome.LinkedTransactionId);
    }

    [Fact]
    public void SavingsClassifier_WeakUnpairedSignal_DefersForCounterparty()
    {
        var classifier = new SavingsTransferClassifier();
        var source = CreateFeature(
            signedAmount: -12m,
            hasSavingsKeyword: true,
            hasStrongSavingsKeyword: false,
            tokens: ["savings", "move"]);

        var outcome = classifier.Classify(
            source,
            new Dictionary<Guid, DeterministicTransactionFeature> { [source.TransactionId] = source },
            linkedTransactionId: null);

        Assert.NotNull(outcome);
        Assert.Equal(DeterministicClassificationStatus.DeferredWaitingForCounterparty, outcome!.Status);
        Assert.False(outcome.Terminal);
        Assert.True(outcome.RetryEligible);
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

    private static DeterministicTransactionFeature CreateFeature(
        decimal signedAmount,
        DateTime? bookedAtUtc = null,
        bool hasTransferKeyword = false,
        bool hasSavingsKeyword = false,
        bool hasStrongSavingsKeyword = false,
        string? accountHint = null,
        IEnumerable<string>? tokens = null,
        bool isBooked = true,
        bool isPending = false,
        bool hasCounterpartyAccounts = true)
    {
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
            AccountHint: accountHint,
            IsBooked: isBooked,
            IsPending: isPending,
            HasProviderTransferHint: false,
            NearbySameAmountCount: 0,
            HasCounterpartyAccounts: hasCounterpartyAccounts,
            ReferenceEntropy: 0.6d);
    }
}
