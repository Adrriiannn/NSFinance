using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class MerchantIntelligenceRegistryTests
{
    [Fact]
    public async Task ResolveAsync_ExactAliasMatch_ReturnsResolvedMerchant()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                CanonicalName: "Netflix",
                DisplayName: "Netflix",
                MerchantStatus: MerchantStatus.Active,
                MerchantType: MerchantType.Merchant,
                MerchantUsageType: MerchantUsageType.NarrowUse,
                PrimaryCountryCode: "IE",
                OfficialWebsite: "https://www.netflix.com",
                DescriptionSummary: "Streaming service",
                ParentMerchantId: null),
            CancellationToken.None);
        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                MerchantId: merchant.Id,
                AliasText: "NETFLIX.COM",
                AliasType: MerchantAliasType.BillingDescriptor,
                Confidence: 1d,
                IsExactMatchPreferred: true,
                Source: "manual",
                IsActive: true),
            CancellationToken.None);

        var resolver = CreateResolver(dbContext, normalizer, registry);
        var result = await resolver.ResolveAsync(" netflix.com ", CancellationToken.None);

        Assert.True(result.IsResolved);
        Assert.Equal(merchant.Id, result.MerchantId);
        Assert.Equal(MerchantResolutionType.ExactAlias, result.ResolutionType);
    }

    [Fact]
    public async Task ResolveAsync_FuzzyAliasMatch_ReturnsResolvedMerchant()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                CanonicalName: "OpenAI Platform",
                DisplayName: "OpenAI",
                MerchantStatus: MerchantStatus.Active,
                MerchantType: MerchantType.Merchant,
                MerchantUsageType: MerchantUsageType.NarrowUse,
                PrimaryCountryCode: "US",
                OfficialWebsite: "https://openai.com",
                DescriptionSummary: "AI platform subscription",
                ParentMerchantId: null),
            CancellationToken.None);
        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                MerchantId: merchant.Id,
                AliasText: "OPENAI API PLATFORM PAYMENT LTD",
                AliasType: MerchantAliasType.BillingDescriptor,
                Confidence: 1d,
                IsExactMatchPreferred: true,
                Source: "manual",
                IsActive: true),
            CancellationToken.None);

        var resolver = CreateResolver(dbContext, normalizer, registry);
        var result = await resolver.ResolveAsync("openai api platform payment", CancellationToken.None);

        Assert.True(result.IsResolved);
        Assert.Equal(merchant.Id, result.MerchantId);
        Assert.Equal(MerchantResolutionType.FuzzyAlias, result.ResolutionType);
    }

    [Fact]
    public async Task ResolveAsync_FuzzyDangerousFamily_DoesNotOverResolveMixedUseDescriptor()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                CanonicalName: "Amazon Prime",
                DisplayName: "Amazon Prime",
                MerchantStatus: MerchantStatus.Active,
                MerchantType: MerchantType.Merchant,
                MerchantUsageType: MerchantUsageType.MixedUse,
                PrimaryCountryCode: "IE",
                OfficialWebsite: "https://amazon.test",
                DescriptionSummary: "Prime membership",
                ParentMerchantId: null),
            CancellationToken.None);
        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                MerchantId: merchant.Id,
                AliasText: "AMAZON PRIME MEMBERSHIP",
                AliasType: MerchantAliasType.BillingDescriptor,
                Confidence: 0.96d,
                IsExactMatchPreferred: false,
                Source: "seed",
                IsActive: true),
            CancellationToken.None);

        var resolver = CreateResolver(dbContext, normalizer, registry);
        var result = await resolver.ResolveAsync("amazon marketplace order", CancellationToken.None);

        Assert.False(result.IsResolved);
        Assert.NotNull(result.UnresolvedMerchantId);
        Assert.Equal(MerchantAcceptanceDecisionType.Unresolved, result.AcceptanceDecisionType);
    }

    [Fact]
    public async Task ResolveAsync_FamilyDangerousFamily_RequiresExactCanonicalName()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                CanonicalName: "Google Services",
                DisplayName: "Google Services",
                MerchantStatus: MerchantStatus.Active,
                MerchantType: MerchantType.Merchant,
                MerchantUsageType: MerchantUsageType.MixedUse,
                PrimaryCountryCode: "US",
                OfficialWebsite: null,
                DescriptionSummary: null,
                ParentMerchantId: null),
            CancellationToken.None);

        var resolver = CreateResolver(dbContext, normalizer, registry);
        var result = await resolver.ResolveAsync("google youtube premium", CancellationToken.None);

        Assert.False(result.IsResolved);
        Assert.NotNull(result.UnresolvedMerchantId);
        Assert.Equal(MerchantAcceptanceDecisionType.Unresolved, result.AcceptanceDecisionType);
    }

    [Fact]
    public async Task ResolveAsync_ExactAliasStillWins_ForDangerousMerchantFamily()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                CanonicalName: "PayPal Spotify",
                DisplayName: "PayPal Spotify",
                MerchantStatus: MerchantStatus.Active,
                MerchantType: MerchantType.Intermediary,
                MerchantUsageType: MerchantUsageType.MixedUse,
                PrimaryCountryCode: "US",
                OfficialWebsite: null,
                DescriptionSummary: null,
                ParentMerchantId: null),
            CancellationToken.None);
        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                MerchantId: merchant.Id,
                AliasText: "PAYPAL *SPOTIFY",
                AliasType: MerchantAliasType.BillingDescriptor,
                Confidence: 1d,
                IsExactMatchPreferred: true,
                Source: "seed",
                IsActive: true),
            CancellationToken.None);

        var resolver = CreateResolver(dbContext, normalizer, registry);
        var result = await resolver.ResolveAsync("paypal spotify", CancellationToken.None);

        Assert.True(result.IsResolved);
        Assert.Equal(MerchantResolutionType.ExactAlias, result.ResolutionType);
        Assert.Equal(merchant.Id, result.MerchantId);
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_TracksUnresolvedAndIncrementsOccurrence()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var resolver = CreateResolver(dbContext, normalizer, registry);

        var first = await resolver.ResolveAsync("Unknown Shop 123456789", CancellationToken.None);
        var second = await resolver.ResolveAsync("unknown shop 123456789", CancellationToken.None);

        Assert.False(first.IsResolved);
        Assert.False(second.IsResolved);
        Assert.NotNull(second.UnresolvedMerchantId);

        var unresolved = await dbContext.UnresolvedMerchants.SingleAsync();
        Assert.Equal(2, unresolved.OccurrenceCount);
        Assert.Contains("[redacted-number]", unresolved.RawDescriptor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_MultipleExactAliases_PrefersPreferredAlias()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                "Acme Media",
                "Acme Media",
                MerchantStatus.Active,
                MerchantType.Merchant,
                MerchantUsageType.NarrowUse,
                "IE",
                null,
                null,
                null),
            CancellationToken.None);

        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                merchant.Id,
                "ACME MEDIA LTD",
                MerchantAliasType.MerchantName,
                0.80d,
                false,
                "seed",
                true),
            CancellationToken.None);
        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                merchant.Id,
                "ACME MEDIA LTD",
                MerchantAliasType.BillingDescriptor,
                0.72d,
                true,
                "seed",
                true),
            CancellationToken.None);

        var resolver = CreateResolver(dbContext, normalizer, registry);
        var result = await resolver.ResolveAsync("acme media ltd", CancellationToken.None);

        Assert.True(result.IsResolved);
        Assert.Equal(MerchantResolutionType.ExactAlias, result.ResolutionType);
        Assert.Equal("ACME MEDIA LTD", result.MatchedAlias);
    }

    [Fact]
    public async Task ResolveAsync_AcceptedInvestigation_CreatesMerchantAndResolves()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var resolver = new MerchantResolutionService(
            dbContext,
            normalizer,
            registry,
            new TestMerchantInvestigationService(),
            new MerchantAcceptancePolicy(),
            NullLogger<MerchantResolutionService>.Instance);

        var result = await resolver.ResolveAsync("ACME LIFE INSURANCE DD", CancellationToken.None);

        Assert.True(result.IsResolved);
        Assert.NotNull(result.MerchantId);
        Assert.Equal(MerchantAcceptanceDecisionType.AcceptedTrusted, result.AcceptanceDecisionType);

        var merchant = await dbContext.Merchants.SingleAsync(x => x.Id == result.MerchantId);
        Assert.Equal("Acme Life Insurance", merchant.CanonicalName);
        Assert.True(await dbContext.MerchantAliases.AnyAsync(x => x.MerchantId == merchant.Id));
        Assert.True(await dbContext.MerchantEvidence.AnyAsync(x => x.MerchantId == merchant.Id));
    }

    [Fact]
    public async Task AttachAliasAsync_PreventsCrossMerchantAliasContamination()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchantA = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                "Spotify",
                "Spotify",
                MerchantStatus.Active,
                MerchantType.Merchant,
                MerchantUsageType.NarrowUse,
                "IE",
                null,
                null,
                null),
            CancellationToken.None);
        var merchantB = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                "Spotify Mirror",
                "Spotify Mirror",
                MerchantStatus.LowConfidence,
                MerchantType.Merchant,
                MerchantUsageType.MixedUse,
                "IE",
                null,
                null,
                null),
            CancellationToken.None);
        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                merchantA.Id,
                "SPOTIFY",
                MerchantAliasType.BillingDescriptor,
                0.99d,
                true,
                "manual",
                true),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.AttachAliasAsync(
                new MerchantAliasCreateRequest(
                    merchantB.Id,
                    "spotify",
                    MerchantAliasType.BillingDescriptor,
                    0.75d,
                    false,
                    "manual",
                    true),
                CancellationToken.None));
    }

    [Fact]
    public async Task RegistryService_StoresAndReturnsMerchantIntelligencePackage()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                "Prime Utility",
                "Prime Utility",
                MerchantStatus.Active,
                MerchantType.Utility,
                MerchantUsageType.NarrowUse,
                "GB",
                "https://utility.test",
                "Monthly electric utility provider.",
                null),
            CancellationToken.None);

        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                merchant.Id,
                "PRIME UTILITY DIRECT DEBIT",
                MerchantAliasType.BillingDescriptor,
                0.94d,
                true,
                "deterministic",
                true),
            CancellationToken.None);
        await registry.UpsertBehaviorProfileAsync(
            new MerchantBehaviorProfileUpsertRequest(
                merchant.Id,
                SupportsSubscriptions: false,
                SupportsRecurringPayments: true,
                SupportsOneTimePurchases: false,
                SupportsMarketplacePayments: false,
                SupportsInAppPurchases: false,
                AnnualRenewalsCommon: false,
                RefundsCommon: false,
                MixedUseRisk: false,
                PaymentBehaviorConfidence: 0.86d,
                BehaviorSummary: "Recurring utility direct debit pattern."),
            CancellationToken.None);
        await registry.AddCategoryHintAsync(
            new MerchantCategoryHintCreateRequest(
                merchant.Id,
                DomainId: 140,
                CategoryId: 14010,
                SubcategoryId: 140101,
                Confidence: 0.89d,
                HintStrength: MerchantHintStrength.Strong,
                Source: "deterministic",
                IsActive: true),
            CancellationToken.None);
        await registry.AddEvidenceAsync(
            new MerchantEvidenceCreateRequest(
                merchant.Id,
                MerchantEvidenceType.Deterministic,
                "Observed stable recurring utility descriptor over several months.",
                0.88d,
                "unit-test"),
            CancellationToken.None);

        var package = await registry.GetMerchantIntelligencePackageAsync(merchant.Id, CancellationToken.None);

        Assert.NotNull(package);
        Assert.Single(package!.Aliases);
        Assert.NotNull(package.BehaviorProfile);
        Assert.Single(package.CategoryHints);
        Assert.Single(package.Evidence);
    }

    [Fact]
    public void AcceptancePolicy_ReturnsAcceptedTrusted_ForStrongUnambiguousEvidence()
    {
        var policy = new MerchantAcceptancePolicy();
        var result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates:
            [
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Acme Insurance",
                    DisplayName: "Acme Insurance",
                    MerchantType: MerchantType.Insurer,
                    MerchantUsageType: MerchantUsageType.NarrowUse,
                    PrimaryCountryCode: "IE",
                    Confidence: 0.96d,
                    AmbiguityScore: 0.03d,
                    MixedUseRisk: false,
                    HasContradictions: false,
                    OfficialWebsite: "https://acme-insurance.test",
                    DescriptionSummary: "Insurance provider",
                    AliasCandidates: ["ACME INSURANCE DD"]),
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Acme Financial",
                    DisplayName: "Acme Financial",
                    MerchantType: MerchantType.Institution,
                    MerchantUsageType: MerchantUsageType.MixedUse,
                    PrimaryCountryCode: "IE",
                    Confidence: 0.71d,
                    AmbiguityScore: 0.24d,
                    MixedUseRisk: true,
                    HasContradictions: false,
                    OfficialWebsite: null,
                    DescriptionSummary: null,
                    AliasCandidates: ["ACME FINANCIAL"])
            ],
            Evidence:
            [
                new MerchantInvestigationEvidence(
                    MerchantEvidenceType.OfficialSource,
                    "Official billing descriptor matches insurer name.",
                    0.92d,
                    "https://acme-insurance.test/help")
            ],
            FailureReason: null);

        var decision = policy.Evaluate(result);

        Assert.Equal(MerchantAcceptanceDecisionType.AcceptedTrusted, decision.DecisionType);
        Assert.NotNull(decision.SelectedCandidate);
    }

    [Fact]
    public void AcceptancePolicy_ReturnsRejected_WhenTopCandidateHasContradictions()
    {
        var policy = new MerchantAcceptancePolicy();
        var result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates:
            [
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Conflicted Merchant",
                    DisplayName: "Conflicted Merchant",
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.MixedUse,
                    PrimaryCountryCode: "US",
                    Confidence: 0.91d,
                    AmbiguityScore: 0.28d,
                    MixedUseRisk: true,
                    HasContradictions: true,
                    OfficialWebsite: null,
                    DescriptionSummary: null,
                    AliasCandidates: ["CONFLICTED MERCHANT"])
            ],
            Evidence: [],
            FailureReason: null);

        var decision = policy.Evaluate(result);

        Assert.Equal(MerchantAcceptanceDecisionType.Rejected, decision.DecisionType);
        Assert.Contains("contradictory_evidence", decision.ReasonCodes);
    }

    [Fact]
    public void AcceptancePolicy_ReturnsLowConfidence_ForAmbiguousMixedUseCandidate()
    {
        var policy = new MerchantAcceptancePolicy();
        var result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates:
            [
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Acme Group",
                    DisplayName: "Acme Group",
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.MixedUse,
                    PrimaryCountryCode: "US",
                    Confidence: 0.69d,
                    AmbiguityScore: 0.31d,
                    MixedUseRisk: true,
                    HasContradictions: false,
                    OfficialWebsite: null,
                    DescriptionSummary: null,
                    AliasCandidates: ["ACME GROUP"]),
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Acme Services",
                    DisplayName: "Acme Services",
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.MixedUse,
                    PrimaryCountryCode: "US",
                    Confidence: 0.64d,
                    AmbiguityScore: 0.33d,
                    MixedUseRisk: true,
                    HasContradictions: false,
                    OfficialWebsite: null,
                    DescriptionSummary: null,
                    AliasCandidates: ["ACME SERVICES"])
            ],
            Evidence: [],
            FailureReason: null);

        var decision = policy.Evaluate(result);

        Assert.Equal(MerchantAcceptanceDecisionType.LowConfidence, decision.DecisionType);
    }

    [Fact]
    public void AcceptancePolicy_ReturnsUnresolved_WhenEvidenceIsInsufficient()
    {
        var policy = new MerchantAcceptancePolicy();
        var result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: true,
            Candidates: [],
            Evidence: [],
            FailureReason: null);

        var decision = policy.Evaluate(result);

        Assert.Equal(MerchantAcceptanceDecisionType.Unresolved, decision.DecisionType);
        Assert.Contains("insufficient_evidence", decision.ReasonCodes);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"merchant-intelligence-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static MerchantRegistryService CreateRegistry(AppDbContext dbContext, MerchantDescriptorNormalizer normalizer)
    {
        return new MerchantRegistryService(
            dbContext,
            normalizer,
            NullLogger<MerchantRegistryService>.Instance);
    }

    private static MerchantResolutionService CreateResolver(
        AppDbContext dbContext,
        MerchantDescriptorNormalizer normalizer,
        IMerchantRegistryService registryService)
    {
        return new MerchantResolutionService(
            dbContext,
            normalizer,
            registryService,
            new StubMerchantInvestigationService(NullLogger<StubMerchantInvestigationService>.Instance),
            new MerchantAcceptancePolicy(),
            NullLogger<MerchantResolutionService>.Instance);
    }

    private sealed class TestMerchantInvestigationService : IMerchantInvestigationService
    {
        public Task<MerchantInvestigationResult> InvestigateAsync(
            MerchantInvestigationRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new MerchantInvestigationResult(
                    Succeeded: true,
                    InsufficientEvidence: false,
                    Candidates:
                    [
                        new MerchantInvestigationCandidate(
                            ExistingMerchantId: null,
                            CanonicalName: "Acme Life Insurance",
                            DisplayName: "Acme Life Insurance",
                            MerchantType: MerchantType.Insurer,
                            MerchantUsageType: MerchantUsageType.NarrowUse,
                            PrimaryCountryCode: "IE",
                            Confidence: 0.95d,
                            AmbiguityScore: 0.03d,
                            MixedUseRisk: false,
                            HasContradictions: false,
                            OfficialWebsite: "https://acme-life-insurance.test",
                            DescriptionSummary: "Insurance direct debit provider",
                            AliasCandidates: [request.RawDescriptor])
                    ],
                    Evidence:
                    [
                        new MerchantInvestigationEvidence(
                            MerchantEvidenceType.OfficialSource,
                            "Descriptor and official billing page align.",
                            0.92d,
                            "https://acme-life-insurance.test/help")
                    ],
                    FailureReason: null));
        }
    }
}
