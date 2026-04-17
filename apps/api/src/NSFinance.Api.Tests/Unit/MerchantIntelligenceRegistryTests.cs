using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
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

    [Theory]
    [InlineData("amazon", "Amazon Services", "amazon kindle charge")]
    [InlineData("google", "Google Services", "google cloud billing")]
    [InlineData("apple", "Apple Services", "apple digital service")]
    [InlineData("microsoft", "Microsoft Services", "microsoft office payment")]
    [InlineData("paypal", "PayPal Services", "paypal merchant payment")]
    public async Task ResolveAsync_DangerousFamilyBroadDescriptor_DoesNotResolveWithoutSpecificity(
        string familyToken,
        string canonicalName,
        string descriptor)
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                CanonicalName: canonicalName,
                DisplayName: canonicalName,
                MerchantStatus: MerchantStatus.Active,
                MerchantType: MerchantType.Merchant,
                MerchantUsageType: MerchantUsageType.MixedUse,
                PrimaryCountryCode: "US",
                OfficialWebsite: null,
                DescriptionSummary: null,
                ParentMerchantId: null),
            CancellationToken.None);

        var resolver = CreateResolver(dbContext, normalizer, registry);
        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);

        Assert.False(result.IsResolved);
        Assert.NotNull(result.UnresolvedMerchantId);
        Assert.Equal(MerchantAcceptanceDecisionType.Unresolved, result.AcceptanceDecisionType);
        Assert.Equal(familyToken, normalizer.Tokenize(result.NormalizedDescriptor).First());
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
            CreateDomainTriggerPolicy(),
            CreateAIGate(dbContext),
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
    public async Task ResolveAsync_UnresolvedCooldown_SkipsRepeatedInvestigationWithinWindow()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var investigation = new CountingMerchantInvestigationService(BuildInsufficientEvidenceResult);
        var resolver = new MerchantResolutionService(
            dbContext,
            normalizer,
            registry,
            investigation,
            CreateDomainTriggerPolicy(),
            CreateAIGate(dbContext),
            new MerchantAcceptancePolicy(),
            NullLogger<MerchantResolutionService>.Instance,
            Options.Create(new MerchantOperationalResilienceOptions
            {
                UnresolvedBaseCooldownMinutes = 180,
                UnresolvedMaxCooldownMinutes = 180,
                HighOccurrenceAccelerationThreshold = 50,
                HighOccurrenceAccelerationMinutes = 30
            }),
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true
            }));

        var first = await resolver.ResolveAsync("MYSTERY*SHOP", CancellationToken.None);
        var second = await resolver.ResolveAsync("MYSTERY*SHOP", CancellationToken.None);

        Assert.False(first.IsResolved);
        Assert.False(second.IsResolved);
        Assert.Equal(1, investigation.CallCount);
        Assert.Contains("merchant_on_cooldown", second.ReasonCodes);

        var unresolved = await dbContext.UnresolvedMerchants.SingleAsync();
        Assert.Equal(1, unresolved.InvestigationAttemptCount);
        Assert.Equal(UnresolvedMerchantStatus.AwaitingEvidence, unresolved.Status);
        Assert.True(unresolved.NextEligibleInvestigationUtc.HasValue);
        Assert.True(unresolved.NextEligibleInvestigationUtc.Value > DateTime.UtcNow.AddMinutes(170));
    }

    [Fact]
    public async Task ResolveAsync_HighOccurrenceAcceleration_AllowsRetryBeforeRegularCooldown()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var normalized = normalizer.Normalize("MYSTERY EURO SHOP");
        var nowUtc = DateTime.UtcNow;
        dbContext.UnresolvedMerchants.Add(new UnresolvedMerchant
        {
            Id = Guid.NewGuid(),
            RawDescriptor = "MYSTERY EURO SHOP",
            NormalizedDescriptor = normalized,
            FirstSeenUtc = nowUtc.AddHours(-5),
            LastSeenUtc = nowUtc.AddMinutes(-45),
            OccurrenceCount = 12,
            LastInvestigationUtc = nowUtc.AddMinutes(-45),
            NextEligibleInvestigationUtc = nowUtc.AddMinutes(40),
            InvestigationAttemptCount = 1,
            Status = UnresolvedMerchantStatus.AwaitingEvidence,
            Notes = "seeded_for_acceleration"
        });
        await dbContext.SaveChangesAsync();

        var investigation = new CountingMerchantInvestigationService(BuildInsufficientEvidenceResult);
        var resolver = new MerchantResolutionService(
            dbContext,
            normalizer,
            registry,
            investigation,
            CreateDomainTriggerPolicy(),
            CreateAIGate(dbContext),
            new MerchantAcceptancePolicy(),
            NullLogger<MerchantResolutionService>.Instance,
            Options.Create(new MerchantOperationalResilienceOptions
            {
                UnresolvedBaseCooldownMinutes = 120,
                UnresolvedMaxCooldownMinutes = 240,
                HighOccurrenceAccelerationThreshold = 10,
                HighOccurrenceAccelerationMinutes = 20
            }),
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true
            }));

        var result = await resolver.ResolveAsync("MYSTERY EURO SHOP", CancellationToken.None);

        Assert.False(result.IsResolved);
        Assert.Equal(0, investigation.CallCount);
        Assert.Contains("merchant_on_cooldown", result.ReasonCodes);

        var unresolved = await dbContext.UnresolvedMerchants.SingleAsync();
        Assert.Equal(1, unresolved.InvestigationAttemptCount);
        Assert.True(unresolved.LastInvestigationUtc.HasValue);
        Assert.True(unresolved.NextEligibleInvestigationUtc.HasValue);
    }

    [Fact]
    public async Task ResolveAsync_StaleResolvedMerchant_ExecutesRevalidationAndPersistsOutcomeRecord()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                "Stale Utility Co",
                "Stale Utility Co",
                MerchantStatus.Active,
                MerchantType.Utility,
                MerchantUsageType.NarrowUse,
                "IE",
                null,
                null,
                null),
            CancellationToken.None);
        merchant.NextValidationDueUtc = DateTime.UtcNow.AddDays(-2);
        merchant.LastValidatedUtc = DateTime.UtcNow.AddDays(-90);
        merchant.InvestigatedAtUtc = DateTime.UtcNow.AddDays(-30);
        merchant.InvestigationCooldownUntilUtc = DateTime.UtcNow.AddDays(-10);
        merchant.ValidationAttemptCount = 0;
        await dbContext.SaveChangesAsync();

        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                merchant.Id,
                "STALE UTILITY DD",
                MerchantAliasType.BillingDescriptor,
                1d,
                true,
                "seed",
                true),
            CancellationToken.None);

        var resolver = new MerchantResolutionService(
            dbContext,
            normalizer,
            registry,
            new StubMerchantInvestigationService(NullLogger<StubMerchantInvestigationService>.Instance),
            CreateDomainTriggerPolicy(),
            CreateAIGate(dbContext),
            new MerchantAcceptancePolicy(),
            NullLogger<MerchantResolutionService>.Instance,
            Options.Create(new MerchantOperationalResilienceOptions
            {
                CautiousMerchantValidationDays = 14
            }),
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true
            }),
            Options.Create(new AIIntegrationOptions
            {
                Enabled = true
            }),
            new OperationalFailureRecorder(dbContext, NullLogger<OperationalFailureRecorder>.Instance));

        var result = await resolver.ResolveAsync("STALE UTILITY DD", CancellationToken.None);

        Assert.True(result.IsResolved);

        var refreshed = await dbContext.Merchants.SingleAsync(x => x.Id == merchant.Id);
        Assert.Equal("mark_for_unresolved_review", refreshed.LastValidationResultCode);
        Assert.Equal(MerchantStatus.LowConfidence, refreshed.MerchantStatus);
        Assert.Equal(1, refreshed.ValidationAttemptCount);
        Assert.True(refreshed.NextValidationDueUtc.HasValue);
        Assert.True(refreshed.NextValidationDueUtc.Value > DateTime.UtcNow);

        var revalidation = await dbContext.MerchantRevalidationRecords
            .SingleOrDefaultAsync(x => x.MerchantId == merchant.Id);
        Assert.NotNull(revalidation);
        Assert.Equal(MerchantRevalidationOutcome.MarkedForUnresolvedReview, revalidation!.Outcome);
        Assert.Equal("mark_for_unresolved_review", revalidation.ResultCode);
        Assert.True(revalidation.RequiresUnresolvedReview);

        var failure = await dbContext.OperationalFailureRecords
            .SingleOrDefaultAsync(x => x.FailureType == "merchant_revalidation_signal");
        Assert.NotNull(failure);
        Assert.Equal(OperationalFailureArea.MerchantResolution, failure!.Area);
    }

    [Fact]
    public async Task AttachAliasAsync_Conflict_PersistsConflictRecordAndIncrementsOccurrence()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchantA = await registry.CreateMerchantAsync(
            new MerchantCreateRequest("Merchant A", "Merchant A", MerchantStatus.Active, MerchantType.Merchant, MerchantUsageType.NarrowUse, "IE", null, null, null),
            CancellationToken.None);
        var merchantB = await registry.CreateMerchantAsync(
            new MerchantCreateRequest("Merchant B", "Merchant B", MerchantStatus.Active, MerchantType.Merchant, MerchantUsageType.NarrowUse, "IE", null, null, null),
            CancellationToken.None);

        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                merchantA.Id,
                "CONFLICT-ALIAS-TEST",
                MerchantAliasType.BillingDescriptor,
                0.95d,
                true,
                "seed",
                true),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.AttachAliasAsync(
                new MerchantAliasCreateRequest(
                    merchantB.Id,
                    "CONFLICT-ALIAS-TEST",
                    MerchantAliasType.BillingDescriptor,
                    0.80d,
                    false,
                    "investigation",
                    true,
                    MerchantAliasTrustLevel.Cautious,
                    "conflict_attempt"),
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.AttachAliasAsync(
                new MerchantAliasCreateRequest(
                    merchantB.Id,
                    "CONFLICT-ALIAS-TEST",
                    MerchantAliasType.BillingDescriptor,
                    0.82d,
                    false,
                    "investigation",
                    true,
                    MerchantAliasTrustLevel.Cautious,
                    "conflict_attempt_repeat"),
                CancellationToken.None));

        var conflict = await dbContext.MerchantAliasConflicts.SingleAsync();
        Assert.Equal(2, conflict.OccurrenceCount);
        Assert.Equal(MerchantAliasConflictStatus.Open, conflict.Status);
        Assert.Equal(merchantA.Id, conflict.ExistingMerchantId);
        Assert.Equal(merchantB.Id, conflict.ProposedMerchantId);
        Assert.Equal(MerchantAliasTrustLevel.Cautious, conflict.ProposedTrustLevel);
    }

    [Fact]
    public void MerchantDescriptorNormalizer_HandlesDiacriticsAndProcessorNoise()
    {
        var normalizer = new MerchantDescriptorNormalizer();

        var normalized = normalizer.Normalize("DEBIT CARD PAYMENT: CAFÉ ÉNERGIE SÀRL 1234567");
        var tokens = normalizer.Tokenize(normalized);

        Assert.StartsWith("cafe energie", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("debit", tokens);
        Assert.DoesNotContain("payment", tokens);
        Assert.Contains("cafe", tokens);
        Assert.Contains("energie", tokens);
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
                    AliasCandidates: ["ACME INSURANCE DD"],
                    DescriptorMatchStrength: 0.94d,
                    EntityMatchStrength: 0.93d),
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
                    AliasCandidates: ["ACME FINANCIAL"],
                    DescriptorMatchStrength: 0.66d,
                    EntityMatchStrength: 0.63d)
            ],
                    Evidence:
                    [
                        new MerchantInvestigationEvidence(
                            MerchantEvidenceType.OfficialSource,
                            "Official billing descriptor matches insurer name.",
                            0.92d,
                            "https://acme-insurance.test/help",
                            SourceClass: "official_source",
                            Relevance: 0.95d,
                            SourceTrustLevel: MerchantSourceTrustLevel.OfficialDomain)
                    ],
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCandidate,
            OverallConfidence: 0.95d,
            AmbiguityLevel: 0.06d);

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
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCautiously,
            OverallConfidence: 0.69d,
            AmbiguityLevel: 0.31d);

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
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCautiously,
            OverallConfidence: 0.69d,
            AmbiguityLevel: 0.31d);

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

    [Fact]
    public void AcceptancePolicy_ReturnsUnresolved_ForHighConfidenceButWeakSourceTrust()
    {
        var policy = new MerchantAcceptancePolicy();
        var result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates:
            [
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Acme Premium Protect",
                    DisplayName: "Acme Premium Protect",
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.NarrowUse,
                    PrimaryCountryCode: "US",
                    Confidence: 0.95d,
                    AmbiguityScore: 0.12d,
                    MixedUseRisk: false,
                    HasContradictions: false,
                    OfficialWebsite: "https://acme-premium-protect.example",
                    DescriptionSummary: "Protection add-on",
                    AliasCandidates: ["ACME PREMIUM PROTECT"],
                    DescriptorMatchStrength: 0.93d,
                    EntityMatchStrength: 0.90d)
            ],
            Evidence:
            [
                new MerchantInvestigationEvidence(
                    MerchantEvidenceType.AI,
                    "Model inferred likely match from public snippets.",
                    0.87d,
                    null,
                    SourceClass: "ai_inference",
                    Relevance: 0.74d,
                    SourceTrustLevel: MerchantSourceTrustLevel.AIInferenceOnly)
            ],
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCandidate,
            OverallConfidence: 0.94d,
            AmbiguityLevel: 0.12d);

        var decision = policy.Evaluate(result);

        Assert.Equal(MerchantAcceptanceDecisionType.Unresolved, decision.DecisionType);
        Assert.Contains("weak_source_trust_profile", decision.ReasonCodes);
        Assert.Contains("acceptance_blocked_identity_trust", decision.ReasonCodes);
    }

    [Fact]
    public void AcceptancePolicy_ReturnsUnresolved_ForDomainNameMismatchAndWeakSources()
    {
        var policy = new MerchantAcceptancePolicy();
        var result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates:
            [
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Northbridge Energy Services",
                    DisplayName: "Northbridge Energy Services",
                    MerchantType: MerchantType.Utility,
                    MerchantUsageType: MerchantUsageType.NarrowUse,
                    PrimaryCountryCode: "GB",
                    Confidence: 0.90d,
                    AmbiguityScore: 0.18d,
                    MixedUseRisk: false,
                    HasContradictions: false,
                    OfficialWebsite: "https://nbx-payments.co",
                    DescriptionSummary: "Utility billing descriptor",
                    AliasCandidates: ["NORTHBRIDGE ENERGY"],
                    DescriptorMatchStrength: 0.87d,
                    EntityMatchStrength: 0.84d,
                    DomainNameMismatchRisk: true)
            ],
            Evidence:
            [
                new MerchantInvestigationEvidence(
                    MerchantEvidenceType.TransactionObservation,
                    "Descriptor overlap only.",
                    0.71d,
                    null,
                    SourceClass: "web_mention",
                    Relevance: 0.62d,
                    SourceTrustLevel: MerchantSourceTrustLevel.WeakWebMention)
            ],
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCandidate,
            OverallConfidence: 0.90d,
            AmbiguityLevel: 0.18d);

        var decision = policy.Evaluate(result);

        Assert.Equal(MerchantAcceptanceDecisionType.Unresolved, decision.DecisionType);
        Assert.Contains("domain_name_mismatch_risk", decision.ReasonCodes);
        Assert.Contains("weak_source_trust_profile", decision.ReasonCodes);
    }

    [Fact]
    public void AcceptancePolicy_ReturnsUnresolved_ForGenericMerchantNameWithoutAuthoritativeSource()
    {
        var policy = new MerchantAcceptancePolicy();
        var result = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates:
            [
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Digital Services",
                    DisplayName: "Digital Services",
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.MixedUse,
                    PrimaryCountryCode: "US",
                    Confidence: 0.83d,
                    AmbiguityScore: 0.30d,
                    MixedUseRisk: true,
                    HasContradictions: false,
                    OfficialWebsite: "https://digital-services-hub.info",
                    DescriptionSummary: "Generic merchant candidate",
                    AliasCandidates: ["DIGITAL SERVICES"],
                    DescriptorMatchStrength: 0.79d,
                    EntityMatchStrength: 0.77d)
            ],
            Evidence:
            [
                new MerchantInvestigationEvidence(
                    MerchantEvidenceType.TransactionObservation,
                    "Recurring descriptor found in user history.",
                    0.66d,
                    null,
                    SourceClass: "public_listing",
                    Relevance: 0.60d,
                    SourceTrustLevel: MerchantSourceTrustLevel.PublicDirectory)
            ],
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCautiously,
            OverallConfidence: 0.78d,
            AmbiguityLevel: 0.30d);

        var decision = policy.Evaluate(result);

        Assert.Equal(MerchantAcceptanceDecisionType.Unresolved, decision.DecisionType);
        Assert.Contains("generic_merchant_name_risk", decision.ReasonCodes);
        Assert.Contains(
            decision.ReasonCodes,
            code => code is "acceptance_blocked_generic_name_without_authoritative_source" or "acceptance_blocked_identity_trust");
    }

    [Fact]
    public async Task ResolveAsync_D3Domain_ReturnsSuggestionOnly_NotAutoResolved()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var resolver = CreateResolver(
            dbContext,
            normalizer,
            registry,
            new TestMerchantInvestigationService());

        var result = await resolver.ResolveAsync(
            new MerchantResolutionRequest(
                RawDescriptor: "CHARITY DONATION HOUSE",
                UserId: Guid.NewGuid(),
                ConnectionId: Guid.NewGuid(),
                SyncRunId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                NormalizedTransactionId: Guid.NewGuid(),
                TaxonomyDomainId: 240,
                TaxonomyCategoryId: null,
                TaxonomySubcategoryId: null,
                DeterministicTerminal: false,
                DeterministicResultCode: "not_terminal",
                ManualOverridePresent: false,
                Amount: -120m,
                DescriptorMerchantLike: true,
                TriggerSource: "unit_test",
                RunState: new MerchantResolutionRunState(Guid.NewGuid())),
            CancellationToken.None);

        Assert.False(result.IsResolved);
        Assert.Equal(MerchantResolutionFinalState.AIEnrichedSuggestionOnly, result.FinalState);
        Assert.Equal(DomainTriggerMode.D3, result.TriggerMode);
        Assert.Equal(AITriggerSkipReason.UserConfirmationPreferred, result.AIGateSkipReason);
    }

    [Fact]
    public async Task ResolveAsync_UnknownMerchantAlone_DoesNotTriggerInvestigation()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var investigation = new CountingMerchantInvestigationService(BuildInsufficientEvidenceResult);
        var resolver = CreateResolver(
            dbContext,
            normalizer,
            registry,
            investigation);

        var result = await resolver.ResolveAsync(
            new MerchantResolutionRequest(
                RawDescriptor: "UNKNOWN MERCHANT 123456",
                UserId: Guid.NewGuid(),
                ConnectionId: Guid.NewGuid(),
                SyncRunId: Guid.NewGuid(),
                TransactionId: Guid.NewGuid(),
                NormalizedTransactionId: Guid.NewGuid(),
                TaxonomyDomainId: 130,
                TaxonomyCategoryId: null,
                TaxonomySubcategoryId: null,
                DeterministicTerminal: false,
                DeterministicResultCode: "not_terminal",
                ManualOverridePresent: false,
                Amount: -4.50m,
                DescriptorMerchantLike: true,
                TriggerSource: "unit_test",
                RunState: new MerchantResolutionRunState(Guid.NewGuid())),
            CancellationToken.None);

        Assert.False(result.IsResolved);
        Assert.Equal(0, investigation.CallCount);
        Assert.Contains("expected_value_too_low", result.ReasonCodes);
    }

    [Fact]
    public async Task ResolveAsync_TwentyTransactionsSameMerchant_InvokesAIOncePerRun()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var investigation = new CountingMerchantInvestigationService(BuildInsufficientEvidenceResult);
        var resolver = CreateResolver(dbContext, normalizer, registry, investigation);
        var runState = new MerchantResolutionRunState(Guid.NewGuid());

        for (var i = 0; i < 20; i++)
        {
            _ = await resolver.ResolveAsync(
                new MerchantResolutionRequest(
                    RawDescriptor: "RECURRING COFFEE SHOP",
                    UserId: Guid.NewGuid(),
                    ConnectionId: Guid.NewGuid(),
                    SyncRunId: runState.SyncRunId,
                    TransactionId: Guid.NewGuid(),
                    NormalizedTransactionId: Guid.NewGuid(),
                    TaxonomyDomainId: 130,
                    TaxonomyCategoryId: null,
                    TaxonomySubcategoryId: null,
                    DeterministicTerminal: false,
                    DeterministicResultCode: "not_terminal",
                    ManualOverridePresent: false,
                    Amount: -120m,
                    DescriptorMerchantLike: true,
                    TriggerSource: "unit_test",
                    RunState: runState),
                CancellationToken.None);
        }

        Assert.Equal(1, investigation.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_RevalidationContradiction_PersistsSignalsWithoutSilentOverwrite()
    {
        await using var dbContext = CreateDbContext();
        var normalizer = new MerchantDescriptorNormalizer();
        var registry = CreateRegistry(dbContext, normalizer);
        var merchant = await registry.CreateMerchantAsync(
            new MerchantCreateRequest(
                "Prime Utilities",
                "Prime Utilities",
                MerchantStatus.Active,
                MerchantType.Utility,
                MerchantUsageType.NarrowUse,
                "GB",
                "https://prime-utilities.test",
                "Seed merchant for revalidation contradiction test",
                null),
            CancellationToken.None);

        merchant.NextValidationDueUtc = DateTime.UtcNow.AddDays(-1);
        merchant.LastValidatedUtc = DateTime.UtcNow.AddDays(-60);
        merchant.InvestigatedAtUtc = DateTime.UtcNow.AddDays(-30);
        merchant.InvestigationCooldownUntilUtc = DateTime.UtcNow.AddDays(-10);
        merchant.ValidationAttemptCount = 0;
        await dbContext.SaveChangesAsync();

        await registry.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                merchant.Id,
                "PRIME UTILITIES DD",
                MerchantAliasType.BillingDescriptor,
                0.97d,
                true,
                "seed",
                true,
                MerchantAliasTrustLevel.Trusted),
            CancellationToken.None);

        var resolver = new MerchantResolutionService(
            dbContext,
            normalizer,
            registry,
            new ContradictoryRevalidationInvestigationService(),
            CreateDomainTriggerPolicy(),
            CreateAIGate(dbContext),
            new MerchantAcceptancePolicy(),
            NullLogger<MerchantResolutionService>.Instance,
            Options.Create(new MerchantOperationalResilienceOptions
            {
                CautiousMerchantValidationDays = 14
            }),
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true
            }),
            Options.Create(new AIIntegrationOptions
            {
                Enabled = true
            }),
            new OperationalFailureRecorder(dbContext, NullLogger<OperationalFailureRecorder>.Instance));

        var resolve = await resolver.ResolveAsync("PRIME UTILITIES DD", CancellationToken.None);

        Assert.True(resolve.IsResolved);
        Assert.Equal(MerchantResolutionType.ExactAlias, resolve.ResolutionType);

        var refreshed = await dbContext.Merchants.SingleAsync(x => x.Id == merchant.Id);
        Assert.Equal(MerchantStatus.LowConfidence, refreshed.MerchantStatus);
        Assert.Equal("mark_for_unresolved_review", refreshed.LastValidationResultCode);
        Assert.Equal(1, refreshed.ValidationAttemptCount);

        var revalidation = await dbContext.MerchantRevalidationRecords.SingleAsync(x => x.MerchantId == merchant.Id);
        Assert.Equal(MerchantRevalidationOutcome.MarkedForUnresolvedReview, revalidation.Outcome);
        Assert.True(revalidation.ContradictionDetected);
        Assert.True(revalidation.StatusChanged);
        Assert.True(revalidation.RequiresUnresolvedReview);

        var alias = await dbContext.MerchantAliases.SingleAsync(x => x.MerchantId == merchant.Id);
        Assert.Equal(MerchantAliasTrustLevel.Cautious, alias.TrustLevel);
        Assert.True(alias.IsActive);

        var revalidationEvidence = await dbContext.MerchantEvidence
            .Where(x => x.MerchantId == merchant.Id)
            .ToListAsync();
        Assert.Contains(
            revalidationEvidence,
            x => x.EvidenceSummary.Contains("Revalidation flagged", StringComparison.OrdinalIgnoreCase));
        Assert.True(await dbContext.OperationalFailureRecords.AnyAsync(x => x.FailureType == "merchant_revalidation_signal"));
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
        return CreateResolver(
            dbContext,
            normalizer,
            registryService,
            new StubMerchantInvestigationService(NullLogger<StubMerchantInvestigationService>.Instance));
    }

    private static MerchantResolutionService CreateResolver(
        AppDbContext dbContext,
        MerchantDescriptorNormalizer normalizer,
        IMerchantRegistryService registryService,
        IMerchantInvestigationService investigationService)
    {
        return new MerchantResolutionService(
            dbContext,
            normalizer,
            registryService,
            investigationService,
            CreateDomainTriggerPolicy(),
            CreateAIGate(dbContext),
            new MerchantAcceptancePolicy(),
            NullLogger<MerchantResolutionService>.Instance,
            null,
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true
            }));
    }

    private static IDomainTriggerPolicyService CreateDomainTriggerPolicy()
    {
        return new DomainTriggerPolicyService();
    }

    private static IAITriggerGateService CreateAIGate(AppDbContext dbContext)
    {
        return new AITriggerGateService(
            dbContext,
            Options.Create(new MerchantAIGovernanceOptions
            {
                Enabled = true,
                AllowD1AIByDefault = true,
                MaxAICallsPerSyncRun = 20,
                MaxAICallsPerConnectionPerRun = 20,
                MaxAICallsPerUserPer24h = 100
            }),
            Options.Create(new AIIntegrationOptions
            {
                Enabled = true
            }));
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
                            AliasCandidates: [request.RawDescriptor],
                            DescriptorMatchStrength: 0.95d,
                            EntityMatchStrength: 0.94d)
                    ],
                    Evidence:
                    [
                        new MerchantInvestigationEvidence(
                            MerchantEvidenceType.OfficialSource,
                            "Descriptor and official billing page align.",
                            0.92d,
                            "https://acme-life-insurance.test/help",
                            SourceClass: "official_source",
                            Relevance: 0.93d,
                            SourceTrustLevel: MerchantSourceTrustLevel.OfficialDomain)
                    ],
                    FailureReason: null,
                    Recommendation: MerchantInvestigationRecommendation.AcceptCandidate,
                    OverallConfidence: 0.94d,
                    AmbiguityLevel: 0.05d));
        }
    }

    private static MerchantInvestigationResult BuildInsufficientEvidenceResult(MerchantInvestigationRequest _)
    {
        return new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: true,
            Candidates: [],
            Evidence: [],
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.InsufficientEvidence,
            OverallConfidence: 0.22d,
            AmbiguityLevel: 0.84d);
    }

    private sealed class CountingMerchantInvestigationService(Func<MerchantInvestigationRequest, MerchantInvestigationResult> responseFactory)
        : IMerchantInvestigationService
    {
        public int CallCount { get; private set; }

        public Task<MerchantInvestigationResult> InvestigateAsync(MerchantInvestigationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount += 1;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class ContradictoryRevalidationInvestigationService : IMerchantInvestigationService
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
                            CanonicalName: "Prime Utilities",
                            DisplayName: "Prime Utilities",
                            MerchantType: MerchantType.Utility,
                            MerchantUsageType: MerchantUsageType.NarrowUse,
                            PrimaryCountryCode: "GB",
                            Confidence: 0.91d,
                            AmbiguityScore: 0.20d,
                            MixedUseRisk: false,
                            HasContradictions: true,
                            OfficialWebsite: "https://prime-utilities.test",
                            DescriptionSummary: "Contradictory revalidation candidate",
                            AliasCandidates: [request.RawDescriptor],
                            DescriptorMatchStrength: 0.86d,
                            EntityMatchStrength: 0.82d,
                            WhyItMayMatch: "Descriptor is close to known merchant.",
                            WhyItMayBeWrong: "Contradictory legal identity surfaced during review.")
                    ],
                    Evidence:
                    [
                        new MerchantInvestigationEvidence(
                            MerchantEvidenceType.TransactionObservation,
                            "Contradictory legal ownership records detected.",
                            0.78d,
                            "unit-test",
                            SourceClass: "authoritative_listing",
                            Relevance: 0.72d,
                            SourceTrustLevel: MerchantSourceTrustLevel.AuthoritativeListing)
                    ],
                    FailureReason: null,
                    Recommendation: MerchantInvestigationRecommendation.AcceptCautiously,
                    OverallConfidence: 0.82d,
                    AmbiguityLevel: 0.24d));
        }
    }
}
