using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class UserFinancialCommitmentServiceTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateManualAsync_PersistsUserOwnedCommitmentAndAtomicAudit()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);

        var result = await service.CreateManualAsync(
            new CreateManualFinancialCommitmentRequest(
                null,
                "Car insurance",
                "yearly",
                UtcNow,
                UtcNow.AddYears(1),
                UtcNow.AddDays(10),
                480m,
                "eur",
                false),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.StartsWith("user_manual:", result.Value!.Id, StringComparison.Ordinal);
        var row = Assert.Single(dbContext.UserFinancialCommitments);
        Assert.Equal("manual", row.OriginType);
        Assert.Equal(1, row.Revision);
        var item = Assert.Single(await service.ApplyAsync([], false, CancellationToken.None));
        Assert.Equal("Car insurance", item.Label);
        Assert.Equal(480m, item.NextAmount);
        Assert.Equal("EUR", item.Currency);
        Assert.Equal("user", item.Source);
        Assert.NotNull(item.UserDecision);
        Assert.Equal(1, item.UserDecision.Revision);
        Assert.Equal("manual", item.UserDecision.DecisionMode);
        var audit = Assert.Single(dbContext.AuditEvents);
        Assert.Equal("manual_created", audit.EventName);
        Assert.DoesNotContain("Car insurance", audit.MetadataJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("480", audit.MetadataJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecideAsync_ConfirmInference_RemovesReviewGateAndKeepsEvidence()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var inferred = CreateCommitment("inferred_recurring:test", "inferred", "needs_review");

        var mutation = await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("confirm"),
            CancellationToken.None);

        Assert.True(mutation.Succeeded);
        Assert.Equal(1, mutation.Value!.Revision);
        var item = Assert.Single(await service.ApplyAsync([inferred], false, CancellationToken.None));
        Assert.Equal("active", item.Lifecycle);
        Assert.Equal("user_confirmed", item.Confidence);
        Assert.DoesNotContain("requires_user_confirmation", item.Exclusions);
        Assert.Contains(item.Evidence, evidence => evidence.Type == "transaction_pattern");
        Assert.Contains(item.Evidence, evidence => evidence.Type == "user_confirmation");
        Assert.Equal(1, item.UserDecision!.Revision);
        Assert.Equal("confirm", item.UserDecision.LastAction);
    }

    [Fact]
    public async Task DecideAsync_DismissAndReactivate_IsSoftReversible()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var inferred = CreateCommitment("inferred_recurring:reversible", "inferred", "needs_review");
        await service.DecideAsync(inferred.Id, inferred, Decision("confirm"), CancellationToken.None);

        var dismissed = await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("dismiss", 1),
            CancellationToken.None);

        Assert.Equal(2, dismissed.Value!.Revision);
        Assert.Empty(await service.ApplyAsync([inferred], false, CancellationToken.None));
        var includedDismissed = Assert.Single(await service.ApplyAsync([inferred], true, CancellationToken.None));
        Assert.Equal("dismissed", includedDismissed.Lifecycle);
        Assert.Equal(2, includedDismissed.UserDecision!.Revision);

        var reactivated = await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("reactivate", 2),
            CancellationToken.None);

        Assert.Equal(3, reactivated.Value!.Revision);
        var active = Assert.Single(await service.ApplyAsync([inferred], false, CancellationToken.None));
        Assert.Equal("active", active.Lifecycle);
        Assert.Equal(3, active.UserDecision!.Revision);
        Assert.Contains(active.Evidence, evidence => evidence.ReasonCodes.Contains("reactivated_by_user"));
    }

    [Fact]
    public async Task DecideAsync_CorrectionAndReset_PreservesLiveProviderFacts()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var provider = CreateCommitment("provider_direct_debit:test", "provider", "active");

        var corrected = await service.DecideAsync(
            provider.Id,
            provider,
            Decision(
                "correct",
                label: "Corrected label",
                nextDateUtc: UtcNow.AddDays(20),
                nextAmount: 35m,
                currency: "eur",
                isVariableAmount: false),
            CancellationToken.None);

        Assert.Equal(1, corrected.Value!.Revision);
        var effective = Assert.Single(await service.ApplyAsync([provider], false, CancellationToken.None));
        Assert.Equal("Corrected label", effective.Label);
        Assert.Equal(35m, effective.NextAmount);
        Assert.Equal("user_override", effective.Source);
        Assert.Equal(1, effective.UserDecision!.Revision);
        Assert.Equal("corrected", effective.UserDecision.DecisionMode);
        Assert.Contains(effective.Evidence, evidence => evidence.Type == "provider_direct_debit");
        Assert.Contains(effective.Evidence, evidence => evidence.Type == "user_correction");

        var reset = await service.DecideAsync(
            provider.Id,
            provider,
            Decision("correct", 1, resetFields: ["label"]),
            CancellationToken.None);

        Assert.Equal(2, reset.Value!.Revision);
        effective = Assert.Single(await service.ApplyAsync([provider], false, CancellationToken.None));
        Assert.Equal(provider.Label, effective.Label);
        Assert.Equal(35m, effective.NextAmount);
    }

    [Fact]
    public async Task ApplyAsync_MissingConfirmedSource_DegradesToNeedsReview()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var inferred = CreateCommitment("inferred_recurring:missing", "inferred", "needs_review");
        await service.DecideAsync(inferred.Id, inferred, Decision("confirm"), CancellationToken.None);

        var item = Assert.Single(await service.ApplyAsync([], false, CancellationToken.None));

        Assert.Equal("needs_review", item.Lifecycle);
        Assert.Equal("unknown", item.Freshness);
        Assert.Contains("source_commitment_unavailable", item.Exclusions);
    }

    [Fact]
    public async Task ApplyAsync_IsolatesManualRowsByUser()
    {
        await using var dbContext = CreateDbContext();
        var firstUser = await SeedUserAsync(dbContext, "EUR");
        var secondUser = await SeedUserAsync(dbContext, "USD");
        var firstService = CreateService(dbContext, firstUser);
        var secondService = CreateService(dbContext, secondUser);
        await firstService.CreateManualAsync(Manual("First bill", "EUR"), CancellationToken.None);
        await secondService.CreateManualAsync(Manual("Second bill", "USD"), CancellationToken.None);

        var firstItem = Assert.Single(await firstService.ApplyAsync([], false, CancellationToken.None));
        var secondItem = Assert.Single(await secondService.ApplyAsync([], false, CancellationToken.None));

        Assert.Equal("First bill", firstItem.Label);
        Assert.Equal("Second bill", secondItem.Label);
    }

    [Fact]
    public async Task DecideAsync_StaleRevision_ReturnsConflictWithoutMutation()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var inferred = CreateCommitment("inferred_recurring:conflict", "inferred", "needs_review");
        await service.DecideAsync(inferred.Id, inferred, Decision("confirm"), CancellationToken.None);

        var conflict = await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("dismiss", 99),
            CancellationToken.None);

        Assert.False(conflict.Succeeded);
        Assert.Equal("commitment_revision_conflict", conflict.Error!.Code);
        Assert.Equal(1, Assert.Single(dbContext.UserFinancialCommitments).Revision);
        Assert.Single(dbContext.AuditEvents);
    }

    [Fact]
    public async Task CreateManualAsync_RejectsAmountWithoutCurrencyOrAccount()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);

        var result = await service.CreateManualAsync(
            Manual("Unknown currency", null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("commitment_currency_required", result.Error!.Code);
        Assert.Empty(dbContext.UserFinancialCommitments);
        Assert.Empty(dbContext.AuditEvents);
    }

    [Fact]
    public async Task CreateAndCorrect_RejectAccountsOwnedByAnotherUser()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var otherUserId = await SeedUserAsync(dbContext, "EUR");
        var otherAccountId = Guid.NewGuid();
        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = otherAccountId,
            UserId = otherUserId,
            Name = "Other user's current account",
            Type = "current",
            Currency = "EUR",
            CreatedUtc = UtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, userId);

        var manualResult = await service.CreateManualAsync(
            Manual("Foreign account bill", null) with { AccountId = otherAccountId },
            CancellationToken.None);
        var provider = CreateCommitment("provider_direct_debit:foreign-account", "provider", "active");
        var correctionResult = await service.DecideAsync(
            provider.Id,
            provider,
            Decision("correct", accountId: otherAccountId),
            CancellationToken.None);

        Assert.False(manualResult.Succeeded);
        Assert.Equal("commitment_account_not_found", manualResult.Error!.Code);
        Assert.False(correctionResult.Succeeded);
        Assert.Equal("commitment_account_not_found", correctionResult.Error!.Code);
        Assert.Empty(dbContext.UserFinancialCommitments);
        Assert.Empty(dbContext.AuditEvents);
    }

    [Fact]
    public async Task ApplyAsync_MalformedStoredSnapshot_IsSkippedWithoutFailingTheRead()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        dbContext.UserFinancialCommitments.Add(new UserFinancialCommitment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OriginType = "manual",
            State = "active",
            DecisionMode = "manual",
            LastAction = "create",
            SnapshotJson = "{not-valid-json",
            Revision = 1,
            CreatedUtc = UtcNow.UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, userId);

        var result = await service.ApplyAsync([], false, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ManualCommitment_CanBeCorrectedDismissedAndReactivated()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var created = await service.CreateManualAsync(Manual("Original bill", "EUR"), CancellationToken.None);

        var corrected = await service.DecideAsync(
            created.Value!.Id,
            null,
            Decision("correct", 1, label: "Updated bill", nextAmount: 45m),
            CancellationToken.None);
        var correctedItem = Assert.Single(await service.ApplyAsync([], false, CancellationToken.None));

        var dismissed = await service.DecideAsync(
            created.Value.Id,
            null,
            Decision("dismiss", 2),
            CancellationToken.None);
        var hiddenItems = await service.ApplyAsync([], false, CancellationToken.None);
        var dismissedItem = Assert.Single(await service.ApplyAsync([], true, CancellationToken.None));

        var reactivated = await service.DecideAsync(
            created.Value.Id,
            null,
            Decision("reactivate", 3),
            CancellationToken.None);

        Assert.Equal(2, corrected.Value!.Revision);
        Assert.Equal("Updated bill", correctedItem.Label);
        Assert.Equal(45m, correctedItem.NextAmount);
        Assert.Equal(3, dismissed.Value!.Revision);
        Assert.Empty(hiddenItems);
        Assert.Equal("dismissed", dismissedItem.Lifecycle);
        Assert.Equal(4, reactivated.Value!.Revision);
        Assert.Equal("active", Assert.Single(await service.ApplyAsync([], false, CancellationToken.None)).Lifecycle);
    }

    [Fact]
    public async Task DecideAsync_RejectsIgnoredOrEmptyCorrectionPayloads()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var inferred = CreateCommitment("inferred_recurring:action-payload", "inferred", "needs_review");

        var ignoredFields = await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("confirm", label: "Must not be ignored"),
            CancellationToken.None);
        var emptyCorrection = await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("correct"),
            CancellationToken.None);

        Assert.False(ignoredFields.Succeeded);
        Assert.Equal("commitment_action_fields_invalid", ignoredFields.Error!.Code);
        Assert.False(emptyCorrection.Succeeded);
        Assert.Equal("commitment_correction_required", emptyCorrection.Error!.Code);
        Assert.Empty(dbContext.UserFinancialCommitments);
        Assert.Empty(dbContext.AuditEvents);
    }

    [Fact]
    public async Task DismissThenReactivate_InferredCommitmentStillNeedsConfirmation()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var service = CreateService(dbContext, userId);
        var inferred = CreateCommitment("inferred_recurring:not-confirmed", "inferred", "needs_review");

        var dismissed = await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("dismiss"),
            CancellationToken.None);
        await service.DecideAsync(
            inferred.Id,
            inferred,
            Decision("reactivate", dismissed.Value!.Revision),
            CancellationToken.None);

        var item = Assert.Single(await service.ApplyAsync([inferred], false, CancellationToken.None));
        Assert.Equal("needs_review", item.Lifecycle);
        Assert.Equal("strong", item.Confidence);
        Assert.Contains("requires_user_confirmation", item.Exclusions);
    }

    [Fact]
    public async Task ApplyAsync_MalformedCorrectionDocument_IsSkipped()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "EUR");
        var provider = CreateCommitment("provider_direct_debit:bad-override", "provider", "active");
        dbContext.UserFinancialCommitments.Add(new UserFinancialCommitment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetCommitmentId = provider.Id,
            OriginType = "decision",
            State = "active",
            DecisionMode = "corrected",
            LastAction = "correct",
            SnapshotJson = UserFinancialCommitmentProjector.SerializeSnapshot(provider),
            OverrideJson = "{not-valid-json",
            Revision = 1,
            CreatedUtc = UtcNow.UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, userId);

        var result = await service.ApplyAsync([provider], false, CancellationToken.None);

        Assert.Empty(result);
    }

    private static UserFinancialCommitmentService CreateService(AppDbContext dbContext, Guid userId)
    {
        return new UserFinancialCommitmentService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new TestTimeProvider(UtcNow),
            new TestRequestContextAccessor(),
            NullLogger<UserFinancialCommitmentService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"user-financial-commitment-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext, string currency)
    {
        var userId = Guid.NewGuid();
        var email = $"commitment-user-{userId:N}@local";
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = email,
            NormalizedEmail = email,
            DisplayName = "Commitment User",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = UtcNow.UtcDateTime,
            UpdatedUtc = UtcNow.UtcDateTime,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-IE",
            PreferredCurrency = currency,
            PlanTier = "standard"
        });
        await dbContext.SaveChangesAsync();
        return userId;
    }

    private static FinancialCommitmentDto CreateCommitment(
        string id,
        string source,
        string lifecycle)
    {
        var evidenceType = source == "provider" ? "provider_direct_debit" : "transaction_pattern";
        return new FinancialCommitmentDto(
            id,
            source == "provider" ? "direct_debit" : "inferred_recurring",
            lifecycle,
            source,
            source == "provider" ? "confirmed" : "strong",
            source == "provider" ? 100d : 82d,
            "outflow",
            null,
            null,
            string.Empty,
            "Original label",
            "monthly",
            UtcNow.UtcDateTime.AddMonths(-3),
            null,
            UtcNow.UtcDateTime.AddMonths(-1),
            25m,
            "EUR",
            UtcNow.UtcDateTime.AddDays(10),
            source == "provider" ? "provider_reported" : "estimated",
            25m,
            "EUR",
            source == "provider" ? "provider_reported" : "estimated",
            false,
            UtcNow.UtcDateTime.AddHours(-1),
            "fresh",
            false,
            source == "provider" ? "active" : null,
            source == "inferred" ? ["requires_user_confirmation"] : [],
            [new FinancialCommitmentEvidenceDto(
                evidenceType,
                Guid.NewGuid(),
                UtcNow.UtcDateTime.AddMonths(-1),
                source == "provider" ? "provider_fact" : "inferred_signal",
                [])]);
    }

    private static CreateManualFinancialCommitmentRequest Manual(string label, string? currency)
    {
        return new CreateManualFinancialCommitmentRequest(
            null,
            label,
            "monthly",
            null,
            null,
            UtcNow.AddDays(5),
            20m,
            currency,
            false);
    }

    private static FinancialCommitmentDecisionRequest Decision(
        string action,
        int? revision = null,
        string? label = null,
        DateTimeOffset? nextDateUtc = null,
        decimal? nextAmount = null,
        string? currency = null,
        bool? isVariableAmount = null,
        IReadOnlyList<string>? resetFields = null,
        Guid? accountId = null)
    {
        return new FinancialCommitmentDecisionRequest(
            action,
            revision,
            accountId,
            false,
            label,
            null,
            false,
            nextDateUtc,
            false,
            nextAmount,
            false,
            currency,
            false,
            isVariableAmount,
            false,
            resetFields);
    }

    private sealed class TestCurrentUserProvider(Guid userId) : ICurrentUserProvider
    {
        public Guid UserId => userId;

        public bool TryGetUserId(out Guid resolvedUserId)
        {
            resolvedUserId = userId;
            return true;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = Guid.Empty;
            return false;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "user-commitment-test";
        public string SourceChannel => "test";
        public string? IpAddress => null;
        public string? UserAgent => null;
        public string? Platform => "test";
        public string? AppVersion => "test";
    }
}
