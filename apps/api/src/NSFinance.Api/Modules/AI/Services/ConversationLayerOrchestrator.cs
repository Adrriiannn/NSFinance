using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class ConversationLayerOrchestrator(
    IConversationContextService contextService,
    IConversationBehaviorEngine behaviorEngine,
    IModeRouter modeRouter,
    IResponseComposer responseComposer,
    ILogger<ConversationLayerOrchestrator> logger,
    IOptions<AIIntegrationOptions> options,
    IChatTelemetry telemetry,
    IOperationalFailureRecorder? failureRecorder = null,
    IHostEnvironment? hostEnvironment = null,
    IConversationThreadService? conversationThreadService = null,
    IConversationTurnService? conversationTurnService = null,
    IConversationMessageService? conversationMessageService = null,
    IConversationStateService? conversationStateService = null,
    IConversationSummaryService? conversationSummaryService = null,
    IPersistentConversationContextService? persistentConversationContextService = null,
    IResultContextService? resultContextService = null,
    ILocalDiscoveryConstraintExtractor? localDiscoveryConstraintExtractor = null) : IUserChatOrchestrator
{
    public async Task<UserChatResponse> ExecuteAsync(UserChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateUserMessage(request.UserMessage, options.Value.ChatTurns.MaxUserMessageChars);

        var totalStopwatch = Stopwatch.StartNew();
        var setupStopwatch = Stopwatch.StartNew();
        var lifecycleRoute = CreateOrchestrationLifecycleRoute();

        var workingRequest = request;
        var warnings = new List<string>();
        var conversationThreadId = request.ConversationThreadId;
        Guid? conversationTurnId = null;
        var canUsePersistentMemory = CanUsePersistentMemory(request);

        if (request.UsePersistentMemory && !canUsePersistentMemory)
        {
            warnings.Add("persistent_memory_unavailable_transient");
            await RecordTransientFallbackEventAsync(
                request,
                AITaskType.ConversationDecision,
                lifecycleRoute,
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
                    lifecycleRoute,
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
        var effectiveState = NormalizeIncomingState(request.State);
        var resultContextReadResult = new ResultContextReadResult(
            ActiveResultContext: null,
            BindingClassification: ResultContextBindingClassification.None,
            UsedClientResultSetId: false,
            ExpiredBindingCleared: false,
            ReasonCodes: []);

        if (canUsePersistentMemory)
        {
            var persistentUserId = request.UserId!.Value;
            conversationThreadId = await EnsureConversationThreadAsync(request, cancellationToken);
            workingRequest = request with
            {
                ConversationThreadId = conversationThreadId
            };
            var persistentThreadId = conversationThreadId.Value;
            var clientRequestId = ResolveClientRequestId(request, options.Value.ChatTurns.MaxClientRequestIdLength);
            var turnStart = await conversationTurnService!.StartOrGetAsync(
                persistentUserId,
                persistentThreadId,
                clientRequestId,
                request.CorrelationId,
                AITaskType.ConversationDecision,
                lifecycleRoute.ModelClass,
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
                    workingRequest,
                    AITaskType.ConversationDecision,
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
                    workingRequest,
                    AITaskType.ConversationDecision,
                    lifecycleRoute,
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

                var persistedState = await conversationStateService!.GetLatestStateAsync(
                    persistentUserId,
                    persistentThreadId,
                    cancellationToken);
                effectiveState = persistedState?.State ?? effectiveState;

                if (resultContextService is not null)
                {
                    resultContextReadResult = await resultContextService.ReadAsync(
                        new ResultContextReadRequest(
                            UserId: persistentUserId,
                            ConversationThreadId: persistentThreadId,
                            State: effectiveState,
                            ClientMetadata: workingRequest.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                            UserMessage: workingRequest.UserMessage),
                        cancellationToken);
                }
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
                    lifecycleRoute,
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
                    "Conversation-first turn setup failed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId}",
                    request.CorrelationId,
                    conversationThreadId,
                    activeTurn.Id);

                return BuildFailedTurnResponse(
                    "I couldn't prepare the conversation context safely.",
                    lifecycleRoute,
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
            (contextMessages, contextSummary, structuredState, contextReasonCodes) = BuildTransientContext(request);
        }

        setupStopwatch.Stop();

        if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
        {
            await conversationTurnService!.MarkAIInProgressAsync(
                request.UserId.Value,
                conversationThreadId.Value,
                conversationTurnId.Value,
                lifecycleRoute,
                cancellationToken);
        }

        var executionStopwatch = Stopwatch.StartNew();
        OrchestratedConversationResult executionResult;
        try
        {
            executionResult = await ExecuteConversationPathAsync(
                workingRequest,
                contextMessages,
                contextSummary,
                effectiveState,
                resultContextReadResult,
                warnings,
                cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            if (failureRecorder is not null)
            {
                await failureRecorder.RecordAsync(
                    new OperationalFailureRecordInput(
                        OperationalFailureArea.UserChat,
                        OperationalFailureSeverity.Warning,
                        "chat_turn_cancelled_during_execution",
                        $"chat_turn_cancelled_during_execution:{conversationThreadId?.ToString("N") ?? "no_thread"}",
                        request.CorrelationId,
                        conversationTurnId?.ToString("N"),
                        ex.Message,
                        null),
                    CancellationToken.None);
            }

            if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
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
                lifecycleRoute,
                warnings.Append("request_cancelled").ToArray(),
                "request_cancelled",
                conversationThreadId,
                conversationTurnId,
                ConversationTurnStatus.Cancelled,
                isDuplicateRequest: false,
                isTurnInProgress: false,
                contextSummary: contextSummary);
        }
        catch (Exception ex)
        {
            if (failureRecorder is not null)
            {
                await failureRecorder.RecordAsync(
                    new OperationalFailureRecordInput(
                        OperationalFailureArea.UserChat,
                        OperationalFailureSeverity.Error,
                        "chat_conversation_first_execution_failed",
                        $"chat_conversation_first_execution_failed:{conversationThreadId?.ToString("N") ?? "no_thread"}",
                        request.CorrelationId,
                        conversationTurnId?.ToString("N"),
                        ex.Message,
                        null),
                    CancellationToken.None);
            }

            if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
            {
                await conversationTurnService!.MarkFailedAsync(
                    request.UserId.Value,
                    conversationThreadId.Value,
                    conversationTurnId.Value,
                    "conversation_first_execution_failed",
                    ex.Message,
                    CancellationToken.None);
            }

            logger.LogError(
                ex,
                "Conversation-first execution failed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId}",
                request.CorrelationId,
                conversationThreadId,
                conversationTurnId);

            return BuildFailedTurnResponse(
                "I couldn't complete that turn safely.",
                lifecycleRoute,
                warnings.Append("conversation_first_execution_failed").ToArray(),
                "conversation_first_execution_failed",
                conversationThreadId,
                conversationTurnId,
                ConversationTurnStatus.Failed,
                isDuplicateRequest: false,
                isTurnInProgress: false,
                contextSummary: contextSummary);
        }
        finally
        {
            executionStopwatch.Stop();
        }

        long persistenceDurationMs = 0;
        if (canUsePersistentMemory && request.UserId.HasValue && conversationThreadId.HasValue && conversationTurnId.HasValue)
        {
            var persistentUserId = request.UserId.Value;
            var persistentThreadId = conversationThreadId.Value;
            var persistentTurnId = conversationTurnId.Value;

            if (!executionResult.Response.Succeeded)
            {
                await conversationTurnService!.MarkFailedAsync(
                    persistentUserId,
                    persistentThreadId,
                    persistentTurnId,
                    executionResult.Response.FailureReason ?? "conversation_first_response_failed",
                    executionResult.Response.ReplyText,
                    cancellationToken);

                return executionResult.Response with
                {
                    ConversationThreadId = conversationThreadId,
                    ConversationTurnId = conversationTurnId,
                    TurnStatus = ConversationTurnStatus.Failed
                };
            }

            var persistenceStopwatch = Stopwatch.StartNew();
            await conversationTurnService!.ApplyResolvedRouteAsync(
                persistentUserId,
                persistentThreadId,
                persistentTurnId,
                executionResult.AssistantRoute,
                cancellationToken);

            await conversationTurnService!.MarkAICompletedAsync(
                persistentUserId,
                persistentThreadId,
                persistentTurnId,
                executionStopwatch.ElapsedMilliseconds,
                cancellationToken);

            try
            {
                await PersistAssistantTurnContextAsync(
                    persistentUserId,
                    persistentThreadId,
                    AITaskType.ResponseComposition,
                    executionResult.AssistantRoute,
                    persistentTurnId,
                    executionResult.Response,
                    executionResult.State,
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
                    "Conversation-first assistant persistence failed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId}",
                    request.CorrelationId,
                    conversationThreadId,
                    conversationTurnId);

                return BuildFailedTurnResponse(
                    "I generated a response but couldn't persist it safely. Please retry.",
                    executionResult.AssistantRoute,
                    executionResult.Response.Warnings.Append("assistant_persistence_failed").ToArray(),
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
            persistenceStopwatch.Stop();
            persistenceDurationMs = persistenceStopwatch.ElapsedMilliseconds;
        }

        await telemetry.TrackAsync(
            "chat.turn.contract_compat",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["threadId"] = conversationThreadId?.ToString("N"),
                ["turnId"] = conversationTurnId?.ToString("N"),
                ["succeeded"] = executionResult.Response.Succeeded,
                ["model"] = executionResult.Response.ModelUsed,
                ["reasoningClass"] = executionResult.Response.ReasoningClass.ToString()
            },
            cancellationToken);

        await telemetry.TrackAsync(
            "chat.turn.model_usage_summary",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["threadId"] = conversationThreadId?.ToString("N"),
                ["turnId"] = conversationTurnId?.ToString("N"),
                ["totalModelCallCount"] = executionResult.TotalModelCallCount,
                ["heavyModelCallCount"] = executionResult.HeavyModelCallCount,
                ["fastModelCallCount"] = executionResult.FastModelCallCount,
                ["usedDeterministicPath"] = executionResult.UsedDeterministicPath,
                ["selectionReasons"] = executionResult.SelectionReasons.ToArray(),
                ["escalationJustifications"] = executionResult.EscalationJustifications.ToArray(),
                ["responseModel"] = executionResult.Response.ModelUsed,
                ["responseReasoningClass"] = executionResult.Response.ReasoningClass.ToString()
            },
            cancellationToken);

        await telemetry.TrackAsync(
            "chat.turn.latency_budget",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["threadId"] = conversationThreadId?.ToString("N"),
                ["turnId"] = conversationTurnId?.ToString("N"),
                ["setupDurationMs"] = setupStopwatch.ElapsedMilliseconds,
                ["behaviorDurationMs"] = executionResult.BehaviorDurationMs,
                ["modeExecutionDurationMs"] = executionResult.ModeExecutionDurationMs,
                ["responseCompositionDurationMs"] = executionResult.ResponseCompositionDurationMs,
                ["executionDurationMs"] = executionStopwatch.ElapsedMilliseconds,
                ["persistenceDurationMs"] = persistenceDurationMs,
                ["totalDurationMs"] = totalStopwatch.ElapsedMilliseconds
            },
            cancellationToken);

        logger.LogInformation(
            "Conversation-first user chat completed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} succeeded={Succeeded} model={Model} contextReasonCodes={ContextReasonCodes} warnings={Warnings}",
            request.CorrelationId,
            conversationThreadId,
            conversationTurnId,
            executionResult.Response.Succeeded,
            executionResult.Response.ModelUsed,
            string.Join(',', contextReasonCodes),
            string.Join(',', executionResult.Response.Warnings));

        return executionResult.Response with
        {
            ConversationThreadId = conversationThreadId,
            ConversationTurnId = conversationTurnId,
            TurnStatus = canUsePersistentMemory ? ConversationTurnStatus.Completed : null
        };
    }

    private async Task<OrchestratedConversationResult> ExecuteConversationPathAsync(
        UserChatRequest request,
        IReadOnlyList<AIMessage> contextMessages,
        string? contextSummary,
        ConversationStateSnapshot effectiveState,
        ResultContextReadResult resultContextReadResult,
        IReadOnlyList<string> existingWarnings,
        CancellationToken cancellationToken)
    {
        var clientMetadata = request.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var behaviorStopwatch = Stopwatch.StartNew();
        var behavior = await behaviorEngine.EvaluateAsync(
            new ConversationBehaviorRequest(
                Request: request,
                ContextMessages: contextMessages,
                ContextSummary: contextSummary,
                EffectiveState: effectiveState,
                ResultContext: resultContextReadResult.ActiveResultContext,
                ResultContextReadResult: resultContextReadResult,
                ClientMetadata: clientMetadata,
                FailureHistory: [],
                CancellationToken: cancellationToken),
            cancellationToken);
        behaviorStopwatch.Stop();

        var currentResultContext = resultContextReadResult.ActiveResultContext;
        var behaviorState = behavior.State;
        long modeExecutionDurationMs = 0;
        long responseCompositionDurationMs = 0;

        if (behaviorState.FollowUpBindingType is FollowUpBindingType.None or FollowUpBindingType.NewTopic)
        {
            currentResultContext = null;
        }

        if (resultContextService is not null
            && request.UserId.HasValue
            && request.ConversationThreadId.HasValue
            && currentResultContext is not null
            && !string.IsNullOrWhiteSpace(behaviorState.SelectedEntityId))
        {
            var selectedEntityUpdate = await resultContextService.TrySelectEntityAsync(
                request.UserId.Value,
                request.ConversationThreadId.Value,
                currentResultContext.ResultSetId,
                behaviorState.SelectedEntityId,
                cancellationToken);
            if (selectedEntityUpdate is not null)
            {
                currentResultContext = selectedEntityUpdate.Snapshot;
                behaviorState = behaviorState with
                {
                    SelectedEntityId = selectedEntityUpdate.Snapshot.SelectedEntityId,
                    ResultContextRef = selectedEntityUpdate.Reference
                };
                behavior = behavior with
                {
                    State = behaviorState
                };
            }
        }

        if (behavior.StayInDirectMode)
        {
            var compositionRequest = behavior.CompositionRequest ?? new ResponseCompositionRequest(
                ResponseType: ResponseCompositionType.Fallback,
                ToneDirective: ResponseToneDirective.Neutral,
                Strategy: behavior.StrategyDecision.Strategy,
                Mode: ConversationMode.Conversation,
                ReadinessLevel: behavior.State.ReadinessLevel,
                UserMessage: request.UserMessage,
                GroundedData: new GroundedDataEnvelope([], [], behavior.Warnings),
                Constraints: behavior.State.Constraints,
                MissingConstraints: behavior.State.MissingConstraints ?? [],
                MaxLengthHint: 420,
                ClarificationQuestion: behavior.State.LastClarificationPrompt,
                SuggestedOptions: behavior.State.LastSuggestedOptions);

            var responseCompositionStopwatch = Stopwatch.StartNew();
            var composed = await responseComposer.ComposeAsync(
                compositionRequest,
                request.CorrelationId,
                cancellationToken);
            responseCompositionStopwatch.Stop();
            responseCompositionDurationMs = responseCompositionStopwatch.ElapsedMilliseconds;

            var reply = new UserChatResponse(
                ReplyText: composed.ReplyText,
                ModelUsed: composed.ModelUsed,
                ReasoningClass: composed.ReasoningClass,
                SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                    behavior.State,
                    currentResultContext,
                    behavior.StrategyDecision,
                    behavior.StrategyDecision.SuggestedOptions,
                    composed.SuggestedStructuredStateUpdates,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                ReferencedContextSummary: contextSummary,
                Warnings: existingWarnings
                    .Concat(resultContextReadResult.ReasonCodes)
                    .Concat(behavior.Warnings)
                    .Concat(composed.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FollowUpIntentHints: composed.FollowUpIntentHints
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Succeeded: true,
                FailureReason: null);

            return new OrchestratedConversationResult(
                Response: reply,
                State: behavior.State,
                AssistantRoute: new AIModelRoute(
                    TaskType: AITaskType.ResponseComposition,
                    ModelClass: composed.ReasoningClass,
                    Model: composed.ModelUsed,
                    Deployment: composed.DeploymentUsed,
                    IsFallback: composed.ModelUsed.StartsWith("deterministic", StringComparison.OrdinalIgnoreCase),
                    Reason: "direct_mode_response",
                    Notes: []),
                TotalModelCallCount: behavior.DecisionModelCallCount + (composed.UsedDeterministicPath ? 0 : 1),
                HeavyModelCallCount: behavior.HeavyDecisionModelCallCount,
                FastModelCallCount: behavior.FastDecisionModelCallCount + (composed.UsedDeterministicPath ? 0 : 1),
                UsedDeterministicPath: behavior.PrimaryDecisionModelSelection.SelectionKind == ConversationModelSelectionKind.Deterministic
                                       || behavior.ExplorationSubtypeModelSelection?.SelectionKind == ConversationModelSelectionKind.Deterministic
                                       || composed.UsedDeterministicPath,
                BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
                ModeExecutionDurationMs: modeExecutionDurationMs,
                ResponseCompositionDurationMs: responseCompositionDurationMs,
                SelectionReasons: BuildSelectionReasons(
                    behavior.PrimaryDecisionModelSelection.SelectionReason,
                    behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                    composed.SelectionReason),
                EscalationJustifications: BuildEscalationJustifications(
                    behavior.PrimaryDecisionModelSelection.EscalationJustification,
                    behavior.ExplorationSubtypeModelSelection?.EscalationJustification));
        }

        await telemetry.TrackAsync(
            "chat.turn.mode_handoff",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["mode"] = behavior.TargetMode.ToString(),
                ["explorationSubtype"] = behavior.ExplorationSubtypeDecision?.Subtype.ToString(),
                ["readiness"] = behavior.StrategyDecision.Readiness.To.ToString()
            },
            cancellationToken);

        var modeExecutionStopwatch = Stopwatch.StartNew();
        var modeResult = await modeRouter.RouteAsync(
            new ConversationModeRequest(
                Request: request,
                ContextMessages: contextMessages,
                ContextSummary: contextSummary,
                State: behaviorState,
                ResultContext: currentResultContext,
                StrategyDecision: behavior.StrategyDecision,
                ExplorationSubtypeDecision: behavior.ExplorationSubtypeDecision,
                ClientMetadata: clientMetadata),
            cancellationToken);
        modeExecutionStopwatch.Stop();
        modeExecutionDurationMs = modeExecutionStopwatch.ElapsedMilliseconds;

        var finalState = modeResult.State;
        currentResultContext = modeResult.ResultContext ?? currentResultContext;

        if (!modeResult.Succeeded)
        {
            var failureResponse = new UserChatResponse(
                ReplyText: "I couldn't complete that mode transition safely.",
                ModelUsed: "deterministic-mode-failure",
                ReasoningClass: AIModelClass.Fast,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: contextSummary,
                Warnings: existingWarnings
                    .Concat(resultContextReadResult.ReasonCodes)
                    .Concat(behavior.Warnings)
                    .Concat(modeResult.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FollowUpIntentHints: modeResult.FollowUpIntentHints,
                Succeeded: false,
                FailureReason: modeResult.FailureReason ?? "mode_execution_failed");

            return new OrchestratedConversationResult(
                Response: failureResponse,
                State: finalState,
                AssistantRoute: new AIModelRoute(
                    TaskType: AITaskType.Other,
                    ModelClass: AIModelClass.Fast,
                    Model: "deterministic-mode-failure",
                    Deployment: "deterministic-mode-failure",
                    IsFallback: true,
                    Reason: "mode_execution_failed",
                    Notes: []),
                TotalModelCallCount: behavior.DecisionModelCallCount,
                HeavyModelCallCount: behavior.HeavyDecisionModelCallCount,
                FastModelCallCount: behavior.FastDecisionModelCallCount,
                UsedDeterministicPath: true,
                BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
                ModeExecutionDurationMs: modeExecutionDurationMs,
                ResponseCompositionDurationMs: responseCompositionDurationMs,
                SelectionReasons: BuildSelectionReasons(
                    behavior.PrimaryDecisionModelSelection.SelectionReason,
                    behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                    "mode_execution_failed_deterministic"),
                EscalationJustifications: BuildEscalationJustifications(
                    behavior.PrimaryDecisionModelSelection.EscalationJustification,
                    behavior.ExplorationSubtypeModelSelection?.EscalationJustification));
        }

        if (!string.IsNullOrWhiteSpace(modeResult.DeterministicReplyText))
        {
            var reply = new UserChatResponse(
                ReplyText: modeResult.DeterministicReplyText!,
                ModelUsed: "deterministic-mode-handler",
                ReasoningClass: AIModelClass.Fast,
                SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                    finalState,
                    currentResultContext,
                    behavior.StrategyDecision,
                    finalState.LastSuggestedOptions,
                    modeResult.SuggestedStructuredStateUpdates,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                ReferencedContextSummary: contextSummary,
                Warnings: existingWarnings
                    .Concat(resultContextReadResult.ReasonCodes)
                    .Concat(behavior.Warnings)
                    .Concat(modeResult.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FollowUpIntentHints: modeResult.FollowUpIntentHints
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Succeeded: true,
                FailureReason: null);

            return new OrchestratedConversationResult(
                Response: reply,
                State: finalState,
                AssistantRoute: new AIModelRoute(
                    TaskType: AITaskType.ResponseComposition,
                    ModelClass: AIModelClass.Fast,
                    Model: "deterministic-mode-handler",
                    Deployment: "deterministic-mode-handler",
                    IsFallback: true,
                    Reason: "mode_handler_deterministic_response",
                    Notes: []),
                TotalModelCallCount: behavior.DecisionModelCallCount,
                HeavyModelCallCount: behavior.HeavyDecisionModelCallCount,
                FastModelCallCount: behavior.FastDecisionModelCallCount,
                UsedDeterministicPath: true,
                BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
                ModeExecutionDurationMs: modeExecutionDurationMs,
                ResponseCompositionDurationMs: responseCompositionDurationMs,
                SelectionReasons: BuildSelectionReasons(
                    behavior.PrimaryDecisionModelSelection.SelectionReason,
                    behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                    "mode_handler_deterministic_response"),
                EscalationJustifications: BuildEscalationJustifications(
                    behavior.PrimaryDecisionModelSelection.EscalationJustification,
                    behavior.ExplorationSubtypeModelSelection?.EscalationJustification));
        }

        var routedCompositionRequest = modeResult.CompositionRequest ?? new ResponseCompositionRequest(
            ResponseType: ResponseCompositionType.Fallback,
            ToneDirective: ResponseToneDirective.Neutral,
            Strategy: behavior.StrategyDecision.Strategy,
            Mode: behavior.TargetMode,
            ReadinessLevel: finalState.ReadinessLevel,
            UserMessage: request.UserMessage,
            GroundedData: new GroundedDataEnvelope([], [], modeResult.Warnings),
            Constraints: finalState.Constraints,
            MissingConstraints: finalState.MissingConstraints ?? [],
            MaxLengthHint: 420,
            ClarificationQuestion: finalState.LastClarificationPrompt,
            SuggestedOptions: finalState.LastSuggestedOptions);

        var routedCompositionStopwatch = Stopwatch.StartNew();
        var routedComposition = await responseComposer.ComposeAsync(
            routedCompositionRequest,
            request.CorrelationId,
            cancellationToken);
        routedCompositionStopwatch.Stop();
        responseCompositionDurationMs = routedCompositionStopwatch.ElapsedMilliseconds;

        var routedReply = new UserChatResponse(
            ReplyText: routedComposition.ReplyText,
            ModelUsed: routedComposition.ModelUsed,
            ReasoningClass: routedComposition.ReasoningClass,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                finalState,
                currentResultContext,
                behavior.StrategyDecision,
                finalState.LastSuggestedOptions,
                modeResult.SuggestedStructuredStateUpdates,
                routedComposition.SuggestedStructuredStateUpdates),
            ReferencedContextSummary: contextSummary,
            Warnings: existingWarnings
                .Concat(resultContextReadResult.ReasonCodes)
                .Concat(behavior.Warnings)
                .Concat(modeResult.Warnings)
                .Concat(routedComposition.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FollowUpIntentHints: modeResult.FollowUpIntentHints
                .Concat(routedComposition.FollowUpIntentHints)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Succeeded: true,
            FailureReason: null);

        return new OrchestratedConversationResult(
            Response: routedReply,
            State: finalState,
            AssistantRoute: new AIModelRoute(
                TaskType: AITaskType.ResponseComposition,
                ModelClass: routedComposition.ReasoningClass,
                Model: routedComposition.ModelUsed,
                Deployment: routedComposition.DeploymentUsed,
                IsFallback: routedComposition.ModelUsed.StartsWith("deterministic", StringComparison.OrdinalIgnoreCase),
                Reason: "mode_handler_response",
                Notes: []),
            TotalModelCallCount: behavior.DecisionModelCallCount + (routedComposition.UsedDeterministicPath ? 0 : 1),
            HeavyModelCallCount: behavior.HeavyDecisionModelCallCount,
            FastModelCallCount: behavior.FastDecisionModelCallCount + (routedComposition.UsedDeterministicPath ? 0 : 1),
            UsedDeterministicPath: behavior.PrimaryDecisionModelSelection.SelectionKind == ConversationModelSelectionKind.Deterministic
                                   || behavior.ExplorationSubtypeModelSelection?.SelectionKind == ConversationModelSelectionKind.Deterministic
                                   || routedComposition.UsedDeterministicPath,
            BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
            ModeExecutionDurationMs: modeExecutionDurationMs,
            ResponseCompositionDurationMs: responseCompositionDurationMs,
            SelectionReasons: BuildSelectionReasons(
                behavior.PrimaryDecisionModelSelection.SelectionReason,
                behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                routedComposition.SelectionReason),
            EscalationJustifications: BuildEscalationJustifications(
                behavior.PrimaryDecisionModelSelection.EscalationJustification,
                behavior.ExplorationSubtypeModelSelection?.EscalationJustification));
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
        ConversationStateSnapshot state,
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
                    Topic: state.ActiveTopic,
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

        await conversationStateService!.SaveSnapshotAsync(
            userId,
            conversationThreadId,
            state,
            ConversationStateSnapshotReason.AssistantTurn,
            cancellationToken);

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
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Persistent context build start correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} cancellationRequested={CancellationRequested}",
            request.CorrelationId,
            conversationThreadId,
            conversationTurnId,
            cancellationToken.IsCancellationRequested);

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
            stopwatch.Stop();
            logger.LogInformation(
                "Persistent conversation context built correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} elapsedMs={ElapsedMs} estimatedTokens={EstimatedTokens}",
                request.CorrelationId,
                conversationThreadId,
                conversationTurnId,
                stopwatch.ElapsedMilliseconds,
                persistentContext.EstimatedPromptTokenCount);

            return ContextBuildResult.Success(
                persistentContext.ContextMessages,
                persistentContext.ContextSummary,
                persistentContext.StructuredState,
                persistentContext.ReasonCodes,
                "persistent",
                persistentContext.EstimatedPromptTokenCount);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger.LogWarning(
                ex,
                "Persistent context build cancelled by request token correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} elapsedMs={ElapsedMs}",
                request.CorrelationId,
                conversationThreadId,
                conversationTurnId,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var reasonCategory = ClassifyPersistentContextFailure(ex);
            var localDiscoveryCandidate = IsLocalDiscoveryCandidate(request);
            var fallbackPolicy = ResolveTransientFallbackPolicy(
                request,
                reasonCategory,
                localDiscoveryCandidate);
            warnings.Add($"persistent_context_failure:{reasonCategory}");

            logger.LogWarning(
                ex,
                "Persistent context build failed correlationId={CorrelationId} threadId={ThreadId} turnId={TurnId} reason={ReasonCategory} localDiscoveryCandidate={LocalDiscoveryCandidate} elapsedMs={ElapsedMs} cancellationRequested={CancellationRequested}",
                request.CorrelationId,
                conversationThreadId,
                conversationTurnId,
                reasonCategory,
                localDiscoveryCandidate,
                stopwatch.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);

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

                if (failureRecorder is not null)
                {
                    await failureRecorder.RecordAsync(
                        new OperationalFailureRecordInput(
                            OperationalFailureArea.UserChat,
                            OperationalFailureSeverity.Error,
                            "chat_persistent_context_build_failed",
                            $"chat_persistent_context_build_failed:{reasonCategory}:{conversationThreadId:N}",
                            request.CorrelationId,
                            conversationTurnId.ToString("N"),
                            ex.Message,
                            null),
                        CancellationToken.None);
                }

                return ContextBuildResult.Failure(
                    BuildFailedTurnResponse(
                        "I couldn't load the conversation context safely.",
                        route,
                        warnings.Append(fallbackPolicy.DenyReason ?? "persistent_context_unavailable").ToArray(),
                        fallbackPolicy.DenyReason ?? "persistent_context_unavailable",
                        conversationThreadId,
                        conversationTurnId,
                        ConversationTurnStatus.Failed,
                        isDuplicateRequest: false,
                        isTurnInProgress: false));
            }

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

            var transient = BuildTransientContext(request);
            warnings.Add("persistent_context_transient_fallback");

            return ContextBuildResult.Success(
                transient.ContextMessages,
                transient.ContextSummary,
                transient.StructuredState,
                transient.ReasonCodes,
                "transient_fallback",
                EstimateTokens(transient.ContextMessages));
        }
    }

    private (bool Allowed, string Mode, bool AllowedByConfig, string? DenyReason) ResolveTransientFallbackPolicy(
        UserChatRequest request,
        string reasonCategory,
        bool localDiscoveryCandidate)
    {
        var chatOptions = options.Value.ChatTurns;
        var isProduction = hostEnvironment?.IsProduction() == true;

        if (localDiscoveryCandidate
            && reasonCategory is "persistent_context_cancelled" or "persistent_context_timeout")
        {
            return (true, "local_discovery_nonessential_context", false, null);
        }

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
        if (exception is OperationCanceledException)
        {
            return "persistent_context_cancelled";
        }

        if (exception is TimeoutException)
        {
            return "persistent_context_timeout";
        }

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
            ["persistentContextCancellationRequested"] = cancellationToken.IsCancellationRequested,
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
            CancellationToken.None);
    }

    private bool IsLocalDiscoveryCandidate(UserChatRequest request)
    {
        if (localDiscoveryConstraintExtractor is not null)
        {
            var extracted = localDiscoveryConstraintExtractor.Extract(request.UserMessage);
            if (extracted.IsLocalDiscoveryCandidate)
            {
                return true;
            }
        }

        if (CompanionLocationGroundingParser.RequiresCurrentLocation(request.UserMessage))
        {
            return true;
        }

        if (request.Metadata is null)
        {
            return false;
        }

        if (request.Metadata.TryGetValue("chat_path", out var chatPath)
            && string.Equals(chatPath, "companion_local_places", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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
                    ReasoningClass: Enum.TryParse<AIModelClass>(turn.ModelClass, out var parsedModelClass)
                        ? parsedModelClass
                        : AIModelClass.Fast,
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
                new AIModelRoute(
                    AITaskType.Other,
                    AIModelClass.Fast,
                    options.Value.Routing.FastModelName,
                    options.Value.Routing.FastDeploymentName,
                    true,
                    "duplicate_in_progress",
                    []),
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
            new AIModelRoute(
                AITaskType.Other,
                AIModelClass.Fast,
                options.Value.Routing.FastModelName,
                options.Value.Routing.FastDeploymentName,
                true,
                "duplicate_terminal",
                []),
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

    private static AIModelRoute CreateOrchestrationLifecycleRoute()
    {
        return new AIModelRoute(
            TaskType: AITaskType.Other,
            ModelClass: AIModelClass.Fast,
            Model: "conversation-orchestrator-lifecycle",
            Deployment: "conversation-orchestrator-lifecycle",
            IsFallback: true,
            Reason: "orchestrator_lifecycle",
            Notes: ["model_neutral"]);
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

    private static int EstimateTokens(IReadOnlyList<AIMessage> messages)
    {
        var charCount = messages.Sum(x => x.Content.Length);
        return Math.Max(1, (charCount / 4) + (messages.Count * 8));
    }

    private static IReadOnlyList<string> BuildSelectionReasons(params string?[] reasons)
    {
        return reasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Select(static reason => reason!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildEscalationJustifications(params string?[] justifications)
    {
        return justifications
            .Where(static justification => !string.IsNullOrWhiteSpace(justification))
            .Select(static justification => justification!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private (IReadOnlyList<AIMessage> ContextMessages, string? ContextSummary, IReadOnlyDictionary<string, string> StructuredState, IReadOnlyList<string> ReasonCodes)
        BuildTransientContext(UserChatRequest request)
    {
        var context = contextService.BuildContext(
            new ConversationContextBuildRequest(
                TaskType: AITaskType.ConversationDecision,
                RecentTurns: request.RecentTurns,
                State: request.State,
                CurrentUserMessage: null,
                CorrelationId: request.CorrelationId));

        var messages = new List<AIMessage>(context.IncludedTurns.Count);
        messages.AddRange(context.IncludedTurns.Select(turn => new AIMessage(turn.Role, turn.Content, turn.TimestampUtc)));
        return (messages, context.ContextSummary, context.StructuredState, context.ReasonCodes);
    }

    private static ConversationStateSnapshot NormalizeIncomingState(ConversationStateSnapshot? state)
    {
        if (state is not null)
        {
            return state with
            {
                Constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase),
                Summaries = state.Summaries?.ToArray() ?? [],
                RecentConclusions = state.RecentConclusions?.ToArray() ?? [],
                MissingConstraints = state.MissingConstraints?.ToArray() ?? [],
                LastSuggestedOptions = state.LastSuggestedOptions?.ToArray() ?? [],
                LastSuggestedEntities = state.LastSuggestedEntities?.ToArray() ?? [],
                LoopGuards = state.LoopGuards ?? new ConversationLoopGuards()
            };
        }

        return new ConversationStateSnapshot(
            ActiveTopic: null,
            UserIntent: null,
            Constraints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Summaries: [],
            BudgetPreference: null,
            LocationPreference: null,
            MerchantInvestigationSubject: null,
            RecentConclusions: []);
    }

    private static IReadOnlyDictionary<string, string> BuildSuggestedStateUpdates(
        ConversationStateSnapshot state,
        ResultContextSnapshot? resultContext,
        ConversationTurnStrategyDecision strategyDecision,
        IReadOnlyList<string>? suggestedOptions,
        IReadOnlyDictionary<string, string> primaryUpdates,
        IReadOnlyDictionary<string, string> secondaryUpdates)
    {
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (state.ActiveMode.HasValue)
        {
            updates["active_mode"] = state.ActiveMode.Value.ToString();
        }

        if (state.ModeCandidate.HasValue)
        {
            updates["mode_candidate"] = state.ModeCandidate.Value.ToString();
        }

        updates["readiness_level"] = state.ReadinessLevel.ToString();
        updates["needs_follow_up"] = state.NeedsFollowUp ? "true" : "false";
        updates["follow_up_binding_type"] = state.FollowUpBindingType.ToString();
        updates["strategy"] = strategyDecision.Strategy.ToString();
        updates["strategy_confidence"] = strategyDecision.Confidence.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(state.InferredIntent))
        {
            updates["inferred_intent"] = state.InferredIntent;
        }

        if (!string.IsNullOrWhiteSpace(state.TransitionIntent))
        {
            updates["transition_intent"] = state.TransitionIntent;
        }

        if (!string.IsNullOrWhiteSpace(state.LastClarificationPrompt))
        {
            updates["last_clarification_prompt"] = state.LastClarificationPrompt;
        }

        if (state.MissingConstraints is { Count: > 0 })
        {
            updates["missing_constraints"] = string.Join('|', state.MissingConstraints);
        }

        if ((suggestedOptions?.Count ?? 0) > 0)
        {
            updates["suggested_options"] = string.Join('|', suggestedOptions!);
        }

        if (!string.IsNullOrWhiteSpace(state.SelectedEntityId))
        {
            updates["selected_entity_id"] = state.SelectedEntityId;
        }

        if (resultContext?.ResultSetId is Guid resultSetId)
        {
            updates["active_result_set_id"] = resultSetId.ToString("D");
        }

        MergeUpdates(updates, primaryUpdates);
        MergeUpdates(updates, secondaryUpdates);
        return updates;
    }

    private static void MergeUpdates(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            target[pair.Key.Trim()] = pair.Value.Trim();
        }
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

    private sealed record OrchestratedConversationResult(
        UserChatResponse Response,
        ConversationStateSnapshot State,
        AIModelRoute AssistantRoute,
        int TotalModelCallCount,
        int HeavyModelCallCount,
        int FastModelCallCount,
        bool UsedDeterministicPath,
        long BehaviorDurationMs,
        long ModeExecutionDurationMs,
        long ResponseCompositionDurationMs,
        IReadOnlyList<string> SelectionReasons,
        IReadOnlyList<string> EscalationJustifications);
}
