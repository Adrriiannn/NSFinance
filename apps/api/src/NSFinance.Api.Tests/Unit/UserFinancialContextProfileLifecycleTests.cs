using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class UserFinancialContextProfileLifecycleTests
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    [Fact]
    public async Task GetOrCreateAsync_ProfileAbsent_CreatesGovernedLifecycleProfile()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId, preferredCurrency: "EUR", country: "IE", adviceTone: "balanced");
        var tools = new ProfileToolStubs();
        var lifecycleOptions = CreateLifecycleOptions(profileSchemaVersion: 3);
        var sut = CreateService(dbContext, tools, lifecycleOptions);

        var snapshot = await sut.GetOrCreateAsync(userId, CancellationToken.None);

        var persisted = await dbContext.UserFinancialContextProfiles.SingleAsync(x => x.UserId == userId);
        Assert.Equal("IE", snapshot.Country);
        Assert.Equal("EUR", snapshot.Currency);
        Assert.Equal("fresh", persisted.FreshnessState);
        Assert.Equal(3, persisted.ProfileSchemaVersion);
        Assert.NotEqual(default, persisted.CreatedUtc);
        Assert.NotEqual(default, persisted.UpdatedUtc);
        Assert.NotEqual(default, persisted.LastRefreshedUtc);
        Assert.Equal(1, tools.SummaryCalls);
        Assert.Equal(1, tools.BudgetCalls);
        Assert.Equal(1, tools.RecurringCalls);
        Assert.Equal(1, tools.SpendingCalls);
    }

    [Fact]
    public async Task GetOrCreateAsync_FreshProfile_ReusesWithoutRefreshingInferredSignals()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId, preferredCurrency: "GBP", country: "GB", adviceTone: "conservative");
        var nowUtc = DateTime.UtcNow;
        dbContext.UserFinancialContextProfiles.Add(new UserFinancialContextProfile
        {
            UserId = userId,
            Country = "GB",
            Currency = "GBP",
            MonthlyIncomeRange = "2000-4000",
            KnownObligationsJson = "[]",
            BudgetStructureJson = "{}",
            ActivePlansJson = "[]",
            SpendingTendenciesJson = "[]",
            CategoryFlexibilityMarkersJson = "[]",
            AdviceStylePreference = "conservative",
            ExplicitSignalsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["country"] = "GB",
                ["currency"] = "GBP",
                ["advice_style_preference"] = "conservative"
            }),
            InferredSignalsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["monthly_income_range"] = "2000-4000"
            }),
            SignalMetadataJson = JsonSerializer.Serialize(
                new Dictionary<string, UserFinancialProfileSignalMetadata>
                {
                    ["monthly_income_range"] = new(
                        UserFinancialProfileSignalSource.InferredFromSummary,
                        UserFinancialProfileSignalStrength.Acceptable,
                        IsExplicit: false,
                        UpdatedAtUtc: nowUtc.AddDays(-1))
                },
                MetadataJsonOptions),
            FreshnessState = "fresh",
            ProfileSchemaVersion = 1,
            LastRefreshedUtc = nowUtc.AddHours(-2),
            CreatedUtc = nowUtc.AddDays(-3),
            UpdatedUtc = nowUtc.AddHours(-2)
        });
        await dbContext.SaveChangesAsync();

        var tools = new ProfileToolStubs();
        var lifecycleOptions = CreateLifecycleOptions();
        var sut = CreateService(dbContext, tools, lifecycleOptions);

        _ = await sut.GetOrCreateAsync(userId, CancellationToken.None);

        Assert.Equal(0, tools.SummaryCalls);
        Assert.Equal(0, tools.BudgetCalls);
        Assert.Equal(0, tools.RecurringCalls);
        Assert.Equal(0, tools.SpendingCalls);
    }

    [Fact]
    public async Task GetOrCreateAsync_RefreshNeeded_UpdatesLifecycleAndReplacesWeakerInferredSignals()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId, preferredCurrency: "EUR", country: "IE", adviceTone: "balanced");
        var createdUtc = DateTime.UtcNow.AddDays(-10);
        var lastRefreshedUtc = DateTime.UtcNow.AddHours(-120);
        dbContext.UserFinancialContextProfiles.Add(new UserFinancialContextProfile
        {
            UserId = userId,
            Country = "IE",
            Currency = "EUR",
            MonthlyIncomeRange = "0-2000",
            KnownObligationsJson = "[]",
            BudgetStructureJson = "{}",
            ActivePlansJson = "[]",
            SpendingTendenciesJson = """{"averageDailySpend":3,"largestExpense":12,"spendByDomain":{"130":10}}""",
            CategoryFlexibilityMarkersJson = "[]",
            AdviceStylePreference = "balanced",
            ExplicitSignalsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["country"] = "IE",
                ["currency"] = "EUR",
                ["advice_style_preference"] = "balanced"
            }),
            InferredSignalsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["spending_tendencies"] = """{"averageDailySpend":3,"largestExpense":12,"spendByDomain":{"130":10}}"""
            }),
            SignalMetadataJson = JsonSerializer.Serialize(
                new Dictionary<string, UserFinancialProfileSignalMetadata>
                {
                    ["spending_tendencies"] = new(
                        UserFinancialProfileSignalSource.InferredFromSpendingPattern,
                        UserFinancialProfileSignalStrength.Weak,
                        IsExplicit: false,
                        UpdatedAtUtc: DateTime.UtcNow.AddDays(-6))
                },
                MetadataJsonOptions),
            FreshnessState = "refresh_needed",
            ProfileSchemaVersion = 1,
            LastRefreshedUtc = lastRefreshedUtc,
            CreatedUtc = createdUtc,
            UpdatedUtc = DateTime.UtcNow.AddDays(-6)
        });
        await dbContext.SaveChangesAsync();

        var tools = new ProfileToolStubs
        {
            Spending = new SpendingAnalysisResult(
                SpendByDomain: new Dictionary<int, decimal> { [130] = 460m, [210] = 190m, [220] = 120m },
                AverageDailySpend: 28m,
                LargestExpense: 145m)
        };
        var lifecycleOptions = CreateLifecycleOptions(profileSchemaVersion: 2);
        var sut = CreateService(dbContext, tools, lifecycleOptions);

        _ = await sut.GetOrCreateAsync(userId, CancellationToken.None);

        var persisted = await dbContext.UserFinancialContextProfiles.SingleAsync(x => x.UserId == userId);
        var metadata = DeserializeMetadata(persisted.SignalMetadataJson);

        Assert.Equal("fresh", persisted.FreshnessState);
        Assert.Equal(2, persisted.ProfileSchemaVersion);
        Assert.True(persisted.LastRefreshedUtc > lastRefreshedUtc);
        Assert.Equal(createdUtc, persisted.CreatedUtc);
        Assert.True(persisted.UpdatedUtc >= persisted.LastRefreshedUtc);
        Assert.Contains("averageDailySpend", persisted.SpendingTendenciesJson, StringComparison.Ordinal);
        Assert.True(metadata.TryGetValue("spending_tendencies", out var spendingMetadata));
        Assert.Equal(UserFinancialProfileSignalStrength.Strong, spendingMetadata!.Strength);
        Assert.Equal(UserFinancialProfileSignalSource.InferredFromSpendingPattern, spendingMetadata.Source);
    }

    [Fact]
    public async Task GetOrCreateAsync_ExplicitSignalsOutrankConflictingInferredSignals()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId, preferredCurrency: "GBP", country: "GB", adviceTone: "conservative");
        dbContext.UserFinancialContextProfiles.Add(new UserFinancialContextProfile
        {
            UserId = userId,
            Country = "GB",
            Currency = "USD",
            MonthlyIncomeRange = "2000-4000",
            KnownObligationsJson = "[]",
            BudgetStructureJson = "{}",
            ActivePlansJson = "[]",
            SpendingTendenciesJson = "[]",
            CategoryFlexibilityMarkersJson = "[]",
            AdviceStylePreference = "balanced",
            ExplicitSignalsJson = "{}",
            InferredSignalsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["currency"] = "USD",
                ["advice_style_preference"] = "flexible"
            }),
            SignalMetadataJson = JsonSerializer.Serialize(
                new Dictionary<string, UserFinancialProfileSignalMetadata>
                {
                    ["currency"] = new(
                        UserFinancialProfileSignalSource.InferredFromSummary,
                        UserFinancialProfileSignalStrength.Strong,
                        IsExplicit: false,
                        UpdatedAtUtc: DateTime.UtcNow.AddDays(-2))
                },
                MetadataJsonOptions),
            FreshnessState = "fresh",
            ProfileSchemaVersion = 1,
            LastRefreshedUtc = DateTime.UtcNow.AddHours(-3),
            CreatedUtc = DateTime.UtcNow.AddDays(-4),
            UpdatedUtc = DateTime.UtcNow.AddHours(-3)
        });
        await dbContext.SaveChangesAsync();

        var tools = new ProfileToolStubs
        {
            Summary = new UserFinancialSummary(2000m, 1200m, 800m, "USD")
        };
        var sut = CreateService(dbContext, tools, CreateLifecycleOptions());

        _ = await sut.GetOrCreateAsync(userId, CancellationToken.None);

        var persisted = await dbContext.UserFinancialContextProfiles.SingleAsync(x => x.UserId == userId);
        var metadata = DeserializeMetadata(persisted.SignalMetadataJson);
        Assert.Equal("GBP", persisted.Currency);
        Assert.Equal("conservative", persisted.AdviceStylePreference);
        Assert.True(metadata.TryGetValue("currency", out var currencyMetadata));
        Assert.True(currencyMetadata!.IsExplicit);
        Assert.Equal(UserFinancialProfileSignalSource.ExplicitUser, currencyMetadata.Source);
        Assert.Equal(UserFinancialProfileSignalStrength.Explicit, currencyMetadata.Strength);
    }

    [Fact]
    public async Task GetOrCreateAsync_WeakOrNoisyInferenceDoesNotOverwriteStrongerPersistedSignal()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        SeedUser(dbContext, userId, preferredCurrency: "EUR", country: "IE", adviceTone: "balanced");
        var originalSpending = """{"averageDailySpend":24,"largestExpense":130,"spendByDomain":{"130":320,"210":220}}""";
        dbContext.UserFinancialContextProfiles.Add(new UserFinancialContextProfile
        {
            UserId = userId,
            Country = "IE",
            Currency = "EUR",
            MonthlyIncomeRange = "2000-4000",
            KnownObligationsJson = """[{"name":"Rent","amount":900}]""",
            BudgetStructureJson = "{}",
            ActivePlansJson = "[]",
            SpendingTendenciesJson = originalSpending,
            CategoryFlexibilityMarkersJson = "[]",
            AdviceStylePreference = "balanced",
            ExplicitSignalsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["country"] = "IE",
                ["currency"] = "EUR",
                ["advice_style_preference"] = "balanced"
            }),
            InferredSignalsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["spending_tendencies"] = originalSpending,
                ["known_obligations"] = """[{"name":"Rent","amount":900}]"""
            }),
            SignalMetadataJson = JsonSerializer.Serialize(
                new Dictionary<string, UserFinancialProfileSignalMetadata>
                {
                    ["spending_tendencies"] = new(
                        UserFinancialProfileSignalSource.InferredFromSpendingPattern,
                        UserFinancialProfileSignalStrength.Strong,
                        IsExplicit: false,
                        UpdatedAtUtc: DateTime.UtcNow.AddDays(-3)),
                    ["known_obligations"] = new(
                        UserFinancialProfileSignalSource.InferredFromRecurringObligations,
                        UserFinancialProfileSignalStrength.Strong,
                        IsExplicit: false,
                        UpdatedAtUtc: DateTime.UtcNow.AddDays(-3))
                },
                MetadataJsonOptions),
            FreshnessState = "refresh_needed",
            ProfileSchemaVersion = 1,
            LastRefreshedUtc = DateTime.UtcNow.AddHours(-120),
            CreatedUtc = DateTime.UtcNow.AddDays(-20),
            UpdatedUtc = DateTime.UtcNow.AddHours(-120)
        });
        await dbContext.SaveChangesAsync();

        var tools = new ProfileToolStubs
        {
            Recurring = new RecurringObligationsResult([], 0m),
            Spending = new SpendingAnalysisResult(new Dictionary<int, decimal>(), 0m, 0m)
        };
        var sut = CreateService(dbContext, tools, CreateLifecycleOptions());

        _ = await sut.GetOrCreateAsync(userId, CancellationToken.None);

        var persisted = await dbContext.UserFinancialContextProfiles.SingleAsync(x => x.UserId == userId);
        Assert.Equal(originalSpending, persisted.SpendingTendenciesJson);
        Assert.Contains("Rent", persisted.KnownObligationsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshnessEvaluator_TransitionsAcrossFreshStaleRefreshNeededThresholds()
    {
        var options = Options.Create(CreateLifecycleOptions(staleAfterHours: 24, refreshNeededAfterHours: 72));
        var evaluator = new UserFinancialProfileFreshnessEvaluator(options);
        var nowUtc = DateTime.UtcNow;

        var fresh = evaluator.Evaluate(nowUtc, nowUtc.AddHours(-4));
        var stale = evaluator.Evaluate(nowUtc, nowUtc.AddHours(-36));
        var refreshNeeded = evaluator.Evaluate(nowUtc, nowUtc.AddHours(-90));

        Assert.Equal(UserFinancialProfileFreshnessState.Fresh, fresh);
        Assert.Equal(UserFinancialProfileFreshnessState.Stale, stale);
        Assert.Equal(UserFinancialProfileFreshnessState.RefreshNeeded, refreshNeeded);
    }

    private static UserFinancialContextProfileService CreateService(
        AppDbContext dbContext,
        ProfileToolStubs tools,
        CompanionProfileLifecycleOptions options)
    {
        var optionsWrapper = Options.Create(options);
        var mapper = new UserFinancialProfileSerializationMapper();
        var mergePolicy = new UserFinancialProfileMergePolicy();
        var inferenceBuilder = new UserFinancialProfileInferenceBuilder(
            dbContext,
            tools,
            tools,
            tools,
            tools,
            optionsWrapper,
            NullLogger<UserFinancialProfileInferenceBuilder>.Instance);
        var persistencePolicy = new UserFinancialProfileInferencePersistencePolicy();
        var metadataPolicy = new UserFinancialProfileSignalMetadataPolicy();
        var invariantValidator = new UserFinancialProfileLifecycleInvariantValidator();
        var freshnessEvaluator = new UserFinancialProfileFreshnessEvaluator(optionsWrapper);
        return new UserFinancialContextProfileService(
            dbContext,
            mapper,
            mergePolicy,
            inferenceBuilder,
            persistencePolicy,
            metadataPolicy,
            invariantValidator,
            freshnessEvaluator,
            optionsWrapper,
            NullLogger<UserFinancialContextProfileService>.Instance);
    }

    private static Dictionary<string, UserFinancialProfileSignalMetadata> DeserializeMetadata(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, UserFinancialProfileSignalMetadata>>(json, MetadataJsonOptions) ?? [];
    }

    private static CompanionProfileLifecycleOptions CreateLifecycleOptions(
        int staleAfterHours = 24,
        int refreshNeededAfterHours = 72,
        int profileSchemaVersion = 1)
    {
        return new CompanionProfileLifecycleOptions
        {
            StaleAfterHours = staleAfterHours,
            RefreshNeededAfterHours = refreshNeededAfterHours,
            ProfileSchemaVersion = profileSchemaVersion,
            MaxActivePlans = 5,
            MaxRecurringObligations = 12,
            SpendingAnalysisLookbackDays = 60,
            StrongRecurringMonthlyTotalThreshold = 150m,
            StrongLargestExpenseThreshold = 75m,
            StrongAverageDailySpendThreshold = 8m,
            StrongSpendingDomainCountThreshold = 2,
            StrongMonthToDateSpendThreshold = 150m
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"profile-lifecycle-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedUser(
        AppDbContext dbContext,
        Guid userId,
        string preferredCurrency,
        string? country,
        string adviceTone)
    {
        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = $"{userId:N}@example.com",
            NormalizedEmail = $"{userId:N}@example.com",
            DisplayName = "Profile User",
            FullName = "Profile User",
            Role = "user",
            Status = "active",
            OnboardingStatus = "completed",
            Timezone = "UTC",
            Locale = "en-GB",
            PreferredCurrency = preferredCurrency,
            PlanTier = "standard",
            CreatedUtc = DateTime.UtcNow.AddDays(-20),
            UpdatedUtc = DateTime.UtcNow.AddDays(-1),
            CountryRegion = country
        });
        dbContext.UserPreferences.Add(new UserPreference
        {
            UserId = userId,
            AdviceTonePreference = adviceTone,
            DigestFrequency = "weekly",
            ReminderPreference = "important_only",
            NotificationPreferencesJson = "{}",
            PrivacyPreferencesJson = "{}",
            EssentialCategoryPreferencesJson = "{}",
            FutureGoalConfigurationJson = "{}",
            UpdatedUtc = DateTime.UtcNow
        });
        dbContext.SaveChanges();
    }

    private sealed class ProfileToolStubs :
        IUserFinancialSummaryService,
        IRecurringObligationsService,
        IBudgetStatusService,
        ISpendingAnalysisService
    {
        public UserFinancialSummary Summary { get; set; } = new(3000m, 1900m, 1100m, "EUR");
        public RecurringObligationsResult Recurring { get; set; } = new(
            [new RecurringObligationItem("Rent", 900m, "EUR", 30), new RecurringObligationItem("Gym", 45m, "EUR", 30)],
            945m);
        public BudgetStatusResult Budget { get; set; } = new(true, 2200m, 1250m, 950m);
        public SpendingAnalysisResult Spending { get; set; } = new(
            new Dictionary<int, decimal> { [130] = 420m, [210] = 180m, [220] = 90m },
            22m,
            130m);

        public int SummaryCalls { get; private set; }
        public int RecurringCalls { get; private set; }
        public int BudgetCalls { get; private set; }
        public int SpendingCalls { get; private set; }

        public Task<UserFinancialSummary> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SummaryCalls += 1;
            return Task.FromResult(Summary);
        }

        public Task<RecurringObligationsResult> GetRecurringAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecurringCalls += 1;
            return Task.FromResult(Recurring);
        }

        public Task<BudgetStatusResult> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BudgetCalls += 1;
            return Task.FromResult(Budget);
        }

        public Task<SpendingAnalysisResult> AnalyzeAsync(Guid userId, int lookbackDays, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpendingCalls += 1;
            return Task.FromResult(Spending);
        }
    }
}
