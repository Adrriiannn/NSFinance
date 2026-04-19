using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using NSFinance.Api.Persistence.Entities;
using System.Diagnostics;
using System.Text.Json;

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
    IHostEnvironment? hostEnvironment = null,
    IConversationThreadService? conversationThreadService = null,
    IConversationTurnService? conversationTurnService = null,
    IConversationMessageService? conversationMessageService = null,
    IConversationStateService? conversationStateService = null,
    IConversationSummaryService? conversationSummaryService = null,
    IPersistentConversationContextService? persistentConversationContextService = null,
    IUserChatCompanionHandoffService? companionHandoffService = null) : IUserChatOrchestrator
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
        var canUsePersistentMemory = CanUsePersistentMemory(request);

        if (request.UsePersistentMemory && !canUsePersistentMemory)
        {
            warnings.Add("persistent_memory_unavailable_transient");
            await RecordTransientFallbackEventAsync(
                request,
                taskType,
                route,
                conversationThreadId,
                conversationTurnId,
                reasonCategory: "persistent_memory_unavailable",
                fallbackMode: "forced_transient",
                allowedByConfig: true,
                exception: null,
                cancellationToken);

            if (options.Value.ChatTurns.RequirePersistentMemoryWhenRequested)
            {
                return BuildFailedTurnResponse(
                    "Persistent memory was requested but is not available for this request.",
                    route,
                    warnings.Append("persistent_memory_required_unavailable").ToArray(),
                    "persistent_memory_required_unavailable",
                    conversationThreadId,
                    conversationTurnId,
                    turnStatus: null,
                    isDuplicateRequest: false,
                    isTurnInProgress: false);
            }
        }

        IReadOnlyList<AIMessage> contextMessages;
        string? contextSummary;
        IReadOnlyDictionary<string, string> structuredState;
        IReadOnlyList<string> contextReasonCodes;

        if (canUsePersistentMemory)
        {
            var persistentUserId = request.UserId!.Value;
            conversationThreadId = await EnsureConversationThreadAsync(request, cancellationToken);
            var persistentThreadId = conversationThreadId.Value;
            var clientRequestId = ResolveClientRequestId(request, options.Value.ChatTurns.MaxClientRequestIdLength);
            var turnStart = await conversationTurnService!.StartOrGetAsync(
                persistentUserId,
                persistentThreadId,
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
                    persistentThreadId,
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
                    persistentThreadId,
                    activeTurn.Id,
                    cancellationToken);

                activeTurn = (await conversationTurnService.MarkPersistedUserTurnAsync(
                    persistentUserId,
                    persistentThreadId,
                    activeTurn.Id,
                    userMessage.Id,
                    cancellationToken)).Turn;

                var persistentContextResult = await TryBuildPersistentContextAsync(
                    request,
                    taskType,
                    route,
                    persistentThreadId,
                    activeTurn.Id,
                    warnings,
                    cancellationToken);

                if (!persistentContextResult.Succeeded)
                {
                    var failedResponse = persistentContextResult.Response!;
                    activeTurn = (await conversationTurnService.MarkFailedAsync(
                        persistentUserId,
                        persistentThreadId,
                        activeTurn.Id,
                        failedResponse.FailureReason ?? "persistent_context_build_failed",
                        failedResponse.ReplyText,
                        cancellationToken)).Turn;

                    return persistentContextResult.Response!;
                }

                contextMessages = persistentContextResult.ContextMessages!;
                contextSummary = persistentContextResult.ContextSummary;
                structuredState = persistentContextResult.StructuredState!;
                contextReasonCodes = persistentContextResult.ContextReasonCodes!;

                activeTurn = (await conversationTurnService.MarkContextBuiltAsync(
                    persistentUserId,
                    persistentThreadId,
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
                            $"chat_turn_cancelled_during_setup:{persistentThreadId:N}",
                            request.CorrelationId,
                            conversationTurnId?.ToString("N"),
                            ex.Message,
                            null),
                        CancellationToken.None);
                }

                await conversationTurnService.MarkCancelledAsync(
                    persistentUserId,
                    persistentThreadId,
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
                            $"chat_turn_setup_failed:{persistentThreadId:N}",
                            request.CorrelationId,
                            conversationTurnId?.ToString("N"),
                            ex.Message,
                            null),
                        CancellationToken.None);
                }

                await conversationTurnService.MarkFailedAsync(
                    persistentUserId,
                    persistentThreadId,
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

        var companionHandoffResponse = await TryExecuteCompanionHandoffAsync(
            request,
            taskType,
            route,
            warnings,
            contextSummary,
            canUsePersistentMemory,
            conversationThreadId,
            conversationTurnId,
            cancellationToken);
        if (companionHandoffResponse is not null)
        {
            return companionHandoffResponse;
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

            if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
            {
                var persistentUserId = request.UserId.Value;
                var persistentThreadId = conversationThreadId.Value;
                var persistentTurnId = conversationTurnId.Value;
                await conversationTurnService!.MarkFailedAsync(
                    persistentUserId,
                    persistentThreadId,
                    persistentTurnId,
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
                TurnStatus: canUsePersistentMemory ? ConversationTurnStatus.Failed : null);
        }

        if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
        {
            var persistentUserId = request.UserId.Value;
            var persistentThreadId = conversationThreadId.Value;
            var persistentTurnId = conversationTurnId.Value;
            await conversationTurnService!.MarkAIInProgressAsync(
                persistentUserId,
                persistentThreadId,
                persistentTurnId,
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

            if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
            {
                var persistentUserId = request.UserId.Value;
                var persistentThreadId = conversationThreadId.Value;
                var persistentTurnId = conversationTurnId.Value;
                await conversationTurnService!.MarkCancelledAsync(
                    persistentUserId,
                    persistentThreadId,
                    persistentTurnId,
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
        logger.LogInformation(
            "User chat parse diagnostics correlationId={CorrelationId} providerSucceeded={ProviderSucceeded} parserSucceeded={ParserSucceeded} contentLength={ContentLength} structuredPayloadLength={StructuredPayloadLength} reasonCodes={ReasonCodes}",
            request.CorrelationId,
            response.Succeeded,
            parsedResponse.Succeeded,
            response.Content?.Length ?? 0,
            response.StructuredPayloadJson?.Length ?? 0,
            string.Join(',', reasonCodes));

        if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
        {
            var persistentUserId = request.UserId.Value;
            var persistentThreadId = conversationThreadId.Value;
            var persistentTurnId = conversationTurnId.Value;
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
                        persistentUserId,
                        persistentThreadId,
                        persistentTurnId,
                        failureCode,
                        failureReason,
                        cancellationToken);
                }
                else
                {
                    await conversationTurnService!.MarkFailedAsync(
                        persistentUserId,
                        persistentThreadId,
                        persistentTurnId,
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
                persistentUserId,
                persistentThreadId,
                persistentTurnId,
                stopwatch.ElapsedMilliseconds,
                cancellationToken);

            try
            {
                await PersistAssistantTurnContextAsync(
                    persistentUserId,
                    persistentThreadId,
                    taskType,
                    route,
                    persistentTurnId,
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
                    persistentUserId,
                    persistentThreadId,
                    persistentTurnId,
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
                persistentUserId,
                persistentThreadId,
                persistentTurnId,
                cancellationToken);
        }

        logger.LogInformation(
            "User chat orchestrated path=generic_ai correlationId={CorrelationId} complexity={Complexity} task={TaskType} model={Model} deployment={Deployment} succeeded={Succeeded} contextReasonCodes={ContextReasonCodes} reasonCodes={ReasonCodes}",
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
            TurnStatus = canUsePersistentMemory ? ConversationTurnStatus.Completed : null
        };
    }

    private async Task<UserChatResponse?> TryExecuteCompanionHandoffAsync(
        UserChatRequest request,
        AITaskType taskType,
        AIModelRoute route,
        IReadOnlyList<string> existingWarnings,
        string? contextSummary,
        bool canUsePersistentMemory,
        Guid? conversationThreadId,
        Guid? conversationTurnId,
        CancellationToken cancellationToken)
    {
        if (companionHandoffService is null)
        {
            return null;
        }

        var sessionId = conversationThreadId?.ToString("N") ?? request.CorrelationId;
        var companionResponse = await companionHandoffService.TryExecuteAsync(
            request,
            route,
            sessionId,
            cancellationToken);
        if (companionResponse is null)
        {
            return null;
        }

        var merged = companionResponse with
        {
            ReferencedContextSummary = contextSummary,
            Warnings = existingWarnings
                .Concat(companionResponse.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ConversationThreadId = conversationThreadId,
            ConversationTurnId = conversationTurnId
        };

        logger.LogInformation(
            "User chat companion handoff completed correlationId={CorrelationId} succeeded={Succeeded} model={Model} warnings={Warnings}",
            request.CorrelationId,
            merged.Succeeded,
            merged.ModelUsed,
            string.Join(',', merged.Warnings));

        if (!canUsePersistentMemory
            || !request.UserId.HasValue
            || !conversationThreadId.HasValue
            || !conversationTurnId.HasValue)
        {
            return merged;
        }

        var persistentUserId = request.UserId.Value;
        var persistentThreadId = conversationThreadId.Value;
        var persistentTurnId = conversationTurnId.Value;
        var companionRoute = route with
        {
            Model = string.IsNullOrWhiteSpace(merged.ModelUsed) ? route.Model : merged.ModelUsed,
            Deployment = string.IsNullOrWhiteSpace(merged.ModelUsed) ? route.Deployment : merged.ModelUsed,
            Reason = "companion_handoff_local_places"
        };

        await conversationTurnService!.MarkAIInProgressAsync(
            persistentUserId,
            persistentThreadId,
            persistentTurnId,
            companionRoute,
            cancellationToken);

        if (!merged.Succeeded)
        {
            await conversationTurnService.MarkFailedAsync(
                persistentUserId,
                persistentThreadId,
                persistentTurnId,
                merged.FailureReason ?? "companion_handoff_failed",
                merged.ReplyText,
                cancellationToken);

            return merged with
            {
                TurnStatus = ConversationTurnStatus.Failed
            };
        }

        await conversationTurnService.MarkAICompletedAsync(
            persistentUserId,
            persistentThreadId,
            persistentTurnId,
            responseLatencyMs: 0,
            cancellationToken);

        try
        {
            await PersistAssistantTurnContextAsync(
                persistentUserId,
                persistentThreadId,
                taskType,
                companionRoute,
                persistentTurnId,
                merged,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await conversationTurnService.MarkFailedAsync(
                persistentUserId,
                persistentThreadId,
                persistentTurnId,
                "assistant_persistence_failed",
                ex.Message,
                CancellationToken.None);
            logger.LogError(
                ex,
                "Assistant persistence failed after companion handoff correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId}",
                request.CorrelationId,
                persistentThreadId,
                persistentTurnId);

            return BuildFailedTurnResponse(
                "I prepared a grounded response but couldn't persist it safely. Please retry.",
                companionRoute,
                merged.Warnings.Append("assistant_persistence_failed").ToArray(),
                "assistant_persistence_failed",
                conversationThreadId,
                conversationTurnId,
                ConversationTurnStatus.Failed,
                isDuplicateRequest: false,
                isTurnInProgress: false,
                contextSummary: contextSummary);
        }

        await conversationTurnService.MarkCompletedAsync(
            persistentUserId,
            persistentThreadId,
            persistentTurnId,
            cancellationToken);

        return merged with
        {
            TurnStatus = ConversationTurnStatus.Completed
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
        catch (Exception ex)
        {
            var reasonCategory = ClassifyPersistentContextFailure(ex);
            var fallbackPolicy = ResolveTransientFallbackPolicy(request);
            if (!fallbackPolicy.Allowed)
            {
                logger.LogError(
                    ex,
                    "Persistent context build failed and transient fallback denied correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} reason={ReasonCategory} policyReason={PolicyReason}",
                    request.CorrelationId,
                    conversationThreadId,
                    conversationTurnId,
                    reasonCategory,
                    fallbackPolicy.DenyReason ?? "none");

                await RecordTransientFallbackEventAsync(
                    request,
                    taskType,
                    route,
                    conversationThreadId,
                    conversationTurnId,
                    reasonCategory,
                    fallbackPolicy.Mode,
                    fallbackPolicy.AllowedByConfig,
                    ex,
                    cancellationToken);

                return ContextBuildResult.Failure(BuildFailedTurnResponse(
                    "I couldn't build context safely for this request.",
                    route,
                    warnings.Append("persistent_context_build_failed").Append(fallbackPolicy.DenyReason ?? "transient_fallback_denied").ToArray(),
                    "persistent_context_build_failed",
                    conversationThreadId,
                    conversationTurnId,
                    ConversationTurnStatus.Failed,
                    isDuplicateRequest: false,
                    isTurnInProgress: false));
            }

            logger.LogWarning(
                ex,
                "Persistent context build failed; transient fallback allowed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} reason={ReasonCategory} mode={FallbackMode}",
                request.CorrelationId,
                conversationThreadId,
                conversationTurnId,
                reasonCategory,
                fallbackPolicy.Mode);
            warnings.Add("persistent_memory_fallback_to_transient");
            warnings.Add(reasonCategory);

            await RecordTransientFallbackEventAsync(
                request,
                taskType,
                route,
                conversationThreadId,
                conversationTurnId,
                reasonCategory,
                fallbackPolicy.Mode,
                fallbackPolicy.AllowedByConfig,
                ex,
                cancellationToken);

            var transient = BuildTransientContext(request, taskType);
            return ContextBuildResult.Success(
                transient.ContextMessages,
                transient.ContextSummary,
                transient.StructuredState,
                transient.ReasonCodes,
                "transient_fallback",
                EstimateTokens(transient.ContextMessages));
        }
    }

    private (bool Allowed, string Mode, bool AllowedByConfig, string? DenyReason) ResolveTransientFallbackPolicy(UserChatRequest request)
    {
        var chatOptions = options.Value.ChatTurns;
        var isProduction = hostEnvironment?.IsProduction() == true;

        if (request.AllowTransientFallbackOnPersistentFailure)
        {
            if (!isProduction || chatOptions.AllowExplicitTransientFallbackInProduction)
            {
                return (true, "explicit", true, null);
            }

            return (false, "explicit", false, "explicit_fallback_blocked_in_production");
        }

        if (chatOptions.AllowImplicitTransientFallback)
        {
            if (!isProduction || chatOptions.AllowImplicitTransientFallbackInProduction)
            {
                return (true, "implicit", true, null);
            }

            return (false, "implicit", false, "implicit_fallback_blocked_in_production");
        }

        return (false, "none", false, "transient_fallback_disabled");
    }

    private static string ClassifyPersistentContextFailure(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("thread", StringComparison.OrdinalIgnoreCase)
            && message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "persistent_thread_invalid";
        }

        if (message.Contains("summary", StringComparison.OrdinalIgnoreCase))
        {
            return "summary_load_failed";
        }

        if (message.Contains("state", StringComparison.OrdinalIgnoreCase)
            && message.Contains("snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return "state_snapshot_failed";
        }

        return "persistent_context_build_failed";
    }

    private async Task RecordTransientFallbackEventAsync(
        UserChatRequest request,
        AITaskType taskType,
        AIModelRoute route,
        Guid? conversationThreadId,
        Guid? conversationTurnId,
        string reasonCategory,
        string fallbackMode,
        bool allowedByConfig,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        if (failureRecorder is null)
        {
            return;
        }

        var details = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["reasonCategory"] = reasonCategory,
            ["fallbackMode"] = fallbackMode,
            ["allowedByConfig"] = allowedByConfig,
            ["environment"] = hostEnvironment?.EnvironmentName,
            ["threadId"] = conversationThreadId?.ToString("N"),
            ["turnId"] = conversationTurnId?.ToString("N"),
            ["requestId"] = ResolveClientRequestId(request, options.Value.ChatTurns.MaxClientRequestIdLength),
            ["taskType"] = taskType.ToString(),
            ["modelClass"] = route.ModelClass.ToString(),
            ["model"] = route.Model,
            ["deployment"] = route.Deployment,
            ["usePersistentMemory"] = request.UsePersistentMemory,
            ["allowTransientFallbackOnPersistentFailure"] = request.AllowTransientFallbackOnPersistentFailure,
            ["exceptionType"] = exception?.GetType().Name
        });

        await failureRecorder.RecordAsync(
            new OperationalFailureRecordInput(
                Area: OperationalFailureArea.UserChat,
                Severity: OperationalFailureSeverity.Warning,
                FailureType: "chat_transient_fallback",
                Fingerprint: $"chat_transient_fallback:{reasonCategory}:{conversationThreadId?.ToString("N") ?? "no_thread"}",
                CorrelationId: request.CorrelationId,
                SubjectKey: conversationTurnId?.ToString("N") ?? conversationThreadId?.ToString("N") ?? request.UserId?.ToString("N"),
                FailureMessage: exception?.Message ?? $"Transient fallback ({fallbackMode}) for {reasonCategory}",
                DetailsJson: details),
            cancellationToken);
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
