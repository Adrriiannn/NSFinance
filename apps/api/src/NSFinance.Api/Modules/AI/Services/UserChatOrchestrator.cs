using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence.Entities;
using System.Diagnostics;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class UserChatOrchestrator(
    IUserChatComplexityClassifier complexityClassifier,
    IConversationContextService contextService,
    IAIModelRouter modelRouter,
    IPromptBuilder promptBuilder,
    IUserChatResponseParser responseParser,
    IAIClient aiClient,
    ILogger<UserChatOrchestrator> logger,
    IOptions<AIIntegrationOptions> options,
    IOperationalFailureRecorder? failureRecorder = null,
    IConversationThreadService? conversationThreadService = null,
    IConversationTurnService? conversationTurnService = null,
    IConversationMessageService? conversationMessageService = null,
    IConversationStateService? conversationStateService = null,
    IConversationSummaryService? conversationSummaryService = null,
    IPersistentConversationContextService? persistentConversationContextService = null) : IUserChatOrchestrator
{
    public async Task<UserChatResponse> ExecuteAsync(UserChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateUserMessage(request.UserMessage, options.Value.ChatTurns.MaxUserMessageChars);

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
        Guid? conversationTurnId = null;

        IReadOnlyList<AIMessage> contextMessages;
        string? contextSummary;
        IReadOnlyDictionary<string, string> structuredState;
        IReadOnlyList<string> contextReasonCodes;

        if (CanUsePersistentMemory(request))
        {
            conversationThreadId = await EnsureConversationThreadAsync(request, cancellationToken);
            var clientRequestId = ResolveClientRequestId(request, options.Value.ChatTurns.MaxClientRequestIdLength);
                var turnStart = await conversationTurnService!.StartOrGetAsync(
                request.UserId!.Value,
                conversationThreadId.Value,
                clientRequestId,
                request.CorrelationId,
                taskType,
                route.ModelClass,
                cancellationToken);

            conversationTurnId = turnStart.Turn.Id;
            if (turnStart.IsDuplicateRequest)
            {
                if (failureRecorder is not null)
                {
                    await failureRecorder.RecordAsync(
                        new OperationalFailureRecordInput(
                            OperationalFailureArea.UserChat,
                            OperationalFailureSeverity.Info,
                            "chat_duplicate_request_deduped",
                            $"chat_duplicate_request_deduped:{turnStart.Turn.ConversationThreadId:N}:{turnStart.Turn.ClientRequestId}",
                            request.CorrelationId,
                            turnStart.Turn.ClientRequestId,
                            $"Duplicate request deduped with status {turnStart.Turn.Status}.",
                            null),
                        cancellationToken);
                }

                return await BuildDuplicateTurnResponseAsync(
                    request,
                    conversationThreadId.Value,
                    turnStart.Turn,
                    warnings,
                    cancellationToken);
            }

            ConversationTurn activeTurn = turnStart.Turn;
            try
            {
                var userMessage = await PersistUserTurnContextAsync(
                    request,
                    taskType,
                    conversationThreadId.Value,
                    activeTurn.Id,
                    cancellationToken);

                activeTurn = (await conversationTurnService.MarkPersistedUserTurnAsync(
                    request.UserId.Value,
                    conversationThreadId.Value,
                    activeTurn.Id,
                    userMessage.Id,
                    cancellationToken)).Turn;

                var persistentContextResult = await TryBuildPersistentContextAsync(
                    request,
                    taskType,
                    route,
                    conversationThreadId.Value,
                    activeTurn.Id,
                    warnings,
                    cancellationToken);

                if (!persistentContextResult.Succeeded)
                {
                    return persistentContextResult.Response!;
                }

                contextMessages = persistentContextResult.ContextMessages!;
                contextSummary = persistentContextResult.ContextSummary;
                structuredState = persistentContextResult.StructuredState!;
                contextReasonCodes = persistentContextResult.ContextReasonCodes!;

                activeTurn = (await conversationTurnService.MarkContextBuiltAsync(
                    request.UserId.Value,
                    conversationThreadId.Value,
                    activeTurn.Id,
                    persistentContextResult.ContextSource!,
                    persistentContextResult.EstimatedTokens,
                    cancellationToken)).Turn;
            }
            catch (OperationCanceledException ex)
            {
                if (failureRecorder is not null)
                {
                    await failureRecorder.RecordAsync(
                        new OperationalFailureRecordInput(
                            OperationalFailureArea.UserChat,
                            OperationalFailureSeverity.Warning,
                            "chat_turn_cancelled_during_setup",
                            $"chat_turn_cancelled_during_setup:{conversationThreadId.Value:N}",
                            request.CorrelationId,
                            conversationTurnId?.ToString("N"),
                            ex.Message,
                            null),
                        CancellationToken.None);
                }

                await conversationTurnService.MarkCancelledAsync(
                    request.UserId!.Value,
                    conversationThreadId.Value,
                    activeTurn.Id,
                    "request_cancelled",
                    ex.Message,
                    CancellationToken.None);

                return BuildFailedTurnResponse(
                    "Chat request was cancelled before completion.",
                    route,
                    warnings.Append("request_cancelled").ToArray(),
                    "request_cancelled",
                    conversationThreadId,
                    activeTurn.Id,
                    ConversationTurnStatus.Cancelled,
                    isDuplicateRequest: false,
                    isTurnInProgress: false);
            }
            catch (Exception ex)
            {
                if (failureRecorder is not null)
                {
                    await failureRecorder.RecordAsync(
                        new OperationalFailureRecordInput(
                            OperationalFailureArea.UserChat,
                            OperationalFailureSeverity.Error,
                            "chat_turn_setup_failed",
                            $"chat_turn_setup_failed:{conversationThreadId.Value:N}",
                            request.CorrelationId,
                            conversationTurnId?.ToString("N"),
                            ex.Message,
                            null),
                        CancellationToken.None);
                }

                await conversationTurnService.MarkFailedAsync(
                    request.UserId!.Value,
                    conversationThreadId.Value,
                    activeTurn.Id,
                    "turn_setup_failed",
                    ex.Message,
                    CancellationToken.None);

                logger.LogError(
                    ex,
                    "Chat turn setup failed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId}",
                    request.CorrelationId,
                    conversationThreadId,
                    activeTurn.Id);

                return BuildFailedTurnResponse(
                    "I couldn't prepare the conversation context safely.",
                    route,
                    warnings.Append("turn_setup_failed").ToArray(),
                    "turn_setup_failed",
                    conversationThreadId,
                    activeTurn.Id,
                    ConversationTurnStatus.Failed,
                    isDuplicateRequest: false,
                    isTurnInProgress: false);
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
            if (failureRecorder is not null)
            {
                await failureRecorder.RecordAsync(
                    new OperationalFailureRecordInput(
                        OperationalFailureArea.UserChat,
                        OperationalFailureSeverity.Warning,
                        "chat_heavy_model_unavailable",
                        "chat_heavy_model_unavailable",
                        request.CorrelationId,
                        conversationTurnId?.ToString("N"),
                        "Heavy model unavailable for complex chat request.",
                        null),
                    cancellationToken);
            }

            if (CanUsePersistentMemory(request) && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
            {
                await conversationTurnService!.MarkFailedAsync(
                    request.UserId.Value,
                    conversationThreadId.Value,
                    conversationTurnId.Value,
                    "heavy_model_unavailable",
                    "Heavy model unavailable and fail-fast policy configured.",
                    cancellationToken);
            }

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
                ConversationThreadId: conversationThreadId,
                ConversationTurnId: conversationTurnId,
                TurnStatus: CanUsePersistentMemory(request) ? ConversationTurnStatus.Failed : null);
        }

        if (CanUsePersistentMemory(request) && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
        {
            await conversationTurnService!.MarkAIInProgressAsync(
                request.UserId.Value,
                conversationThreadId.Value,
                conversationTurnId.Value,
                route,
                cancellationToken);
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

        AIResponse response;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            response = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            if (failureRecorder is not null)
            {
                await failureRecorder.RecordAsync(
                    new OperationalFailureRecordInput(
                        OperationalFailureArea.UserChat,
                        OperationalFailureSeverity.Warning,
                        "chat_ai_call_cancelled",
                        $"chat_ai_call_cancelled:{route.ModelClass}:{route.Deployment}",
                        request.CorrelationId,
                        conversationTurnId?.ToString("N"),
                        ex.Message,
                        null),
                    CancellationToken.None);
            }

            if (CanUsePersistentMemory(request) && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
            {
                await conversationTurnService!.MarkCancelledAsync(
                    request.UserId.Value,
                    conversationThreadId.Value,
                    conversationTurnId.Value,
                    "request_cancelled",
                    ex.Message,
                    CancellationToken.None);
            }

            return BuildFailedTurnResponse(
                "Chat request was cancelled before completion.",
                route,
                warnings.Append("request_cancelled").ToArray(),
                "request_cancelled",
                conversationThreadId,
                conversationTurnId,
                ConversationTurnStatus.Cancelled,
                isDuplicateRequest: false,
                isTurnInProgress: false,
                contextSummary: contextSummary);
        }
        finally
        {
            stopwatch.Stop();
        }

        responseParser.TryParse(response, route, out var parsedResponse, out var reasonCodes);
        warnings.AddRange(parsedResponse.Warnings);

        if (CanUsePersistentMemory(request) && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
        {
            if (!response.Succeeded || !parsedResponse.Succeeded)
            {
                var failureCode = ResolveFailureCode(response, parsedResponse);
                var failureReason = parsedResponse.FailureReason ?? response.FailureReason ?? "ai_response_failed";
                var isTimeout = IsTimeoutFailure(failureReason);
                if (failureRecorder is not null)
                {
                    await failureRecorder.RecordAsync(
                        new OperationalFailureRecordInput(
                            OperationalFailureArea.UserChat,
                            isTimeout ? OperationalFailureSeverity.Warning : OperationalFailureSeverity.Error,
                            isTimeout ? "chat_ai_timeout" : "chat_ai_response_failed",
                            $"{(isTimeout ? "chat_ai_timeout" : "chat_ai_response_failed")}:{route.ModelClass}:{route.Deployment}",
                            request.CorrelationId,
                            conversationTurnId?.ToString("N"),
                            failureReason,
                            null),
                        cancellationToken);
                }

                if (isTimeout)
                {
                    await conversationTurnService!.MarkTimedOutAsync(
                        request.UserId.Value,
                        conversationThreadId.Value,
                        conversationTurnId.Value,
                        failureCode,
                        failureReason,
                        cancellationToken);
                }
                else
                {
                    await conversationTurnService!.MarkFailedAsync(
                        request.UserId.Value,
                        conversationThreadId.Value,
                        conversationTurnId.Value,
                        failureCode,
                        failureReason,
                        cancellationToken);
                }

                return parsedResponse with
                {
                    ReferencedContextSummary = contextSummary,
                    Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    ConversationThreadId = conversationThreadId,
                    ConversationTurnId = conversationTurnId,
                    TurnStatus = isTimeout ? ConversationTurnStatus.TimedOut : ConversationTurnStatus.Failed
                };
            }

            await conversationTurnService!.MarkAICompletedAsync(
                request.UserId.Value,
                conversationThreadId.Value,
                conversationTurnId.Value,
                stopwatch.ElapsedMilliseconds,
                cancellationToken);

            try
            {
                await PersistAssistantTurnContextAsync(
                    request.UserId.Value,
                    conversationThreadId.Value,
                    taskType,
                    route,
                    conversationTurnId.Value,
                    parsedResponse,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                if (failureRecorder is not null)
                {
                    await failureRecorder.RecordAsync(
                        new OperationalFailureRecordInput(
                            OperationalFailureArea.UserChat,
                            OperationalFailureSeverity.Error,
                            "chat_assistant_persistence_failed",
                            "chat_assistant_persistence_failed",
                            request.CorrelationId,
                            conversationTurnId?.ToString("N"),
                            ex.Message,
                            null),
                        CancellationToken.None);
                }

                await conversationTurnService.MarkFailedAsync(
                    request.UserId.Value,
                    conversationThreadId.Value,
                    conversationTurnId.Value,
                    "assistant_persistence_failed",
                    ex.Message,
                    CancellationToken.None);

                logger.LogError(
                    ex,
                    "Assistant turn persistence failed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId}",
                    request.CorrelationId,
                    conversationThreadId,
                    conversationTurnId);

                return BuildFailedTurnResponse(
                    "I generated a response but couldn't persist it safely. Please retry.",
                    route,
                    warnings.Append("assistant_persistence_failed").ToArray(),
                    "assistant_persistence_failed",
                    conversationThreadId,
                    conversationTurnId,
                    ConversationTurnStatus.Failed,
                    isDuplicateRequest: false,
                    isTurnInProgress: false,
                    contextSummary: contextSummary);
            }

            await conversationTurnService.MarkCompletedAsync(
                request.UserId.Value,
                conversationThreadId.Value,
                conversationTurnId.Value,
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
            ConversationThreadId = conversationThreadId,
            ConversationTurnId = conversationTurnId,
            TurnStatus = CanUsePersistentMemory(request) ? ConversationTurnStatus.Completed : null
        };
    }

    private bool CanUsePersistentMemory(UserChatRequest request)
    {
        return request.UsePersistentMemory
               && request.UserId.HasValue
               && conversationThreadService is not null
               && conversationTurnService is not null
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

    private async Task<ConversationMessage> PersistUserTurnContextAsync(
        UserChatRequest request,
        AITaskType taskType,
        Guid conversationThreadId,
        Guid conversationTurnId,
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

        return await conversationMessageService!.AppendMessageAsync(
            request.UserId!.Value,
            conversationThreadId,
            new ConversationMessageAppendRequest(
                Role: ConversationMessageRole.User,
                Content: request.UserMessage,
                ConversationTurnId: conversationTurnId,
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
        Guid conversationTurnId,
        UserChatResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Succeeded && !string.IsNullOrWhiteSpace(response.ReplyText))
        {
            var assistantMessage = await conversationMessageService!.AppendMessageAsync(
                userId,
                conversationThreadId,
                new ConversationMessageAppendRequest(
                    Role: ConversationMessageRole.Assistant,
                    Content: response.ReplyText,
                    ConversationTurnId: conversationTurnId,
                    Topic: null,
                    IsResolved: false,
                    WasTrimEligible: true,
                    WasSummaryDerived: false,
                    ModelUsed: route.Model,
                    TaskType: taskType.ToString(),
                    CorrelationId: null),
                cancellationToken);

            await conversationTurnService!.MarkPersistedAssistantTurnAsync(
                userId,
                conversationThreadId,
                conversationTurnId,
                assistantMessage.Id,
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

        try
        {
            await conversationSummaryService!.RefreshSummaryIfNeededAsync(
                userId,
                conversationThreadId,
                taskType,
                correlationId: $"assistant-{DateTime.UtcNow:yyyyMMddHHmmss}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Conversation summary refresh failed after assistant turn threadId={ThreadId}",
                conversationThreadId);
        }
    }

    private async Task<ContextBuildResult> TryBuildPersistentContextAsync(
        UserChatRequest request,
        AITaskType taskType,
        AIModelRoute route,
        Guid conversationThreadId,
        Guid conversationTurnId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var persistentContext = await persistentConversationContextService!.BuildContextAsync(
                new PersistentConversationContextBuildRequest(
                    UserId: request.UserId!.Value,
                    ConversationThreadId: conversationThreadId,
                    TaskType: taskType,
                    ModelClass: route.ModelClass,
                    CorrelationId: request.CorrelationId,
                    ConversationTurnId: conversationTurnId,
                    CurrentUserMessage: request.UserMessage,
                    IncludeCurrentUserMessage: false),
                cancellationToken);

            return ContextBuildResult.Success(
                persistentContext.ContextMessages,
                persistentContext.ContextSummary,
                persistentContext.StructuredState,
                persistentContext.ReasonCodes,
                "persistent",
                persistentContext.EstimatedPromptTokenCount);
        }
        catch (Exception ex) when (request.AllowTransientFallbackOnPersistentFailure || options.Value.ChatTurns.AllowImplicitTransientFallback)
        {
            logger.LogWarning(
                ex,
                "Persistent context build failed; transient fallback explicitly allowed correlationId={CorrelationId}",
                request.CorrelationId);
            warnings.Add("persistent_memory_fallback_to_transient");
            var transient = BuildTransientContext(request, taskType);
            return ContextBuildResult.Success(
                transient.ContextMessages,
                transient.ContextSummary,
                transient.StructuredState,
                transient.ReasonCodes,
                "transient_fallback",
                EstimateTokens(transient.ContextMessages));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Persistent context build failed and fallback disabled correlationId={CorrelationId}",
                request.CorrelationId);
            return ContextBuildResult.Failure(BuildFailedTurnResponse(
                "I couldn't build context safely for this request.",
                route,
                warnings.Append("persistent_context_build_failed").ToArray(),
                "persistent_context_build_failed",
                conversationThreadId,
                conversationTurnId,
                ConversationTurnStatus.Failed,
                isDuplicateRequest: false,
                isTurnInProgress: false));
        }
    }

    private async Task<UserChatResponse> BuildDuplicateTurnResponseAsync(
        UserChatRequest request,
        Guid conversationThreadId,
        ConversationTurn turn,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var duplicateWarnings = warnings.Concat(["duplicate_request"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (turn.Status == ConversationTurnStatus.Completed && turn.AssistantMessageId.HasValue)
        {
            var assistant = await conversationMessageService!.GetMessageByIdAsync(
                request.UserId!.Value,
                conversationThreadId,
                turn.AssistantMessageId.Value,
                cancellationToken);
            if (assistant is not null)
            {
                return new UserChatResponse(
                    ReplyText: assistant.Content,
                    ModelUsed: turn.ModelUsed ?? options.Value.Routing.FastModelName,
                    ReasoningClass: Enum.TryParse<AIModelClass>(turn.ModelClass, out var parsedModelClass) ? parsedModelClass : AIModelClass.Fast,
                    SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ReferencedContextSummary: null,
                    Warnings: duplicateWarnings,
                    FollowUpIntentHints: [],
                    Succeeded: true,
                    FailureReason: null,
                    ConversationThreadId: conversationThreadId,
                    ConversationTurnId: turn.Id,
                    TurnStatus: turn.Status,
                    IsDuplicateRequest: true,
                    IsTurnInProgress: false);
            }
        }

        if (turn.Status is ConversationTurnStatus.Received
            or ConversationTurnStatus.PersistedUserTurn
            or ConversationTurnStatus.ContextBuilt
            or ConversationTurnStatus.AIInProgress
            or ConversationTurnStatus.AICompleted
            or ConversationTurnStatus.PersistedAssistantTurn)
        {
            return BuildFailedTurnResponse(
                "Your previous request is still being processed.",
                new AIModelRoute(AITaskType.Other, AIModelClass.Fast, options.Value.Routing.FastModelName, options.Value.Routing.FastDeploymentName, true, "duplicate_in_progress", []),
                duplicateWarnings.Append("turn_in_progress").ToArray(),
                "turn_in_progress",
                conversationThreadId,
                turn.Id,
                turn.Status,
                isDuplicateRequest: true,
                isTurnInProgress: true);
        }

        return BuildFailedTurnResponse(
            "Your previous request did not complete successfully. Please retry with a new request id.",
            new AIModelRoute(AITaskType.Other, AIModelClass.Fast, options.Value.Routing.FastModelName, options.Value.Routing.FastDeploymentName, true, "duplicate_terminal", []),
            duplicateWarnings.Append("duplicate_terminal_turn").ToArray(),
            turn.FailureCode ?? "duplicate_terminal_turn",
            conversationThreadId,
            turn.Id,
            turn.Status,
            isDuplicateRequest: true,
            isTurnInProgress: false);
    }

    private static UserChatResponse BuildFailedTurnResponse(
        string replyText,
        AIModelRoute route,
        IReadOnlyList<string> warnings,
        string failureReason,
        Guid? conversationThreadId,
        Guid? conversationTurnId,
        ConversationTurnStatus? turnStatus,
        bool isDuplicateRequest,
        bool isTurnInProgress,
        string? contextSummary = null)
    {
        return new UserChatResponse(
            ReplyText: replyText,
            ModelUsed: route.Model,
            ReasoningClass: route.ModelClass,
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ReferencedContextSummary: contextSummary,
            Warnings: warnings,
            FollowUpIntentHints: [],
            Succeeded: false,
            FailureReason: failureReason,
            ConversationThreadId: conversationThreadId,
            ConversationTurnId: conversationTurnId,
            TurnStatus: turnStatus,
            IsDuplicateRequest: isDuplicateRequest,
            IsTurnInProgress: isTurnInProgress);
    }

    private static string ResolveClientRequestId(UserChatRequest request, int maxLength)
    {
        var raw = string.IsNullOrWhiteSpace(request.ClientRequestId)
            ? request.CorrelationId
            : request.ClientRequestId!.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Guid.NewGuid().ToString("N");
        }

        return raw.Length <= maxLength ? raw : raw[..maxLength];
    }

    private static void ValidateUserMessage(string userMessage, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("User message is required.", nameof(userMessage));
        }

        if (userMessage.Trim().Length > maxLength)
        {
            throw new ArgumentException($"User message exceeds max length of {maxLength} characters.", nameof(userMessage));
        }
    }

    private static string ResolveFailureCode(AIResponse response, UserChatResponse parsed)
    {
        var failure = parsed.FailureReason ?? response.FailureReason ?? "ai_response_failed";
        if (failure.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "ai_timeout";
        }

        if (failure.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "ai_cancelled";
        }

        return failure.Length <= 80 ? failure : failure[..80];
    }

    private static bool IsTimeoutFailure(string? failureReason)
    {
        return !string.IsNullOrWhiteSpace(failureReason)
               && failureReason.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static int EstimateTokens(IReadOnlyList<AIMessage> messages)
    {
        var charCount = messages.Sum(x => x.Content.Length);
        return Math.Max(1, (charCount / 4) + (messages.Count * 8));
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

    private sealed record ContextBuildResult(
        bool Succeeded,
        UserChatResponse? Response,
        IReadOnlyList<AIMessage>? ContextMessages,
        string? ContextSummary,
        IReadOnlyDictionary<string, string>? StructuredState,
        IReadOnlyList<string>? ContextReasonCodes,
        string? ContextSource,
        int EstimatedTokens)
    {
        public static ContextBuildResult Success(
            IReadOnlyList<AIMessage> contextMessages,
            string? contextSummary,
            IReadOnlyDictionary<string, string> structuredState,
            IReadOnlyList<string> contextReasonCodes,
            string contextSource,
            int estimatedTokens)
            => new(true, null, contextMessages, contextSummary, structuredState, contextReasonCodes, contextSource, estimatedTokens);

        public static ContextBuildResult Failure(UserChatResponse response)
            => new(false, response, null, null, null, null, null, 0);
    }
}
