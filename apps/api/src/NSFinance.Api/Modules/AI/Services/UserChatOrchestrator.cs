using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class UserChatOrchestrator(
    IUserChatComplexityClassifier complexityClassifier,
    IConversationContextService contextService,
    IAIModelRouter modelRouter,
    IPromptBuilder promptBuilder,
    IUserChatResponseParser responseParser,
    IAIClient aiClient,
    ILogger<UserChatOrchestrator> logger,
    IConversationThreadService? conversationThreadService = null,
    IConversationMessageService? conversationMessageService = null,
    IConversationStateService? conversationStateService = null,
    IConversationSummaryService? conversationSummaryService = null,
    IPersistentConversationContextService? persistentConversationContextService = null) : IUserChatOrchestrator
{
    public async Task<UserChatResponse> ExecuteAsync(UserChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var complexity = complexityClassifier.Evaluate(request);

        var taskType = complexity.Complexity == UserChatComplexity.Complex
            ? AITaskType.UserChatComplex
            : AITaskType.UserChatSimple;

        var preferredModelClass = complexity.Complexity == UserChatComplexity.Complex
            ? AIModelClass.HeavyReasoning
            : AIModelClass.Fast;
        var route = modelRouter.Resolve(
            taskType,
            preferredModelClass,
            complexityHint: string.Join(',', complexity.ReasonCodes));

        var warnings = new List<string>();
        var conversationThreadId = request.ConversationThreadId;

        IReadOnlyList<AIMessage> contextMessages;
        string? contextSummary;
        IReadOnlyDictionary<string, string> structuredState;
        IReadOnlyList<string> contextReasonCodes;

        if (CanUsePersistentMemory(request))
        {
            try
            {
                conversationThreadId = await EnsureConversationThreadAsync(request, cancellationToken);
                await PersistUserTurnContextAsync(request, taskType, conversationThreadId!.Value, cancellationToken);

                var persistentContext = await persistentConversationContextService!.BuildContextAsync(
                    new PersistentConversationContextBuildRequest(
                        UserId: request.UserId!.Value,
                        ConversationThreadId: conversationThreadId.Value,
                        TaskType: taskType,
                        ModelClass: route.ModelClass,
                        CorrelationId: request.CorrelationId,
                        CurrentUserMessage: request.UserMessage,
                        IncludeCurrentUserMessage: false),
                    cancellationToken);

                contextMessages = persistentContext.ContextMessages;
                contextSummary = persistentContext.ContextSummary;
                structuredState = persistentContext.StructuredState;
                contextReasonCodes = persistentContext.ReasonCodes;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Persistent chat memory unavailable, falling back to transient context correlationId={CorrelationId}",
                    request.CorrelationId);
                warnings.Add("persistent_memory_fallback_to_transient");
                (contextMessages, contextSummary, structuredState, contextReasonCodes) = BuildTransientContext(request, taskType);
            }
        }
        else
        {
            (contextMessages, contextSummary, structuredState, contextReasonCodes) = BuildTransientContext(request, taskType);
        }

        var prompt = promptBuilder.BuildUserChatPrompt(
            new UserChatPromptInput(
                ChatRequest: request,
                ContextMessages: contextMessages,
                ContextSummary: contextSummary,
                StructuredState: structuredState,
                ComplexityEvaluation: complexity));

        if (route.Reason == "heavy_model_disabled_fail_fast")
        {
            return new UserChatResponse(
                ReplyText: "I can't process that complex request right now because the heavy model is unavailable.",
                ModelUsed: route.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: contextSummary,
                Warnings: warnings.Concat(["heavy_model_unavailable"]).ToArray(),
                FollowUpIntentHints: ["retry_shorter_question"],
                Succeeded: false,
                FailureReason: "heavy_model_unavailable",
                ConversationThreadId: conversationThreadId);
        }

        var aiRequest = AIRequest.Create(
            taskType: taskType,
            preferredModelClass: route.ModelClass,
            messages: prompt.Messages,
            correlationId: request.CorrelationId,
            systemInstructions: prompt.SystemInstructions,
            structuredOutputSchemaName: prompt.StructuredSchemaName,
            temperature: complexity.Complexity == UserChatComplexity.Complex ? 0.2d : 0.1d,
            maxOutputTokens: complexity.Complexity == UserChatComplexity.Complex ? 1200 : 600,
            metadata: request.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var response = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        responseParser.TryParse(response, route, out var parsedResponse, out var reasonCodes);

        warnings.AddRange(parsedResponse.Warnings);

        if (CanUsePersistentMemory(request) && request.UserId.HasValue && conversationThreadId.HasValue)
        {
            await PersistAssistantTurnContextAsync(
                request.UserId.Value,
                conversationThreadId.Value,
                taskType,
                route,
                parsedResponse,
                cancellationToken);
        }

        logger.LogInformation(
            "User chat orchestrated correlationId={CorrelationId} complexity={Complexity} task={TaskType} model={Model} deployment={Deployment} succeeded={Succeeded} contextReasonCodes={ContextReasonCodes} reasonCodes={ReasonCodes}",
            request.CorrelationId,
            complexity.Complexity,
            taskType,
            route.Model,
            route.Deployment,
            parsedResponse.Succeeded,
            string.Join(',', contextReasonCodes),
            string.Join(',', reasonCodes));

        return parsedResponse with
        {
            ReferencedContextSummary = contextSummary,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ConversationThreadId = conversationThreadId
        };
    }

    private bool CanUsePersistentMemory(UserChatRequest request)
    {
        return request.UsePersistentMemory
               && request.UserId.HasValue
               && conversationThreadService is not null
               && conversationMessageService is not null
               && conversationStateService is not null
               && conversationSummaryService is not null
               && persistentConversationContextService is not null;
    }

    private async Task<Guid> EnsureConversationThreadAsync(UserChatRequest request, CancellationToken cancellationToken)
    {
        if (request.ConversationThreadId.HasValue)
        {
            var existing = await conversationThreadService!.GetThreadAsync(
                request.UserId!.Value,
                request.ConversationThreadId.Value,
                cancellationToken);
            if (existing is not null)
            {
                return existing.Id;
            }
        }

        var created = await conversationThreadService!.CreateThreadAsync(
            request.UserId!.Value,
            title: null,
            cancellationToken);
        return created.Id;
    }

    private async Task PersistUserTurnContextAsync(
        UserChatRequest request,
        AITaskType taskType,
        Guid conversationThreadId,
        CancellationToken cancellationToken)
    {
        if (request.State is not null)
        {
            await conversationStateService!.SaveSnapshotAsync(
                request.UserId!.Value,
                conversationThreadId,
                request.State,
                ConversationStateSnapshotReason.UserTurn,
                cancellationToken);
        }

        await conversationMessageService!.AppendMessageAsync(
            request.UserId!.Value,
            conversationThreadId,
            new ConversationMessageAppendRequest(
                Role: ConversationMessageRole.User,
                Content: request.UserMessage,
                Topic: request.State?.ActiveTopic,
                IsResolved: false,
                WasTrimEligible: true,
                WasSummaryDerived: false,
                ModelUsed: null,
                TaskType: taskType.ToString(),
                CorrelationId: request.CorrelationId),
            cancellationToken);
    }

    private async Task PersistAssistantTurnContextAsync(
        Guid userId,
        Guid conversationThreadId,
        AITaskType taskType,
        AIModelRoute route,
        UserChatResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Succeeded && !string.IsNullOrWhiteSpace(response.ReplyText))
        {
            await conversationMessageService!.AppendMessageAsync(
                userId,
                conversationThreadId,
                new ConversationMessageAppendRequest(
                    Role: ConversationMessageRole.Assistant,
                    Content: response.ReplyText,
                    Topic: null,
                    IsResolved: false,
                    WasTrimEligible: true,
                    WasSummaryDerived: false,
                    ModelUsed: route.Model,
                    TaskType: taskType.ToString(),
                    CorrelationId: null),
                cancellationToken);
        }

        if (response.SuggestedStructuredStateUpdates.Count > 0)
        {
            await conversationStateService!.MergeStateUpdatesAsync(
                userId,
                conversationThreadId,
                response.SuggestedStructuredStateUpdates,
                ConversationStateSnapshotReason.AssistantTurn,
                cancellationToken);
        }

        await conversationSummaryService!.RefreshSummaryIfNeededAsync(
            userId,
            conversationThreadId,
            taskType,
            correlationId: $"assistant-{DateTime.UtcNow:yyyyMMddHHmmss}",
            cancellationToken);
    }

    private (IReadOnlyList<AIMessage> ContextMessages, string? ContextSummary, IReadOnlyDictionary<string, string> StructuredState, IReadOnlyList<string> ReasonCodes)
        BuildTransientContext(UserChatRequest request, AITaskType taskType)
    {
        var context = contextService.BuildContext(
            new ConversationContextBuildRequest(
                TaskType: taskType,
                RecentTurns: request.RecentTurns,
                State: request.State,
                CurrentUserMessage: request.UserMessage,
                CorrelationId: request.CorrelationId));

        var messages = new List<AIMessage>(context.IncludedTurns.Count + 1);
        messages.AddRange(context.IncludedTurns.Select(turn => new AIMessage(turn.Role, turn.Content, turn.TimestampUtc)));
        if (!string.IsNullOrWhiteSpace(request.UserMessage))
        {
            messages.Add(AIMessage.User(request.UserMessage.Trim()));
        }

        return (messages, context.ContextSummary, context.StructuredState, context.ReasonCodes);
    }
}
