using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.DTOs;
using NSFinance.Api.Modules.AI.Endpoints;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class AIApiEndpointTests
{
    [Fact]
    public async Task SendChatMessageEndpoint_ReturnsValidationProblem_WhenClientRequestIdMissing()
    {
        var result = await SendChatMessageEndpoint.HandleAsync(
            new SendChatMessageRequest(
                Message: "Hello",
                ClientRequestId: "",
                ConversationThreadId: null),
            new FixedCurrentUserProviderStub(Guid.NewGuid()),
            new StubUserChatOrchestrator(_ => throw new InvalidOperationException("Should not be called")),
            new StubConversationThreadService(_ => null),
            Options.Create(new AIIntegrationOptions()),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (statusCode, _) = await ExecuteResultAsync(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
    }

    [Fact]
    public async Task SendChatMessageEndpoint_ReturnsNotFound_WhenThreadDoesNotBelongToUser()
    {
        var userId = Guid.NewGuid();
        var result = await SendChatMessageEndpoint.HandleAsync(
            new SendChatMessageRequest(
                Message: "Hello",
                ClientRequestId: "req-1",
                ConversationThreadId: Guid.NewGuid()),
            new FixedCurrentUserProviderStub(userId),
            new StubUserChatOrchestrator(_ => throw new InvalidOperationException("Should not be called")),
            new StubConversationThreadService(_ => null),
            Options.Create(new AIIntegrationOptions()),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (statusCode, _) = await ExecuteResultAsync(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
    }

    [Fact]
    public async Task SendChatMessageEndpoint_ReturnsAccepted_WhenTurnIsInProgress()
    {
        var userId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        var result = await SendChatMessageEndpoint.HandleAsync(
            new SendChatMessageRequest(
                Message: "Need help with budgeting",
                ClientRequestId: "req-2",
                ConversationThreadId: threadId),
            new FixedCurrentUserProviderStub(userId),
            new StubUserChatOrchestrator(_ => new UserChatResponse(
                ReplyText: "Working on it.",
                ModelUsed: "gpt-5-chat",
                ReasoningClass: AIModelClass.HeavyReasoning,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(),
                ReferencedContextSummary: null,
                Warnings: [],
                FollowUpIntentHints: [],
                Succeeded: false,
                FailureReason: "duplicate_in_progress",
                ConversationThreadId: threadId,
                ConversationTurnId: turnId,
                TurnStatus: ConversationTurnStatus.AIInProgress,
                IsDuplicateRequest: true,
                IsTurnInProgress: true)),
            new StubConversationThreadService(id => id == threadId ? BuildThread(userId, threadId) : null),
            Options.Create(new AIIntegrationOptions()),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (statusCode, body) = await ExecuteResultAsync(result);
        Assert.Equal(StatusCodes.Status202Accepted, statusCode);
        Assert.Contains("in_progress", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendChatMessageEndpoint_ReturnsOk_ForSuccessfulChat()
    {
        var userId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        var result = await SendChatMessageEndpoint.HandleAsync(
            new SendChatMessageRequest(
                Message: "How can I lower food costs?",
                ClientRequestId: "req-3",
                ConversationThreadId: threadId),
            new FixedCurrentUserProviderStub(userId),
            new StubUserChatOrchestrator(_ => new UserChatResponse(
                ReplyText: "Start with weekly meal planning.",
                ModelUsed: "gpt-4.1",
                ReasoningClass: AIModelClass.Fast,
                SuggestedStructuredStateUpdates: new Dictionary<string, string> { ["active_topic"] = "food_budget" },
                ReferencedContextSummary: "User wants to reduce food costs.",
                Warnings: [],
                FollowUpIntentHints: ["ask_for_weekly_budget"],
                Succeeded: true,
                FailureReason: null,
                ConversationThreadId: threadId,
                ConversationTurnId: turnId,
                TurnStatus: ConversationTurnStatus.Completed,
                IsDuplicateRequest: false,
                IsTurnInProgress: false)),
            new StubConversationThreadService(id => id == threadId ? BuildThread(userId, threadId) : null),
            Options.Create(new AIIntegrationOptions()),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (statusCode, body) = await ExecuteResultAsync(result);
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Contains("meal planning", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestMerchantInvestigationEndpoint_ReturnsValidationProblem_WhenDryRunFalse()
    {
        var result = await TestMerchantInvestigationEndpoint.HandleAsync(
            new MerchantInvestigationTestRequest(
                RawDescriptor: "NETFLIX.COM",
                DryRun: false),
            new StubMerchantInvestigationOrchestrator(_ => throw new InvalidOperationException("Should not be called")),
            new StubMerchantAcceptancePolicy(_ => throw new InvalidOperationException("Should not be called")),
            new MerchantDescriptorNormalizer(),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (statusCode, _) = await ExecuteResultAsync(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
    }

    [Fact]
    public async Task TestMerchantInvestigationEndpoint_ReturnsStructuredResponse_InDryRunMode()
    {
        var investigationResult = new MerchantInvestigationResult(
            Succeeded: true,
            InsufficientEvidence: false,
            Candidates:
            [
                new MerchantInvestigationCandidate(
                    ExistingMerchantId: null,
                    CanonicalName: "Netflix",
                    DisplayName: "Netflix",
                    MerchantType: MerchantType.Merchant,
                    MerchantUsageType: MerchantUsageType.NarrowUse,
                    PrimaryCountryCode: "US",
                    Confidence: 0.93,
                    AmbiguityScore: 0.08,
                    MixedUseRisk: false,
                    HasContradictions: false,
                    OfficialWebsite: "https://www.netflix.com",
                    DescriptionSummary: "Streaming service",
                    AliasCandidates: ["NETFLIX.COM"],
                    WhyItMayMatch: "Descriptor and billing signature align",
                    WhyItMayBeWrong: "Low mismatch risk",
                    DescriptorMatchStrength: 0.95,
                    EntityMatchStrength: 0.92)
            ],
            Evidence: [],
            FailureReason: null,
            Recommendation: MerchantInvestigationRecommendation.AcceptCandidate,
            OverallConfidence: 0.93,
            AmbiguityLevel: 0.08,
            AliasSuggestions:
            [
                new MerchantInvestigationAliasSuggestion("NETFLIX.COM", "BillingDescriptor", 0.91, null, true)
            ],
            InvestigationReasonCodes: ["candidate_dominant"],
            ParserRejected: false);

        var result = await TestMerchantInvestigationEndpoint.HandleAsync(
            new MerchantInvestigationTestRequest(
                RawDescriptor: "NETFLIX.COM",
                DryRun: true),
            new StubMerchantInvestigationOrchestrator(_ => investigationResult),
            new StubMerchantAcceptancePolicy(_ => new MerchantAcceptanceDecision(
                DecisionType: MerchantAcceptanceDecisionType.AcceptedTrusted,
                Confidence: 0.9,
                SelectedCandidate: investigationResult.Candidates[0],
                ReasonCodes: ["dominant_candidate"])),
            new MerchantDescriptorNormalizer(),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var (statusCode, body) = await ExecuteResultAsync(result);
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Contains("AcceptedTrusted", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Netflix", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModuleRegistrationExtensions_ContainsAIModuleMapping()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NSFinance.Api", "Modules", "ModuleRegistrationExtensions.cs"));
        Assert.True(File.Exists(path), $"Expected module registration source at {path}");

        var source = File.ReadAllText(path);
        Assert.Contains("app.MapAIModule();", source, StringComparison.Ordinal);
    }

    private static ConversationThread BuildThread(Guid userId, Guid threadId)
    {
        return new ConversationThread
        {
            Id = threadId,
            UserId = userId,
            Title = "Thread",
            Status = ConversationThreadStatus.Active,
            StartedUtc = DateTime.UtcNow.AddHours(-1),
            LastMessageUtc = DateTime.UtcNow,
            ActiveSummaryVersion = 0,
            CreatedUtc = DateTime.UtcNow.AddHours(-1),
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(_ => { });
        context.RequestServices = services.BuildServiceProvider();
        await using var stream = new MemoryStream();
        context.Response.Body = stream;
        await result.ExecuteAsync(context);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private sealed class FixedCurrentUserProviderStub(Guid userId) : ICurrentUserProvider
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

    private sealed class StubUserChatOrchestrator(Func<UserChatRequest, UserChatResponse> factory) : IUserChatOrchestrator
    {
        public Task<UserChatResponse> ExecuteAsync(UserChatRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(factory(request));
        }
    }

    private sealed class StubConversationThreadService(Func<Guid, ConversationThread?> resolver) : IConversationThreadService
    {
        public Task<ConversationThread> CreateThreadAsync(Guid userId, string? title, CancellationToken cancellationToken)
            => Task.FromResult(BuildThread(userId, Guid.NewGuid()));

        public Task<ConversationThread?> GetThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
            => Task.FromResult(resolver(threadId));

        public Task<IReadOnlyList<ConversationThread>> GetRecentThreadsAsync(Guid userId, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ConversationThread>>([]);

        public Task ArchiveThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task CloseThreadAsync(Guid userId, Guid threadId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task TouchThreadAsync(Guid userId, Guid threadId, DateTime timestampUtc, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubMerchantInvestigationOrchestrator(Func<MerchantInvestigationRequest, MerchantInvestigationResult> factory)
        : IMerchantInvestigationOrchestrator
    {
        public Task<MerchantInvestigationResult> InvestigateAsync(MerchantInvestigationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(factory(request));
        }
    }

    private sealed class StubMerchantAcceptancePolicy(Func<MerchantInvestigationResult, MerchantAcceptanceDecision> evaluate)
        : IMerchantAcceptancePolicy
    {
        public MerchantAcceptanceDecision Evaluate(MerchantInvestigationResult result) => evaluate(result);
    }
}
