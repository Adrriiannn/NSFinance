using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using ChatStateSnapshot = NSFinance.Api.Modules.AI.Services.ConversationStateSnapshot;

namespace NSFinance.Api.Tests.Unit;

public sealed class PersistentConversationMemoryTests
{
    [Fact]
    public async Task ConversationThreadAndMessageServices_PersistAndOrderMessages()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "ordered-messages");

        var threadService = new ConversationThreadService(dbContext, NullLogger<ConversationThreadService>.Instance);
        var messageService = new ConversationMessageService(dbContext);

        var thread = await threadService.CreateThreadAsync(userId, "Budget Chat", CancellationToken.None);
        await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(ConversationMessageRole.User, "hello"),
            CancellationToken.None);
        await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(ConversationMessageRole.Assistant, "how can I help?"),
            CancellationToken.None);

        var messages = await messageService.GetRecentMessagesAsync(userId, thread.Id, 10, CancellationToken.None);

        Assert.Equal(2, messages.Count);
        Assert.Equal(1, messages[0].MessageOrder);
        Assert.Equal(2, messages[1].MessageOrder);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await messageService.GetRecentMessagesAsync(Guid.NewGuid(), thread.Id, 10, CancellationToken.None));
    }

    [Fact]
    public async Task PersistentConversationContextService_BuildsSummaryStateAndDiagnostics()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "context-build");
        var options = CreateAiOptionsWithMemory();

        var threadService = new ConversationThreadService(dbContext, NullLogger<ConversationThreadService>.Instance);
        var messageService = new ConversationMessageService(dbContext);
        var stateService = new ConversationStateService(dbContext, Options.Create(options));
        var summaryService = new ConversationSummaryService(
            dbContext,
            Options.Create(options),
            new DeterministicConversationSummaryGenerator());
        var contextService = new PersistentConversationContextService(
            dbContext,
            messageService,
            stateService,
            summaryService,
            Options.Create(options),
            NullLogger<PersistentConversationContextService>.Instance);

        var thread = await threadService.CreateThreadAsync(userId, "Utilities", CancellationToken.None);
        await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(ConversationMessageRole.User, "hello"),
            CancellationToken.None);
        await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(ConversationMessageRole.User, "My electricity bill keeps rising", Topic: "utilities"),
            CancellationToken.None);
        await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(ConversationMessageRole.Assistant, "Let's compare last 3 months", Topic: "utilities"),
            CancellationToken.None);
        await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(ConversationMessageRole.User, "I want a lower fixed bill", Topic: "utilities"),
            CancellationToken.None);

        await stateService.SaveSnapshotAsync(
            userId,
            thread.Id,
            new ChatStateSnapshot(
                ActiveTopic: "utilities",
                UserIntent: "reduce recurring bills",
                Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["currency"] = "EUR",
                    ["country"] = "IE"
                },
                Summaries: ["User wants to reduce energy spending."],
                BudgetPreference: "conservative",
                LocationPreference: "Ireland",
                MerchantInvestigationSubject: null,
                RecentConclusions: ["Need tariff comparison"]),
            ConversationStateSnapshotReason.UserTurn,
            CancellationToken.None);

        var result = await contextService.BuildContextAsync(
            new PersistentConversationContextBuildRequest(
                UserId: userId,
                ConversationThreadId: thread.Id,
                TaskType: AITaskType.UserChatComplex,
                ModelClass: AIModelClass.HeavyReasoning,
                CorrelationId: "ctx-1",
                CurrentUserMessage: null,
                IncludeCurrentUserMessage: false),
            CancellationToken.None);

        Assert.NotEmpty(result.ContextMessages);
        Assert.False(string.IsNullOrWhiteSpace(result.ContextSummary));
        Assert.NotEmpty(result.StructuredState);
        Assert.Contains("active_topic", result.StructuredState.Keys);
        Assert.True(result.IncludedMessageIds.Count <= options.Memory.ComplexChat.MaxRecentMessages);
        Assert.Contains("context_built", result.ReasonCodes);
        Assert.Equal(1, await dbContext.ConversationContextBuildLogs.CountAsync());
    }

    [Fact]
    public async Task PersistentConversationContextService_AppliesTokenBudgetTrim()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "token-trim");
        var options = CreateAiOptionsWithMemory();

        var threadService = new ConversationThreadService(dbContext, NullLogger<ConversationThreadService>.Instance);
        var messageService = new ConversationMessageService(dbContext);
        var stateService = new ConversationStateService(dbContext, Options.Create(options));
        var summaryService = new ConversationSummaryService(
            dbContext,
            Options.Create(options),
            new DeterministicConversationSummaryGenerator());
        var contextService = new PersistentConversationContextService(
            dbContext,
            messageService,
            stateService,
            summaryService,
            Options.Create(options),
            NullLogger<PersistentConversationContextService>.Instance);

        var thread = await threadService.CreateThreadAsync(userId, "Long Chat", CancellationToken.None);
        for (var i = 0; i < 12; i++)
        {
            await messageService.AppendMessageAsync(
                userId,
                thread.Id,
                new ConversationMessageAppendRequest(
                    i % 2 == 0 ? ConversationMessageRole.User : ConversationMessageRole.Assistant,
                    $"This is a fairly long transaction analysis message number {i} with repeated context tokens and details."),
                CancellationToken.None);
        }

        var result = await contextService.BuildContextAsync(
            new PersistentConversationContextBuildRequest(
                UserId: userId,
                ConversationThreadId: thread.Id,
                TaskType: AITaskType.UserChatComplex,
                ModelClass: AIModelClass.HeavyReasoning,
                CorrelationId: "ctx-2",
                CurrentUserMessage: null,
                IncludeCurrentUserMessage: false,
                MaxPromptTokensOverride: 80),
            CancellationToken.None);

        Assert.Equal("token_budget_trim_applied", result.TrimReason);
        Assert.NotEmpty(result.ExcludedMessageIds);
        Assert.True(result.EstimatedPromptTokenCount <= 120);
    }

    [Fact]
    public async Task UserChatOrchestrator_UsesPersistentMemory_WhenEnabled()
    {
        var services = BuildServiceProviderWithDb(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true",
            ["AI:UseMockProvider"] = "true",
            ["AI:ProviderKind"] = "Mock",
            ["AI:Routing:HeavyModelEnabled"] = "true",
            ["AI:Mock:DefaultSimpleChatScenario"] = "UserChatSimple",
            ["AI:Memory:SummaryRefreshMessageThreshold"] = "2",
            ["AI:Memory:SummaryRefreshMessageDeltaThreshold"] = "1",
            ["AI:Memory:SummaryRefreshTokenEstimateThreshold"] = "120"
        });

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await SeedUserAsync(dbContext, "orchestrator-memory");
        var orchestrator = scope.ServiceProvider.GetRequiredService<IUserChatOrchestrator>();

        var firstResponse = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Help me reduce recurring costs",
                RecentTurns: [],
                State: new ChatStateSnapshot(
                    ActiveTopic: "subscriptions",
                    UserIntent: "cut waste",
                    Constraints: new Dictionary<string, string> { ["currency"] = "EUR" },
                    Summaries: [],
                    BudgetPreference: "balanced",
                    LocationPreference: "IE",
                    MerchantInvestigationSubject: null,
                    RecentConclusions: []),
                CorrelationId: "chat-persist-1",
                UsePersistentMemory: true,
                UserId: userId),
            CancellationToken.None);

        Assert.True(firstResponse.Succeeded);
        Assert.NotNull(firstResponse.ConversationThreadId);

        var secondResponse = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Now compare two plans and rank them",
                RecentTurns: [],
                State: null,
                CorrelationId: "chat-persist-2",
                UsePersistentMemory: true,
                UserId: userId,
                ConversationThreadId: firstResponse.ConversationThreadId),
            CancellationToken.None);

        Assert.True(secondResponse.Succeeded);
        Assert.Equal(firstResponse.ConversationThreadId, secondResponse.ConversationThreadId);

        var threadId = firstResponse.ConversationThreadId!.Value;
        Assert.True(await dbContext.ConversationMessages.CountAsync(x => x.ConversationThreadId == threadId) >= 4);
        Assert.True(await dbContext.ConversationStateSnapshots.CountAsync(x => x.ConversationThreadId == threadId) >= 1);
        Assert.True(await dbContext.ConversationContextBuildLogs.CountAsync(x => x.ConversationThreadId == threadId) >= 2);
    }

    private static AIIntegrationOptions CreateAiOptionsWithMemory()
    {
        return new AIIntegrationOptions
        {
            Memory = new ConversationMemoryOptions
            {
                BuildContextLogsEnabled = true,
                SummaryRefreshMessageThreshold = 2,
                SummaryRefreshMessageDeltaThreshold = 1,
                SummaryRefreshTokenEstimateThreshold = 100,
                MaxSummaryLengthChars = 800,
                MaxStateEntries = 12,
                MaxStateValueLength = 200,
                RecentMessageFetchMultiplier = 4,
                ComplexChat = new TaskContextBudgetOptions
                {
                    MaxRecentMessages = 8,
                    MaxPromptTokens = 1200,
                    MaxSummaryChars = 500,
                    MaxStateEntries = 10
                }
            }
        };
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext, string seed)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PrimaryEmail = $"{seed}@example.com",
            NormalizedEmail = $"{seed}@example.com".ToUpperInvariant(),
            DisplayName = seed,
            FullName = seed,
            Status = "active",
            OnboardingStatus = "completed",
            Role = "user",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            EmailVerified = true,
            IsDisabled = false,
            IsSuspended = false,
            DeletionRequested = false,
            Timezone = "Europe/Dublin",
            Locale = "en-IE",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"persistent-conversation-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static ServiceProvider BuildServiceProviderWithDb(IReadOnlyDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(builder =>
            builder.UseInMemoryDatabase($"persistent-chat-di-tests-{Guid.NewGuid():N}"));
        services.AddAIIntegration(configuration);

        return services.BuildServiceProvider();
    }
}
