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
                ConversationTurnId: null,
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
                ConversationTurnId: null,
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

    [Fact]
    public async Task UserChatOrchestrator_DedupesCompletedTurn_ByClientRequestId()
    {
        var services = BuildServiceProviderWithDb(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true",
            ["AI:UseMockProvider"] = "true",
            ["AI:ProviderKind"] = "Mock",
            ["AI:Routing:HeavyModelEnabled"] = "true",
            ["AI:Mock:DefaultSimpleChatScenario"] = "UserChatSimple"
        });

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await SeedUserAsync(dbContext, "chat-dedupe-completed");
        var orchestrator = scope.ServiceProvider.GetRequiredService<IUserChatOrchestrator>();

        var first = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Help me optimize my budget.",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-dedupe-1",
                ClientRequestId: "req-dedupe-1",
                UserId: userId,
                UsePersistentMemory: true),
            CancellationToken.None);

        var second = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "Help me optimize my budget.",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-dedupe-2",
                ClientRequestId: "req-dedupe-1",
                UserId: userId,
                ConversationThreadId: first.ConversationThreadId,
                UsePersistentMemory: true),
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.True(second.IsDuplicateRequest);
        Assert.False(second.IsTurnInProgress);
        Assert.Equal(first.ConversationThreadId, second.ConversationThreadId);
        Assert.Equal(first.ConversationTurnId, second.ConversationTurnId);

        var threadId = first.ConversationThreadId!.Value;
        var turnId = first.ConversationTurnId!.Value;
        var turns = await dbContext.ConversationTurns.Where(x => x.ConversationThreadId == threadId).ToListAsync();
        var messages = await dbContext.ConversationMessages.Where(x => x.ConversationThreadId == threadId).ToListAsync();

        Assert.Single(turns);
        Assert.Equal(ConversationTurnStatus.Completed, turns[0].Status);
        Assert.True(turns[0].WasDeduplicated);
        Assert.Equal(2, turns[0].AttemptCount);
        Assert.Equal(turnId, turns[0].Id);
        Assert.Equal(2, messages.Count); // 1 user + 1 assistant only once
    }

    [Fact]
    public async Task UserChatOrchestrator_DuplicateWhileInProgress_ReturnsTurnInProgress()
    {
        var services = BuildServiceProviderWithDb(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true",
            ["AI:UseMockProvider"] = "true",
            ["AI:ProviderKind"] = "Mock",
            ["AI:Mock:DefaultSimpleChatScenario"] = "UserChatSimple"
        });

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await SeedUserAsync(dbContext, "chat-dedupe-inprogress");

        var threadService = scope.ServiceProvider.GetRequiredService<IConversationThreadService>();
        var turnService = scope.ServiceProvider.GetRequiredService<IConversationTurnService>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IUserChatOrchestrator>();

        var thread = await threadService.CreateThreadAsync(userId, "In progress thread", CancellationToken.None);
        var started = await turnService.StartOrGetAsync(
            userId,
            thread.Id,
            "req-in-progress",
            "corr-turn-start",
            AITaskType.UserChatSimple,
            AIModelClass.Fast,
            CancellationToken.None);

        Assert.False(started.IsDuplicateRequest);
        Assert.Equal(ConversationTurnStatus.Received, started.Turn.Status);

        var duplicateResponse = await orchestrator.ExecuteAsync(
            new UserChatRequest(
                UserMessage: "This should dedupe while in progress.",
                RecentTurns: [],
                State: null,
                CorrelationId: "corr-dup-in-progress",
                ClientRequestId: "req-in-progress",
                UserId: userId,
                ConversationThreadId: thread.Id,
                UsePersistentMemory: true),
            CancellationToken.None);

        Assert.False(duplicateResponse.Succeeded);
        Assert.True(duplicateResponse.IsDuplicateRequest);
        Assert.True(duplicateResponse.IsTurnInProgress);
        Assert.Equal("turn_in_progress", duplicateResponse.FailureReason);
        Assert.Equal(thread.Id, duplicateResponse.ConversationThreadId);
        Assert.Equal(started.Turn.Id, duplicateResponse.ConversationTurnId);

        Assert.Empty(await dbContext.ConversationMessages.Where(x => x.ConversationThreadId == thread.Id).ToListAsync());
    }

    [Fact]
    public async Task PersistentConversationContextService_ExcludesFailedAssistantMessagesFromContext()
    {
        await using var dbContext = CreateDbContext();
        var userId = await SeedUserAsync(dbContext, "context-failed-turn-filter");
        var options = CreateAiOptionsWithMemory();

        var threadService = new ConversationThreadService(dbContext, NullLogger<ConversationThreadService>.Instance);
        var turnService = new ConversationTurnService(dbContext, NullLogger<ConversationTurnService>.Instance);
        var messageService = new ConversationMessageService(dbContext);
        var stateService = new ConversationStateService(dbContext, Options.Create(options));
        var summaryService = new ConversationSummaryService(
            dbContext,
            Options.Create(options),
            new DeterministicConversationSummaryGenerator());
        var contextService = new PersistentConversationContextService(
            dbContext,
            stateService,
            summaryService,
            Options.Create(options),
            NullLogger<PersistentConversationContextService>.Instance);

        var thread = await threadService.CreateThreadAsync(userId, "Failed turn filtering", CancellationToken.None);

        var failedTurn = await turnService.StartOrGetAsync(
            userId,
            thread.Id,
            "req-failed-turn",
            "corr-failed-turn",
            AITaskType.UserChatSimple,
            AIModelClass.Fast,
            CancellationToken.None);
        await turnService.MarkPersistedUserTurnAsync(
            userId,
            thread.Id,
            failedTurn.Turn.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        var failedAssistant = await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(
                ConversationMessageRole.Assistant,
                "This assistant message belongs to a failed turn.",
                ConversationTurnId: failedTurn.Turn.Id),
            CancellationToken.None);
        await turnService.MarkFailedAsync(
            userId,
            thread.Id,
            failedTurn.Turn.Id,
            "forced_failure_for_test",
            "forced failure for filtering",
            CancellationToken.None);

        var healthyTurn = await turnService.StartOrGetAsync(
            userId,
            thread.Id,
            "req-healthy-turn",
            "corr-healthy-turn",
            AITaskType.UserChatSimple,
            AIModelClass.Fast,
            CancellationToken.None);
        var userMessage = await messageService.AppendMessageAsync(
            userId,
            thread.Id,
            new ConversationMessageAppendRequest(
                ConversationMessageRole.User,
                "What should I do next for my budget?",
                ConversationTurnId: healthyTurn.Turn.Id),
            CancellationToken.None);
        await turnService.MarkPersistedUserTurnAsync(
            userId,
            thread.Id,
            healthyTurn.Turn.Id,
            userMessage.Id,
            CancellationToken.None);

        var result = await contextService.BuildContextAsync(
            new PersistentConversationContextBuildRequest(
                UserId: userId,
                ConversationThreadId: thread.Id,
                TaskType: AITaskType.UserChatSimple,
                ModelClass: AIModelClass.Fast,
                CorrelationId: "ctx-failed-filter",
                ConversationTurnId: healthyTurn.Turn.Id,
                CurrentUserMessage: null,
                IncludeCurrentUserMessage: false),
            CancellationToken.None);

        Assert.DoesNotContain(failedAssistant.Id, result.IncludedMessageIds);
        Assert.Contains(failedAssistant.Id, result.ExcludedMessageIds);
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
