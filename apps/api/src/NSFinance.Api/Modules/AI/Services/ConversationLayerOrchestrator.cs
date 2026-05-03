using System.Diagnostics;
using System.Globalization;
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
    ILocalDiscoveryConstraintExtractor? localDiscoveryConstraintExtractor = null,
    ITurnInterpretationEngine? turnInterpretationEngine = null,
    IPlaceRetrievalPlanner? placeRetrievalPlanner = null,
    IConversationIntelligenceService? conversationIntelligenceService = null,
    ICompanionActionResolver? companionActionResolver = null,
    IPlaceResultFollowUpService? placeResultFollowUpService = null,
    ICompanionSemanticIntentService? companionSemanticIntentService = null,
    ICompanionPlaceCandidatePoolService? companionPlaceCandidatePoolService = null,
    ICompanionPlaceConstraintEngine? companionPlaceConstraintEngine = null,
    ICompanionPlaceIntelligenceRankingService? companionPlaceRankingServiceV2 = null,
    ICompanionPlaceFinalistEnrichmentService? companionPlaceFinalistEnrichmentService = null,
    ICompanionPlaceSessionMemoryService? companionPlaceSessionMemoryService = null,
    ICompanionPlaceResultContextBinder? companionPlaceResultContextBinder = null,
    ICompanionPlaceParkingEvidenceService? companionPlaceParkingEvidenceService = null,
    ICompanionPlaceGuardEvidenceService? companionPlaceGuardEvidenceService = null,
    ICompanionPlaceGuardAwareFilter? companionPlaceGuardAwareFilter = null,
    ICompanionPlaceDuplicateClusterService? companionPlaceDuplicateClusterService = null,
    ICompanionPlaceCategoryCompatibilityService? companionPlaceCategoryCompatibilityService = null,
    ICompanionPlaceBrandIdentityService? companionPlaceBrandIdentityService = null,
    ICompanionPlaceSearchStrategyPlanner? companionPlaceSearchStrategyPlanner = null,
    ICompanionPlaceEntityVerificationService? companionPlaceEntityVerificationService = null,
    ICompanionPlaceSearchVariantValidator? companionPlaceSearchVariantValidator = null) : IUserChatOrchestrator
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
        var effectiveState = ApplyConversationMemoryTtl(NormalizeIncomingState(request.State));
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
                effectiveState = ApplyConversationMemoryTtl(persistedState?.State ?? effectiveState);

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

        effectiveState = ApplyConversationMemoryTtl(effectiveState);

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
                ["usedDeterministicRecovery"] = executionResult.UsedDeterministicRecovery,
                ["selectionReasons"] = executionResult.SelectionReasons.ToArray(),
                ["escalationJustifications"] = executionResult.EscalationJustifications.ToArray(),
                ["responseModel"] = executionResult.Response.ModelUsed,
                ["responseReasoningClass"] = executionResult.Response.ReasoningClass.ToString(),
                ["responseSelectionReason"] = executionResult.ResponseSelectionReason,
                ["responseFallbackUsed"] = executionResult.ResponseFallbackUsed,
                ["responseRecoveryReason"] = executionResult.ResponseRecoveryReason,
                ["responseUsedModelInvocation"] = executionResult.ResponseUsedModelInvocation
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
        var interpretedState = effectiveState;
        TurnInterpretationV2? turnInterpretation = null;
        PlaceRetrievalPlanV1? retrievalPlan = null;
        if (turnInterpretationEngine is not null
            && options.Value.Architecture.InterpretationEnabled)
        {
            var interpretationResult = await turnInterpretationEngine.InterpretAsync(
                new TurnInterpretationPromptInput(
                    UserMessage: request.UserMessage,
                    State: effectiveState,
                    Metadata: clientMetadata,
                    ContextSummary: contextSummary,
                    ResultContext: resultContextReadResult.ActiveResultContext),
                request.CorrelationId,
                cancellationToken);
            turnInterpretation = interpretationResult.Interpretation;
            retrievalPlan = placeRetrievalPlanner?.Build(interpretationResult.Interpretation);
            clientMetadata = TurnInterpretationMetadataMapper.Merge(
                clientMetadata,
                interpretationResult.Interpretation,
                retrievalPlan);
            interpretedState = ApplyInterpretationToState(
                effectiveState,
                interpretationResult.Interpretation,
                retrievalPlan,
                ttlMinutes: Math.Max(5, options.Value.Architecture.ExplorationConstraintTtlMinutes));
            if (interpretationResult.Warnings.Count > 0)
            {
                await telemetry.TrackAsync(
                    "chat.turn.interpretation_warnings",
                    new Dictionary<string, object?>
                    {
                        ["correlationId"] = request.CorrelationId,
                        ["selectionReason"] = interpretationResult.SelectionReason,
                        ["warnings"] = interpretationResult.Warnings.ToArray()
                    },
                cancellationToken);
            }
        }

        ConversationIntelligenceResult? conversationIntelligence = null;
        var intelligenceWarnings = Array.Empty<string>();
        var intelligenceModelCallCount = 0;
        var intelligenceFastModelCallCount = 0;
        var intelligenceHeavyModelCallCount = 0;
        if (conversationIntelligenceService is not null
            && options.Value.Architecture.ConversationIntelligenceEnabled)
        {
            var intelligenceResult = await conversationIntelligenceService.EvaluateAsync(
                new ConversationIntelligencePromptInput(
                    ChatRequest: request,
                    ContextMessages: contextMessages,
                    ContextSummary: contextSummary,
                    State: interpretedState,
                    ResultContext: resultContextReadResult.ActiveResultContext,
                    ResultContextReadResult: resultContextReadResult,
                    ClientMetadata: clientMetadata,
                    TurnInterpretation: turnInterpretation,
                    RetrievalPlan: retrievalPlan),
                request.CorrelationId,
                cancellationToken);
            conversationIntelligence = intelligenceResult.Intelligence;
            intelligenceWarnings = intelligenceResult.Warnings.ToArray();
            if (intelligenceResult.UsedModelInvocation)
            {
                intelligenceModelCallCount = 1;
                if (intelligenceResult.Route?.ModelClass == AIModelClass.HeavyReasoning)
                {
                    intelligenceHeavyModelCallCount = 1;
                }
                else
                {
                    intelligenceFastModelCallCount = 1;
                }
            }

            clientMetadata = MergeConversationIntelligenceMetadata(
                clientMetadata,
                conversationIntelligence);
        }

        CompanionResolvedAction? resolvedAction = null;
        if (companionActionResolver is not null
            && options.Value.Architecture.CompanionActionResolverEnabled)
        {
            resolvedAction = companionActionResolver.Resolve(
                request,
                interpretedState,
                resultContextReadResult,
                turnInterpretation,
                retrievalPlan,
                conversationIntelligence);
            await telemetry.TrackAsync(
                "chat.turn.resolved_action",
                BuildResolvedActionTelemetry(
                    request.CorrelationId,
                    resolvedAction,
                    resultContextReadResult.ActiveResultContext is not null),
                cancellationToken);
        }

        if (resolvedAction is not null
            && options.Value.Architecture.PlacesIntelligenceV2Enabled
            && CanExecutePlacesIntelligenceV2(resolvedAction))
        {
            var v2Result = await TryExecutePlacesIntelligenceV2Async(
                request,
                contextSummary,
                interpretedState,
                resultContextReadResult,
                existingWarnings,
                intelligenceWarnings,
                resolvedAction,
                conversationIntelligence,
                turnInterpretation,
                retrievalPlan,
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                cancellationToken);
            if (v2Result is not null)
            {
                return v2Result;
            }
        }

        if (resolvedAction is not null
            && options.Value.Architecture.ResolvedActionDirectExecutionEnabled
            && ShouldExecuteResolvedActionDirectly(resolvedAction, resultContextReadResult.ActiveResultContext))
        {
            if (resolvedAction.Kind == CompanionActionKind.CloseConversation)
            {
                await TrackResolvedActionExecutionDecisionAsync(
                    request,
                    resolvedAction,
                    executedDirectly: true,
                    fellThroughToBehaviorEngine: false,
                    resultContextReadResult.ActiveResultContext is not null,
                    structuredResultsReturned: false,
                    structuredResultCount: 0,
                    reason: "resolved_action_close_conversation_completed",
                    cancellationToken);
            }

            return await ExecuteResolvedActionDirectlyAsync(
                request,
                contextMessages,
                contextSummary,
                interpretedState,
                resultContextReadResult,
                existingWarnings,
                intelligenceWarnings,
                clientMetadata,
                resolvedAction,
                conversationIntelligence,
                turnInterpretation,
                retrievalPlan,
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                cancellationToken);
        }

        if (resolvedAction is not null
            && options.Value.Architecture.PlacesFollowUpExecutionEnabled
            && placeResultFollowUpService is not null
            && resultContextReadResult.ActiveResultContext is not null
            && resolvedAction.Kind is CompanionActionKind.FilterPreviousResults
                or CompanionActionKind.SortPreviousResults
                or CompanionActionKind.EnrichPreviousResults
                or CompanionActionKind.ComparePreviousResults)
        {
            return await ExecutePlaceFollowUpPathAsync(
                request,
                contextSummary,
                interpretedState,
                resultContextReadResult,
                existingWarnings,
                intelligenceWarnings,
                resolvedAction,
                conversationIntelligence,
                turnInterpretation,
                retrievalPlan,
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                cancellationToken);
        }

        if (resolvedAction is not null)
        {
            await TrackResolvedActionExecutionDecisionAsync(
                request,
                resolvedAction,
                executedDirectly: false,
                fellThroughToBehaviorEngine: true,
                resultContextReadResult.ActiveResultContext is not null,
                structuredResultsReturned: false,
                structuredResultCount: 0,
                reason: ResolveActionFallthroughReason(resolvedAction, resultContextReadResult.ActiveResultContext),
                cancellationToken);
        }

        var behaviorStopwatch = Stopwatch.StartNew();
        var behavior = await behaviorEngine.EvaluateAsync(
            new ConversationBehaviorRequest(
                Request: request,
                ContextMessages: contextMessages,
                ContextSummary: contextSummary,
                EffectiveState: interpretedState,
                ResultContext: resultContextReadResult.ActiveResultContext,
                ResultContextReadResult: resultContextReadResult,
                ClientMetadata: clientMetadata,
                FailureHistory: [],
                CancellationToken: cancellationToken,
                ConversationIntelligence: conversationIntelligence),
            cancellationToken);
        behaviorStopwatch.Stop();

        var currentResultContext = resultContextReadResult.ActiveResultContext;
        var behaviorState = RefreshExplorationContextUsage(ApplyConversationMemoryTtl(behavior.State));
        behavior = behavior with
        {
            State = behaviorState
        };
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
            compositionRequest = EnrichCompositionRequest(
                compositionRequest,
                conversationIntelligence,
                turnInterpretation,
                retrievalPlan,
                currentResultContext,
                resolvedAction,
                followUpExecutionResult: null);

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
                    behaviorState,
                    currentResultContext,
                    behavior.StrategyDecision,
                    behavior.StrategyDecision.SuggestedOptions,
                    composed.SuggestedStructuredStateUpdates,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                ReferencedContextSummary: contextSummary,
                Warnings: existingWarnings
                    .Concat(resultContextReadResult.ReasonCodes)
                    .Concat(intelligenceWarnings)
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
                State: behaviorState,
                AssistantRoute: new AIModelRoute(
                    TaskType: AITaskType.ResponseComposition,
                    ModelClass: composed.ReasoningClass,
                    Model: composed.ModelUsed,
                    Deployment: composed.DeploymentUsed,
                    IsFallback: composed.ModelUsed.StartsWith("deterministic", StringComparison.OrdinalIgnoreCase),
                    Reason: "direct_mode_response",
                    Notes: []),
                TotalModelCallCount: intelligenceModelCallCount + behavior.DecisionModelCallCount + (composed.UsedModelInvocation ? 1 : 0),
                HeavyModelCallCount: intelligenceHeavyModelCallCount + behavior.HeavyDecisionModelCallCount,
                FastModelCallCount: intelligenceFastModelCallCount + behavior.FastDecisionModelCallCount + (composed.UsedModelInvocation ? 1 : 0),
                UsedDeterministicPath: behavior.PrimaryDecisionModelSelection.SelectionKind == ConversationModelSelectionKind.Deterministic
                                       || behavior.ExplorationSubtypeModelSelection?.SelectionKind == ConversationModelSelectionKind.Deterministic
                                       || composed.UsedDeterministicPath,
                UsedDeterministicRecovery: composed.UsedDeterministicPath && composed.FallbackUsed && composed.UsedModelInvocation,
                BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
                ModeExecutionDurationMs: modeExecutionDurationMs,
                ResponseCompositionDurationMs: responseCompositionDurationMs,
                SelectionReasons: BuildSelectionReasons(
                    behavior.PrimaryDecisionModelSelection.SelectionReason,
                    behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                    composed.SelectionReason),
                EscalationJustifications: BuildEscalationJustifications(
                    behavior.PrimaryDecisionModelSelection.EscalationJustification,
                    behavior.ExplorationSubtypeModelSelection?.EscalationJustification),
                ResponseSelectionReason: composed.SelectionReason,
                ResponseFallbackUsed: composed.FallbackUsed,
                ResponseRecoveryReason: composed.RecoveryReason,
                ResponseUsedModelInvocation: composed.UsedModelInvocation);
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
                ClientMetadata: clientMetadata,
            ConversationIntelligence: conversationIntelligence),
            cancellationToken);
        modeExecutionStopwatch.Stop();
        modeExecutionDurationMs = modeExecutionStopwatch.ElapsedMilliseconds;

        var finalState = RefreshExplorationContextUsage(ApplyConversationMemoryTtl(modeResult.State));
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
                    .Concat(intelligenceWarnings)
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
                TotalModelCallCount: intelligenceModelCallCount + behavior.DecisionModelCallCount,
                HeavyModelCallCount: intelligenceHeavyModelCallCount + behavior.HeavyDecisionModelCallCount,
                FastModelCallCount: intelligenceFastModelCallCount + behavior.FastDecisionModelCallCount,
                UsedDeterministicPath: true,
                UsedDeterministicRecovery: false,
                BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
                ModeExecutionDurationMs: modeExecutionDurationMs,
                ResponseCompositionDurationMs: responseCompositionDurationMs,
                SelectionReasons: BuildSelectionReasons(
                    behavior.PrimaryDecisionModelSelection.SelectionReason,
                    behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                    "mode_execution_failed_deterministic"),
                EscalationJustifications: BuildEscalationJustifications(
                    behavior.PrimaryDecisionModelSelection.EscalationJustification,
                    behavior.ExplorationSubtypeModelSelection?.EscalationJustification),
                ResponseSelectionReason: "mode_execution_failed_deterministic",
                ResponseFallbackUsed: true,
                ResponseRecoveryReason: modeResult.FailureReason ?? "mode_execution_failed",
                ResponseUsedModelInvocation: false);
        }

        if (!string.IsNullOrWhiteSpace(modeResult.DeterministicReplyText))
        {
            var reply = new UserChatResponse(
                ReplyText: CollapseReplyForStructuredResults(
                    modeResult.DeterministicReplyText!,
                    modeResult.StructuredResults),
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
                    .Concat(intelligenceWarnings)
                    .Concat(behavior.Warnings)
                    .Concat(modeResult.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FollowUpIntentHints: modeResult.FollowUpIntentHints
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Succeeded: true,
                FailureReason: null,
                StructuredResults: modeResult.StructuredResults);

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
                TotalModelCallCount: intelligenceModelCallCount + behavior.DecisionModelCallCount,
                HeavyModelCallCount: intelligenceHeavyModelCallCount + behavior.HeavyDecisionModelCallCount,
                FastModelCallCount: intelligenceFastModelCallCount + behavior.FastDecisionModelCallCount,
                UsedDeterministicPath: true,
                UsedDeterministicRecovery: false,
                BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
                ModeExecutionDurationMs: modeExecutionDurationMs,
                ResponseCompositionDurationMs: responseCompositionDurationMs,
                SelectionReasons: BuildSelectionReasons(
                    behavior.PrimaryDecisionModelSelection.SelectionReason,
                    behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                    "mode_handler_deterministic_response"),
                EscalationJustifications: BuildEscalationJustifications(
                    behavior.PrimaryDecisionModelSelection.EscalationJustification,
                    behavior.ExplorationSubtypeModelSelection?.EscalationJustification),
                ResponseSelectionReason: "mode_handler_deterministic_response",
                ResponseFallbackUsed: false,
                ResponseRecoveryReason: null,
                ResponseUsedModelInvocation: false);
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
        routedCompositionRequest = EnrichCompositionRequest(
            routedCompositionRequest,
            conversationIntelligence,
            turnInterpretation,
            retrievalPlan,
            currentResultContext,
            resolvedAction,
            followUpExecutionResult: null);

        var routedCompositionStopwatch = Stopwatch.StartNew();
        var routedComposition = await responseComposer.ComposeAsync(
            routedCompositionRequest,
            request.CorrelationId,
            cancellationToken);
        routedCompositionStopwatch.Stop();
        responseCompositionDurationMs = routedCompositionStopwatch.ElapsedMilliseconds;

        var routedReply = new UserChatResponse(
            ReplyText: CollapseReplyForStructuredResults(
                routedComposition.ReplyText,
                modeResult.StructuredResults),
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
                .Concat(intelligenceWarnings)
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
            FailureReason: null,
            StructuredResults: modeResult.StructuredResults);

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
            TotalModelCallCount: intelligenceModelCallCount + behavior.DecisionModelCallCount + (routedComposition.UsedModelInvocation ? 1 : 0),
            HeavyModelCallCount: intelligenceHeavyModelCallCount + behavior.HeavyDecisionModelCallCount,
            FastModelCallCount: intelligenceFastModelCallCount + behavior.FastDecisionModelCallCount + (routedComposition.UsedModelInvocation ? 1 : 0),
            UsedDeterministicPath: behavior.PrimaryDecisionModelSelection.SelectionKind == ConversationModelSelectionKind.Deterministic
                                   || behavior.ExplorationSubtypeModelSelection?.SelectionKind == ConversationModelSelectionKind.Deterministic
                                   || routedComposition.UsedDeterministicPath,
            UsedDeterministicRecovery: routedComposition.UsedDeterministicPath && routedComposition.FallbackUsed && routedComposition.UsedModelInvocation,
            BehaviorDurationMs: behaviorStopwatch.ElapsedMilliseconds,
            ModeExecutionDurationMs: modeExecutionDurationMs,
            ResponseCompositionDurationMs: responseCompositionDurationMs,
            SelectionReasons: BuildSelectionReasons(
                behavior.PrimaryDecisionModelSelection.SelectionReason,
                behavior.ExplorationSubtypeModelSelection?.SelectionReason,
                routedComposition.SelectionReason),
            EscalationJustifications: BuildEscalationJustifications(
                behavior.PrimaryDecisionModelSelection.EscalationJustification,
                behavior.ExplorationSubtypeModelSelection?.EscalationJustification),
            ResponseSelectionReason: routedComposition.SelectionReason,
            ResponseFallbackUsed: routedComposition.FallbackUsed,
            ResponseRecoveryReason: routedComposition.RecoveryReason,
            ResponseUsedModelInvocation: routedComposition.UsedModelInvocation);
    }

    private bool CanExecutePlacesIntelligenceV2(CompanionResolvedAction action)
    {
        if (companionSemanticIntentService is null
            || companionPlaceCandidatePoolService is null
            || companionPlaceConstraintEngine is null
            || companionPlaceRankingServiceV2 is null
            || companionPlaceFinalistEnrichmentService is null
            || companionPlaceSessionMemoryService is null
            || companionPlaceResultContextBinder is null
            || companionPlaceParkingEvidenceService is null
            || companionPlaceGuardEvidenceService is null
            || companionPlaceGuardAwareFilter is null
            || companionPlaceDuplicateClusterService is null
            || companionPlaceCategoryCompatibilityService is null
            || companionPlaceBrandIdentityService is null
            || companionPlaceSearchStrategyPlanner is null
            || companionPlaceEntityVerificationService is null
            || companionPlaceSearchVariantValidator is null)
        {
            return false;
        }

        return action.Kind is CompanionActionKind.NewPlaceSearch
            or CompanionActionKind.FilterPreviousResults
            or CompanionActionKind.SortPreviousResults
            or CompanionActionKind.EnrichPreviousResults
            or CompanionActionKind.ComparePreviousResults;
    }

    private async Task<OrchestratedConversationResult?> TryExecutePlacesIntelligenceV2Async(
        UserChatRequest request,
        string? contextSummary,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContextReadResult,
        IReadOnlyList<string> existingWarnings,
        IReadOnlyList<string> intelligenceWarnings,
        CompanionResolvedAction resolvedAction,
        ConversationIntelligenceResult? conversationIntelligence,
        TurnInterpretationV2? turnInterpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        int intelligenceModelCallCount,
        int intelligenceFastModelCallCount,
        int intelligenceHeavyModelCallCount,
        CancellationToken cancellationToken)
    {
        var baseIntent = companionSemanticIntentService!.Build(
            request,
            state,
            resultContextReadResult.ActiveResultContext,
            turnInterpretation,
            retrievalPlan,
            conversationIntelligence,
            resolvedAction);
        await telemetry.TrackAsync(
            "places.v2.used",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["resolvedActionKind"] = resolvedAction.Kind.ToString()
            },
            cancellationToken);
        await telemetry.TrackAsync(
            "places.semantic_intent",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["actionKind"] = baseIntent.ActionKind,
                ["placeQuery"] = baseIntent.PlaceQuery,
                ["brandOrEntity"] = baseIntent.BrandOrEntity,
                ["hardFilterCount"] = baseIntent.HardFilters.Count,
                ["softPreferenceCount"] = baseIntent.SoftPreferences.Count,
                ["negativeFilterCount"] = baseIntent.NegativeFilters.Count,
                ["rankingGoal"] = baseIntent.RankingGoal,
                ["confidence"] = baseIntent.Confidence
            },
            cancellationToken);

        if (baseIntent.ActionKind == "new_place_search" && baseIntent.Location.RequiresLocation)
        {
            await TrackPlacesV2SkippedAsync(request, "missing_location", cancellationToken);
            return null;
        }

        CompanionPlaceSearchStrategy? searchStrategy = null;
        var effectiveBaseIntent = baseIntent;
        if (baseIntent.ActionKind == "new_place_search")
        {
            var plannedStrategy = await companionPlaceSearchStrategyPlanner!.PlanAsync(request, baseIntent, cancellationToken);
            var verification = await companionPlaceEntityVerificationService!.VerifyAsync(plannedStrategy, cancellationToken);
            if (verification.Status == "rejected")
            {
                return await BuildPlacesNoMatchResultAsync(
                    request,
                    contextSummary,
                    state,
                    resultContextReadResult,
                    existingWarnings,
                    intelligenceWarnings,
                    resolvedAction,
                    baseIntent,
                    verification.Warnings.Concat(verification.Evidence).ToArray(),
                    "entity_verification_rejected",
                    intelligenceModelCallCount,
                    intelligenceFastModelCallCount,
                    intelligenceHeavyModelCallCount,
                    cancellationToken);
            }

            searchStrategy = plannedStrategy with
            {
                Entity = verification.Entity,
                SearchVariants = companionPlaceSearchVariantValidator!.Validate(plannedStrategy with
                {
                    Entity = verification.Entity,
                    SearchVariants = verification.VerifiedVariants
                }),
                Warnings = plannedStrategy.Warnings.Concat(verification.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            effectiveBaseIntent = ApplySearchStrategyToIntent(baseIntent, searchStrategy);
        }

        ResultContextSnapshot? latestPlacesV2Context = null;
        if (request.UserId.HasValue && request.ConversationThreadId.HasValue && resultContextService is not null)
        {
            latestPlacesV2Context = await resultContextService.GetLatestPlacesV2ContextAsync(
                request.UserId.Value,
                request.ConversationThreadId.Value,
                cancellationToken);
        }

        var binding = companionPlaceResultContextBinder!.Bind(
            request,
            resultContextReadResult,
            latestPlacesV2Context,
            effectiveBaseIntent);

        CompanionPlaceSearchContext? previousContext = null;
        if (effectiveBaseIntent.ActionKind is "filter_previous_results" or "sort_previous_results")
        {
            previousContext = await companionPlaceSessionMemoryService!.LoadActiveSearchContextAsync(
                request,
                binding.Context,
                cancellationToken);
            if (previousContext is null || previousContext.CandidatePool.Count == 0)
            {
                await TrackPlacesV2SkippedAsync(request, "missing_session_pool", cancellationToken);
                return null;
            }
        }

        var intent = previousContext is null
            ? effectiveBaseIntent
            : MergeFollowUpIntent(previousContext.Intent, effectiveBaseIntent);
        IReadOnlyList<CompanionPlacePoolCandidate> pool;
        IReadOnlyList<string> diagnostics;
        if (previousContext is not null)
        {
            pool = previousContext.CandidatePool;
            diagnostics = ["places_session_memory_pool_used"];
        }
        else
        {
            var poolResult = searchStrategy is null
                ? await companionPlaceCandidatePoolService!.BuildPoolAsync(
                    intent,
                    request,
                    cancellationToken)
                : await companionPlaceCandidatePoolService!.BuildPoolAsync(
                    intent,
                    request,
                    searchStrategy,
                    cancellationToken);
            if (poolResult.Candidates.Count == 0)
            {
                return await BuildPlacesNoMatchResultAsync(
                    request,
                    contextSummary,
                    state,
                    resultContextReadResult,
                    existingWarnings,
                    intelligenceWarnings,
                    resolvedAction,
                    intent,
                    poolResult.Diagnostics,
                    "places_pool_empty",
                    intelligenceModelCallCount,
                    intelligenceFastModelCallCount,
                    intelligenceHeavyModelCallCount,
                    cancellationToken);
            }

            pool = poolResult.Candidates;
            diagnostics = poolResult.Diagnostics;
        }

        var brandFiltered = companionPlaceBrandIdentityService!.Apply(intent, pool, searchStrategy);
        var categoryFiltered = companionPlaceCategoryCompatibilityService!.Apply(intent, brandFiltered.Candidates, searchStrategy);
        var constrained = companionPlaceConstraintEngine!.Apply(intent, categoryFiltered.Candidates);
        await telemetry.TrackAsync(
            "places.constraint.rejected_count",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["rejectedByHardFilterCount"] = constrained.Rejected.Count
            },
            cancellationToken);
        if (constrained.Candidates.Count == 0)
        {
            return await BuildPlacesNoMatchResultAsync(
                request,
                contextSummary,
                state,
                resultContextReadResult,
                existingWarnings,
                intelligenceWarnings,
                resolvedAction,
                intent,
                diagnostics.Concat(constrained.Diagnostics).ToArray(),
                "no_hard_filter_matches",
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                cancellationToken);
        }

        var clustered = companionPlaceDuplicateClusterService!.Cluster(intent, constrained.Candidates);
        var ranked = companionPlaceRankingServiceV2!.Rank(intent, clustered);
        await telemetry.TrackAsync(
            "places.ranking.completed",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["rankedCount"] = ranked.RankedCandidates.Count
            },
            cancellationToken);

        var guardEvidence = await companionPlaceGuardEvidenceService!.EvaluateAsync(
            searchStrategy ?? new CompanionPlaceSearchStrategy(
                request.UserMessage,
                intent.PlaceQuery,
                null,
                intent.Role,
                [],
                intent.HardFilters,
                intent.NegativeFilters,
                intent.SoftPreferences,
                intent.NonSearchablePreferences,
                intent.Location,
                intent.RankingGoal,
                50,
                Math.Clamp(intent.RequestedMaxResults ?? 10, 1, 10),
                intent.Confidence,
                []),
            intent,
            ranked.RankedCandidates,
            cancellationToken);
        var finalCandidates = companionPlaceGuardAwareFilter!.Apply(searchStrategy, ranked.RankedCandidates, guardEvidence);
        if (finalCandidates.Count == 0)
        {
            return await BuildPlacesNoMatchResultAsync(
                request,
                contextSummary,
                state,
                resultContextReadResult,
                existingWarnings,
                intelligenceWarnings,
                resolvedAction,
                intent,
                diagnostics.Concat(constrained.Diagnostics).Concat(ranked.Diagnostics).Concat(guardEvidence.Diagnostics).ToArray(),
                "guard_evidence_no_matches",
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                cancellationToken);
        }

        var maxCards = Math.Clamp(intent.RequestedMaxResults ?? 10, 1, 10);
        await telemetry.TrackAsync(
            "places.finalists.selected",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["visibleCardCount"] = Math.Min(maxCards, finalCandidates.Count)
            },
            cancellationToken);
        var finalists = await companionPlaceFinalistEnrichmentService!.EnrichAsync(
            intent,
            finalCandidates,
            maxCards,
            cancellationToken);
        if (finalists.StructuredResults?.Items.Count is null or 0)
        {
            return await BuildPlacesNoMatchResultAsync(
                request,
                contextSummary,
                state,
                resultContextReadResult,
                existingWarnings,
                intelligenceWarnings,
                resolvedAction,
                intent,
                diagnostics.Concat(constrained.Diagnostics).Concat(ranked.Diagnostics).Concat(finalists.Diagnostics).ToArray(),
                "places_no_finalists",
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                cancellationToken);
        }

        await companionPlaceSessionMemoryService!.SaveSearchContextAsync(
            request,
            state,
            intent,
            pool,
            finalists.StructuredResults,
            cancellationToken);

        var nextState = state with
        {
            ActiveMode = ConversationMode.Exploration,
            ModeCandidate = ConversationMode.Exploration,
            ReadinessLevel = ConversationReadinessLevel.R4_ToolReady,
            NeedsFollowUp = true,
            FollowUpBindingType = FollowUpBindingType.Refine,
            PendingClarification = null
        };
        var strategy = new ConversationTurnStrategyDecision(
            Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
            ModeCandidate: ConversationMode.Exploration,
            Readiness: new ReadinessTransition(state.ReadinessLevel, ConversationReadinessLevel.R4_ToolReady),
            Confidence: intent.Confidence,
            FollowUpBindingType: previousContext is null ? FollowUpBindingType.NewTopic : FollowUpBindingType.Refine,
            ClarificationQuestion: null,
            SuggestedOptions: [],
            ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
            ReasonCodes: ["places_intelligence_v2_executed"]);
        var response = new UserChatResponse(
            ReplyText: BuildPlacesIntelligenceReply(intent),
            ModelUsed: "deterministic-places-intelligence-v2",
            ReasoningClass: AIModelClass.Fast,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                nextState,
                resultContextReadResult.ActiveResultContext,
                strategy,
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            ReferencedContextSummary: contextSummary,
            Warnings: existingWarnings
                .Concat(resultContextReadResult.ReasonCodes)
                .Concat(intelligenceWarnings)
                .Concat(resolvedAction.Warnings)
                .Concat(diagnostics)
                .Concat(brandFiltered.Diagnostics)
                .Concat(categoryFiltered.Diagnostics)
                .Concat(constrained.Diagnostics)
                .Concat(ranked.Diagnostics)
                .Concat(guardEvidence.Diagnostics)
                .Concat(finalists.Diagnostics)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FollowUpIntentHints: ["refine_place_preferences", "compare_options"],
            Succeeded: true,
            FailureReason: null,
            StructuredResults: finalists.StructuredResults);

        await TrackResolvedActionExecutionDecisionAsync(
            request,
            resolvedAction,
            executedDirectly: true,
            fellThroughToBehaviorEngine: false,
            hasResultContext: previousContext?.ResultContext is not null || resultContextReadResult.ActiveResultContext is not null,
            structuredResultsReturned: true,
            structuredResultCount: finalists.StructuredResults.Items.Count,
            reason: "places_intelligence_v2_completed",
            cancellationToken);

        await telemetry.TrackAsync(
            "places.v2.final_decision",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["actionKind"] = intent.ActionKind,
                ["placeQuery"] = intent.PlaceQuery,
                ["brandOrEntity"] = intent.BrandOrEntity,
                ["chosenResultSetId"] = binding.Context?.ResultSetId,
                ["candidatePoolCount"] = pool.Count,
                ["afterBrandFilterCount"] = brandFiltered.Candidates.Count,
                ["afterCategoryFilterCount"] = categoryFiltered.Candidates.Count,
                ["afterHardFilterCount"] = constrained.Candidates.Count,
                ["afterDuplicateClusterCount"] = clustered.Count,
                ["afterParkingEvidenceCount"] = guardEvidence.EvidenceByPlaceId.Count == 0 ? null : guardEvidence.EvidenceByPlaceId.Count,
                ["afterGuardFilterCount"] = finalCandidates.Count,
                ["returnedCardCount"] = finalists.StructuredResults.Items.Count,
                ["noMatchReason"] = null
            },
            cancellationToken);

        return BuildResolvedActionResult(
            response,
            nextState,
            "places_intelligence_v2_response",
            totalModelCallCount: intelligenceModelCallCount,
            fastModelCallCount: intelligenceFastModelCallCount,
            heavyModelCallCount: intelligenceHeavyModelCallCount,
            modeExecutionDurationMs: 0,
            responseCompositionDurationMs: 0,
            responseFallbackUsed: false,
            responseUsedModelInvocation: false);
    }

    private static CompanionSemanticIntent MergeFollowUpIntent(
        CompanionSemanticIntent previous,
        CompanionSemanticIntent followUp)
    {
        return previous with
        {
            ActionKind = followUp.ActionKind,
            HardFilters = previous.HardFilters.Concat(followUp.HardFilters).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NegativeFilters = previous.NegativeFilters.Concat(followUp.NegativeFilters).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SoftPreferences = previous.SoftPreferences.Concat(followUp.SoftPreferences).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NonSearchablePreferences = previous.NonSearchablePreferences.Concat(followUp.NonSearchablePreferences).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RequestedDetailFields = previous.RequestedDetailFields.Concat(followUp.RequestedDetailFields).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RankingGoal = followUp.RankingGoal == "intent_fit_then_distance"
                ? previous.RankingGoal
                : followUp.RankingGoal,
            RequestedMaxResults = followUp.RequestedMaxResults ?? previous.RequestedMaxResults,
            Confidence = Math.Max(previous.Confidence, followUp.Confidence)
        };
    }

    private static CompanionSemanticIntent ApplySearchStrategyToIntent(
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy strategy)
    {
        return intent with
        {
            PlaceQuery = string.IsNullOrWhiteSpace(strategy.CanonicalQuery) ? intent.PlaceQuery : strategy.CanonicalQuery,
            BrandOrEntity = strategy.Entity?.VerificationStatus == "rejected" ? null : strategy.Entity?.CanonicalName,
            Role = strategy.Role,
            HardFilters = strategy.HardRequirements,
            NegativeFilters = strategy.NegativeRequirements,
            SoftPreferences = strategy.SoftPreferences,
            NonSearchablePreferences = strategy.NonSearchablePreferences,
            RankingGoal = strategy.RankingGoal,
            RequestedMaxResults = strategy.MaxVisibleCards == 10 ? intent.RequestedMaxResults : strategy.MaxVisibleCards,
            Confidence = Math.Max(intent.Confidence, strategy.Confidence)
        };
    }

    private static string BuildPlacesIntelligenceReply(CompanionSemanticIntent intent)
    {
        if (intent.RequestedMaxResults == 1 || intent.RankingGoal == "distance")
        {
            return "Here's the closest one I found:";
        }

        if (intent.RequestedDetailFields.Contains("parking", StringComparer.OrdinalIgnoreCase)
            || intent.HardFilters.Any(static filter => filter.Contains("parking", StringComparison.OrdinalIgnoreCase)))
        {
            return "These are the strongest parking matches I found:";
        }

        if (intent.ActionKind is "filter_previous_results" or "sort_previous_results")
        {
            return "I filtered those results:";
        }

        if (intent.PlaceQuery?.Contains("fine dining", StringComparison.OrdinalIgnoreCase) == true
            || intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase))
        {
            return "I found the strongest fine-dining matches nearby:";
        }

        return "I found these matching options:";
    }

    private static bool RequiresParkingEvidence(CompanionSemanticIntent intent)
    {
        return intent.HardFilters.Any(static filter => filter.Contains("parking", StringComparison.OrdinalIgnoreCase))
               || intent.RequestedDetailFields.Contains("parking", StringComparer.OrdinalIgnoreCase);
    }

    private async Task TrackPlacesV2SkippedAsync(
        UserChatRequest request,
        string reason,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "places.v2.skipped",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["reason"] = reason
            },
            cancellationToken);
    }

    private async Task<OrchestratedConversationResult> BuildPlacesNoMatchResultAsync(
        UserChatRequest request,
        string? contextSummary,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContextReadResult,
        IReadOnlyList<string> existingWarnings,
        IReadOnlyList<string> intelligenceWarnings,
        CompanionResolvedAction resolvedAction,
        CompanionSemanticIntent intent,
        IReadOnlyList<string> diagnostics,
        string reason,
        int intelligenceModelCallCount,
        int intelligenceFastModelCallCount,
        int intelligenceHeavyModelCallCount,
        CancellationToken cancellationToken)
    {
        var nextState = state with
        {
            ActiveMode = ConversationMode.Exploration,
            ModeCandidate = ConversationMode.Exploration,
            NeedsFollowUp = true,
            FollowUpBindingType = FollowUpBindingType.Refine,
            PendingClarification = null
        };
        var strategy = new ConversationTurnStrategyDecision(
            Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
            ModeCandidate: ConversationMode.Exploration,
            Readiness: new ReadinessTransition(state.ReadinessLevel, ConversationReadinessLevel.R3_StructuredIncomplete),
            Confidence: intent.Confidence,
            FollowUpBindingType: FollowUpBindingType.Refine,
            ClarificationQuestion: null,
            SuggestedOptions: [],
            ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
            ReasonCodes: ["places_intelligence_v2_no_matches", reason]);
        var response = new UserChatResponse(
            ReplyText: "I couldn’t find any strong matches for that exact requirement nearby.",
            ModelUsed: "deterministic-places-intelligence-v2",
            ReasoningClass: AIModelClass.Fast,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                nextState,
                resultContextReadResult.ActiveResultContext,
                strategy,
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            ReferencedContextSummary: contextSummary,
            Warnings: existingWarnings
                .Concat(resultContextReadResult.ReasonCodes)
                .Concat(intelligenceWarnings)
                .Concat(resolvedAction.Warnings)
                .Concat(diagnostics)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FollowUpIntentHints: [],
            Succeeded: true,
            FailureReason: null,
            StructuredResults: null);

        await TrackResolvedActionExecutionDecisionAsync(
            request,
            resolvedAction,
            executedDirectly: true,
            fellThroughToBehaviorEngine: false,
            hasResultContext: resultContextReadResult.ActiveResultContext is not null,
            structuredResultsReturned: false,
            structuredResultCount: 0,
            reason: reason,
            cancellationToken);

        await telemetry.TrackAsync(
            "places.v2.final_decision",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["actionKind"] = intent.ActionKind,
                ["placeQuery"] = intent.PlaceQuery,
                ["brandOrEntity"] = intent.BrandOrEntity,
                ["chosenResultSetId"] = resultContextReadResult.ActiveResultContext?.ResultSetId,
                ["returnedCardCount"] = 0,
                ["noMatchReason"] = reason
            },
            cancellationToken);

        return BuildResolvedActionResult(
            response,
            nextState,
            "places_intelligence_v2_no_match_response",
            totalModelCallCount: intelligenceModelCallCount,
            fastModelCallCount: intelligenceFastModelCallCount,
            heavyModelCallCount: intelligenceHeavyModelCallCount,
            modeExecutionDurationMs: 0,
            responseCompositionDurationMs: 0,
            responseFallbackUsed: false,
            responseUsedModelInvocation: false);
    }

    private bool ShouldExecuteResolvedActionDirectly(
        CompanionResolvedAction action,
        ResultContextSnapshot? activeResultContext)
    {
        if (action.Kind == CompanionActionKind.CloseConversation)
        {
            return true;
        }

        if (action.Kind == CompanionActionKind.NewPlaceSearch)
        {
            return action.RequiresToolExecution && !action.RequiresClarification;
        }

        if (action.Kind is CompanionActionKind.FilterPreviousResults
            or CompanionActionKind.SortPreviousResults
            or CompanionActionKind.EnrichPreviousResults
            or CompanionActionKind.ComparePreviousResults)
        {
            return options.Value.Architecture.PlacesFollowUpExecutionEnabled
                   && placeResultFollowUpService is not null
                   && activeResultContext is not null;
        }

        return false;
    }

    private async Task<OrchestratedConversationResult> ExecuteResolvedActionDirectlyAsync(
        UserChatRequest request,
        IReadOnlyList<AIMessage> contextMessages,
        string? contextSummary,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContextReadResult,
        IReadOnlyList<string> existingWarnings,
        IReadOnlyList<string> intelligenceWarnings,
        IReadOnlyDictionary<string, string> clientMetadata,
        CompanionResolvedAction resolvedAction,
        ConversationIntelligenceResult? conversationIntelligence,
        TurnInterpretationV2? turnInterpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        int intelligenceModelCallCount,
        int intelligenceFastModelCallCount,
        int intelligenceHeavyModelCallCount,
        CancellationToken cancellationToken)
    {
        return resolvedAction.Kind switch
        {
            CompanionActionKind.CloseConversation => BuildResolvedDirectResponse(
                request,
                contextSummary,
                state with
                {
                    NeedsFollowUp = false,
                    PendingClarification = null,
                    LastSuggestedOptions = []
                },
                existingWarnings.Concat(intelligenceWarnings).ToArray(),
                BuildCloseReply(request.UserMessage),
                "resolved_action_close_conversation"),
            CompanionActionKind.FilterPreviousResults
                or CompanionActionKind.SortPreviousResults
                or CompanionActionKind.EnrichPreviousResults
                or CompanionActionKind.ComparePreviousResults => await ExecutePlaceFollowUpPathAsync(
                    request,
                    contextSummary,
                    state,
                    resultContextReadResult,
                    existingWarnings,
                    intelligenceWarnings,
                    resolvedAction,
                    conversationIntelligence,
                    turnInterpretation,
                    retrievalPlan,
                    intelligenceModelCallCount,
                    intelligenceFastModelCallCount,
                    intelligenceHeavyModelCallCount,
                    cancellationToken),
            CompanionActionKind.NewPlaceSearch => await ExecuteResolvedNewPlaceSearchPathAsync(
                request,
                contextMessages,
                contextSummary,
                state,
                resultContextReadResult,
                existingWarnings,
                intelligenceWarnings,
                clientMetadata,
                resolvedAction,
                conversationIntelligence,
                turnInterpretation,
                retrievalPlan,
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                cancellationToken),
            _ => BuildResolvedDirectResponse(
                request,
                contextSummary,
                state,
                existingWarnings.Concat(intelligenceWarnings).ToArray(),
                "I can help with that, but I need one more detail before taking action.",
                "resolved_action_direct_fallback")
        };
    }

    private async Task<OrchestratedConversationResult> ExecuteResolvedNewPlaceSearchPathAsync(
        UserChatRequest request,
        IReadOnlyList<AIMessage> contextMessages,
        string? contextSummary,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContextReadResult,
        IReadOnlyList<string> existingWarnings,
        IReadOnlyList<string> intelligenceWarnings,
        IReadOnlyDictionary<string, string> clientMetadata,
        CompanionResolvedAction resolvedAction,
        ConversationIntelligenceResult? conversationIntelligence,
        TurnInterpretationV2? turnInterpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        int intelligenceModelCallCount,
        int intelligenceFastModelCallCount,
        int intelligenceHeavyModelCallCount,
        CancellationToken cancellationToken)
    {
        var executionState = ApplyResolvedActionToState(state, resolvedAction);
        var actionMetadata = MergeResolvedActionMetadata(clientMetadata, turnInterpretation, retrievalPlan, resolvedAction);
        var strategy = new ConversationTurnStrategyDecision(
            Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
            ModeCandidate: ConversationMode.Exploration,
            Readiness: new ReadinessTransition(state.ReadinessLevel, ConversationReadinessLevel.R4_ToolReady),
            Confidence: conversationIntelligence?.UserIntentConfidence ?? turnInterpretation?.Confidence ?? 0.86d,
            FollowUpBindingType: FollowUpBindingType.NewTopic,
            ClarificationQuestion: null,
            SuggestedOptions: [],
            ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
            ReasonCodes: ["resolved_action_direct_place_search"]);
        var subtype = new ExplorationSubtypeDecision(
            Subtype: ExplorationSubtype.Structured,
            Confidence: strategy.Confidence,
            ToolPathEligible: true,
            PrimaryWhy: resolvedAction.Reason,
            MissingConstraints: [],
            ReasonCodes: ["resolved_action_direct_structured_exploration"]);

        await telemetry.TrackAsync(
            "chat.turn.mode_handoff",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["mode"] = ConversationMode.Exploration.ToString(),
                ["explorationSubtype"] = ExplorationSubtype.Structured.ToString(),
                ["readiness"] = ConversationReadinessLevel.R4_ToolReady.ToString(),
                ["handoffReason"] = "resolved_action_direct_place_search"
            },
            cancellationToken);

        var modeStopwatch = Stopwatch.StartNew();
        var modeResult = await modeRouter.RouteAsync(
            new ConversationModeRequest(
                Request: request,
                ContextMessages: contextMessages,
                ContextSummary: contextSummary,
                State: executionState,
                ResultContext: resultContextReadResult.ActiveResultContext,
                StrategyDecision: strategy,
                ExplorationSubtypeDecision: subtype,
                ClientMetadata: actionMetadata,
                ConversationIntelligence: conversationIntelligence),
            cancellationToken);
        modeStopwatch.Stop();

        var finalState = RefreshExplorationContextUsage(ApplyConversationMemoryTtl(modeResult.State));
        var currentResultContext = modeResult.ResultContext ?? resultContextReadResult.ActiveResultContext;
        await TrackResolvedActionExecutionDecisionAsync(
            request,
            resolvedAction,
            executedDirectly: true,
            fellThroughToBehaviorEngine: false,
            hasResultContext: currentResultContext is not null,
            structuredResultsReturned: modeResult.StructuredResults?.Items.Count > 0,
            structuredResultCount: modeResult.StructuredResults?.Items.Count ?? 0,
            reason: "resolved_action_direct_place_search_completed",
            cancellationToken);

        if (!modeResult.Succeeded)
        {
            var failureResponse = new UserChatResponse(
                ReplyText: "I couldn't complete that search safely.",
                ModelUsed: "deterministic-resolved-action-mode-failure",
                ReasoningClass: AIModelClass.Fast,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: contextSummary,
                Warnings: existingWarnings
                    .Concat(resultContextReadResult.ReasonCodes)
                    .Concat(intelligenceWarnings)
                    .Concat(resolvedAction.Warnings)
                    .Concat(modeResult.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FollowUpIntentHints: modeResult.FollowUpIntentHints,
                Succeeded: false,
                FailureReason: modeResult.FailureReason ?? "resolved_action_mode_execution_failed");

            return BuildResolvedActionResult(
                failureResponse,
                finalState,
                "resolved_action_mode_execution_failed",
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                modeStopwatch.ElapsedMilliseconds,
                responseCompositionDurationMs: 0,
                responseFallbackUsed: true,
                responseUsedModelInvocation: false);
        }

        if (!string.IsNullOrWhiteSpace(modeResult.DeterministicReplyText))
        {
            var deterministicReply = CollapseReplyForStructuredResults(
                SanitizeUserFacingReply(modeResult.DeterministicReplyText!, modeResult.StructuredResults, resolvedAction),
                modeResult.StructuredResults);
            var reply = new UserChatResponse(
                ReplyText: deterministicReply,
                ModelUsed: "deterministic-resolved-action-mode-handler",
                ReasoningClass: AIModelClass.Fast,
                SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                    finalState,
                    currentResultContext,
                    strategy,
                    finalState.LastSuggestedOptions,
                    modeResult.SuggestedStructuredStateUpdates,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
                ReferencedContextSummary: contextSummary,
                Warnings: existingWarnings
                    .Concat(resultContextReadResult.ReasonCodes)
                    .Concat(intelligenceWarnings)
                    .Concat(resolvedAction.Warnings)
                    .Concat(modeResult.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FollowUpIntentHints: modeResult.FollowUpIntentHints
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Succeeded: true,
                FailureReason: null,
                StructuredResults: modeResult.StructuredResults);

            return BuildResolvedActionResult(
                reply,
                finalState,
                "resolved_action_mode_handler_deterministic_response",
                intelligenceModelCallCount,
                intelligenceFastModelCallCount,
                intelligenceHeavyModelCallCount,
                modeStopwatch.ElapsedMilliseconds,
                responseCompositionDurationMs: 0,
                responseFallbackUsed: false,
                responseUsedModelInvocation: false);
        }

        var compositionRequest = EnrichCompositionRequest(
            modeResult.CompositionRequest ?? new ResponseCompositionRequest(
                ResponseType: ResponseCompositionType.ResultSummary,
                ToneDirective: ResolveToneDirective(conversationIntelligence),
                Strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                Mode: ConversationMode.Exploration,
                ReadinessLevel: finalState.ReadinessLevel,
                UserMessage: request.UserMessage,
                GroundedData: new GroundedDataEnvelope([], [], modeResult.Warnings),
                Constraints: finalState.Constraints,
                MissingConstraints: finalState.MissingConstraints ?? [],
                MaxLengthHint: modeResult.StructuredResults?.Items.Count > 0 ? 220 : 420,
                ClarificationQuestion: null,
                SuggestedOptions: []),
            conversationIntelligence,
            turnInterpretation,
            retrievalPlan,
            currentResultContext,
            resolvedAction,
            followUpExecutionResult: null);

        var compositionStopwatch = Stopwatch.StartNew();
        var composed = await responseComposer.ComposeAsync(
            compositionRequest,
            request.CorrelationId,
            cancellationToken);
        compositionStopwatch.Stop();
        var replyText = CollapseReplyForStructuredResults(
            SanitizeUserFacingReply(composed.ReplyText, modeResult.StructuredResults, resolvedAction),
            modeResult.StructuredResults);
        var response = new UserChatResponse(
            ReplyText: replyText,
            ModelUsed: composed.ModelUsed,
            ReasoningClass: composed.ReasoningClass,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                finalState,
                currentResultContext,
                strategy,
                finalState.LastSuggestedOptions,
                modeResult.SuggestedStructuredStateUpdates,
                composed.SuggestedStructuredStateUpdates),
            ReferencedContextSummary: contextSummary,
            Warnings: existingWarnings
                .Concat(resultContextReadResult.ReasonCodes)
                .Concat(intelligenceWarnings)
                .Concat(resolvedAction.Warnings)
                .Concat(modeResult.Warnings)
                .Concat(composed.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FollowUpIntentHints: modeResult.FollowUpIntentHints
                .Concat(composed.FollowUpIntentHints)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Succeeded: true,
            FailureReason: null,
            StructuredResults: modeResult.StructuredResults);

        return BuildResolvedActionResult(
            response,
            finalState,
            composed.SelectionReason,
            intelligenceModelCallCount + (composed.UsedModelInvocation ? 1 : 0),
            intelligenceFastModelCallCount + (composed.UsedModelInvocation ? 1 : 0),
            intelligenceHeavyModelCallCount,
            modeStopwatch.ElapsedMilliseconds,
            compositionStopwatch.ElapsedMilliseconds,
            composed.FallbackUsed,
            composed.UsedModelInvocation,
            routeModel: composed.ModelUsed,
            routeDeployment: composed.DeploymentUsed,
            routeClass: composed.ReasoningClass,
            usedDeterministicPath: composed.UsedDeterministicPath,
            responseRecoveryReason: composed.RecoveryReason);
    }

    private OrchestratedConversationResult BuildResolvedDirectResponse(
        UserChatRequest request,
        string? contextSummary,
        ConversationStateSnapshot state,
        IReadOnlyList<string> warnings,
        string replyText,
        string routeReason)
    {
        var response = new UserChatResponse(
            ReplyText: replyText,
            ModelUsed: "deterministic-resolved-action",
            ReasoningClass: AIModelClass.Fast,
            SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ReferencedContextSummary: contextSummary,
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            FollowUpIntentHints: [],
            Succeeded: true,
            FailureReason: null);

        return BuildResolvedActionResult(
            response,
            state,
            routeReason,
            totalModelCallCount: 0,
            fastModelCallCount: 0,
            heavyModelCallCount: 0,
            modeExecutionDurationMs: 0,
            responseCompositionDurationMs: 0,
            responseFallbackUsed: false,
            responseUsedModelInvocation: false);
    }

    private static OrchestratedConversationResult BuildResolvedActionResult(
        UserChatResponse response,
        ConversationStateSnapshot state,
        string selectionReason,
        int totalModelCallCount,
        int fastModelCallCount,
        int heavyModelCallCount,
        long modeExecutionDurationMs,
        long responseCompositionDurationMs,
        bool responseFallbackUsed,
        bool responseUsedModelInvocation,
        string routeModel = "deterministic-resolved-action",
        string routeDeployment = "deterministic-resolved-action",
        AIModelClass routeClass = AIModelClass.Fast,
        bool usedDeterministicPath = true,
        string? responseRecoveryReason = null)
    {
        return new OrchestratedConversationResult(
            Response: response,
            State: state,
            AssistantRoute: new AIModelRoute(
                TaskType: AITaskType.ResponseComposition,
                ModelClass: routeClass,
                Model: routeModel,
                Deployment: routeDeployment,
                IsFallback: routeModel.StartsWith("deterministic", StringComparison.OrdinalIgnoreCase),
                Reason: selectionReason,
                Notes: []),
            TotalModelCallCount: totalModelCallCount,
            HeavyModelCallCount: heavyModelCallCount,
            FastModelCallCount: fastModelCallCount,
            UsedDeterministicPath: usedDeterministicPath,
            UsedDeterministicRecovery: false,
            BehaviorDurationMs: 0,
            ModeExecutionDurationMs: modeExecutionDurationMs,
            ResponseCompositionDurationMs: responseCompositionDurationMs,
            SelectionReasons: BuildSelectionReasons(selectionReason),
            EscalationJustifications: [],
            ResponseSelectionReason: selectionReason,
            ResponseFallbackUsed: responseFallbackUsed,
            ResponseRecoveryReason: responseRecoveryReason,
            ResponseUsedModelInvocation: responseUsedModelInvocation);
    }

    private static string CollapseReplyForStructuredResults(
        string replyText,
        CompanionStructuredResults? structuredResults)
    {
        if (structuredResults?.Type is not "places" || structuredResults.Items.Count == 0)
        {
            return replyText;
        }

        var firstUsefulLine = replyText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !IsNumberedResultLine(line));

        return string.IsNullOrWhiteSpace(firstUsefulLine) || IsActionOnlyAcknowledgement(firstUsefulLine)
            ? "I found these matching options:"
            : firstUsefulLine;
    }

    private static bool IsNumberedResultLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 3 || !char.IsDigit(trimmed[0]))
        {
            return false;
        }

        var index = 1;
        while (index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            index++;
        }

        return index < trimmed.Length - 1
               && (trimmed[index] == '.' || trimmed[index] == ')')
               && char.IsWhiteSpace(trimmed[index + 1]);
    }

    private static bool IsActionOnlyAcknowledgement(string line)
    {
        var normalized = line.Trim().ToLowerInvariant();
        return normalized.StartsWith("i'll look", StringComparison.Ordinal)
               || normalized.StartsWith("i will look", StringComparison.Ordinal)
               || normalized.StartsWith("i'll show", StringComparison.Ordinal)
               || normalized.StartsWith("i will show", StringComparison.Ordinal)
               || normalized.StartsWith("i can look", StringComparison.Ordinal)
               || normalized.StartsWith("i can show", StringComparison.Ordinal)
               || normalized.Contains("next helpful step", StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> MergeConversationIntelligenceMetadata(
        IReadOnlyDictionary<string, string> metadata,
        ConversationIntelligenceResult intelligence)
    {
        var merged = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["conversation_intelligence_json"] = JsonSerializer.Serialize(intelligence),
            ["conversation_phase"] = intelligence.ConversationPhase,
            ["user_emotional_state"] = intelligence.UserEmotionalState,
            ["conversation_next_action"] = intelligence.NextAction.Type,
            ["conversation_should_continue_task"] = intelligence.ShouldContinueTask.ToString(),
            ["conversation_should_clarify"] = intelligence.ShouldClarify.ToString(),
            ["conversation_should_execute_tool"] = intelligence.ShouldExecuteTool.ToString(),
            ["conversation_should_acknowledge_issue"] = intelligence.ShouldAcknowledgeIssue.ToString()
        };

        if (!string.IsNullOrWhiteSpace(intelligence.NextAction.Requirement))
        {
            merged["conversation_next_requirement"] = intelligence.NextAction.Requirement!;
        }

        if (!string.IsNullOrWhiteSpace(intelligence.NextAction.Target))
        {
            merged["conversation_next_target"] = intelligence.NextAction.Target!;
        }

        return merged;
    }

    private static ResponseCompositionRequest EnrichCompositionRequest(
        ResponseCompositionRequest request,
        ConversationIntelligenceResult? conversationIntelligence,
        TurnInterpretationV2? turnInterpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        ResultContextSnapshot? resultContext,
        CompanionResolvedAction? resolvedAction,
        PlaceFollowUpExecutionResult? followUpExecutionResult)
    {
        return request with
        {
            ConversationIntelligence = conversationIntelligence,
            TurnInterpretation = turnInterpretation,
            RetrievalPlan = retrievalPlan,
            ResultContext = resultContext,
            ResolvedAction = resolvedAction,
            FollowUpExecutionResult = followUpExecutionResult
        };
    }

    private static ConversationStateSnapshot ApplyResolvedActionToState(
        ConversationStateSnapshot state,
        CompanionResolvedAction action)
    {
        var constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase)
        {
            [ConversationConstraintKeys.SemanticFamily] = ConversationSemanticFamilies.Exploration
        };

        if (!string.IsNullOrWhiteSpace(action.PlaceQuery))
        {
            constraints[ConversationConstraintKeys.ExplorationCanonicalConcept] = action.PlaceQuery.Trim();
        }

        if (!string.IsNullOrWhiteSpace(action.LocationQuery))
        {
            constraints[ConversationConstraintKeys.ExplorationArea] = action.LocationQuery.Trim();
        }

        if (action.ExcludeConcepts.Count > 0)
        {
            constraints[ConversationConstraintKeys.ExplorationExcludeTypes] = string.Join('|', action.ExcludeConcepts);
        }

        if (action.Preferences.Count > 0)
        {
            constraints[ConversationConstraintKeys.ExplorationPreferences] = string.Join('|', action.Preferences);
        }

        if (action.TimeFilters.Count > 0)
        {
            constraints[ConversationConstraintKeys.ExplorationTime] = string.Join('|', action.TimeFilters);
        }

        return state with
        {
            ActiveMode = ConversationMode.Exploration,
            ModeCandidate = ConversationMode.Exploration,
            ReadinessLevel = ConversationReadinessLevel.R4_ToolReady,
            Constraints = constraints,
            MissingConstraints = [],
            PendingClarification = null,
            NeedsFollowUp = true,
            FollowUpBindingType = FollowUpBindingType.NewTopic
        };
    }

    private static IReadOnlyDictionary<string, string> MergeResolvedActionMetadata(
        IReadOnlyDictionary<string, string> clientMetadata,
        TurnInterpretationV2? turnInterpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        CompanionResolvedAction action)
    {
        if (action.Kind != CompanionActionKind.NewPlaceSearch)
        {
            return clientMetadata;
        }

        var effectivePlan = retrievalPlan ?? new PlaceRetrievalPlanV1(
            Version: "place_retrieval_plan_v1",
            SearchScope: "concept_first",
            PlannerAuthoritative: !string.IsNullOrWhiteSpace(action.PlaceQuery),
            BrandTerm: null,
            CanonicalConcept: action.PlaceQuery,
            SelectedDomain: null,
            IntentFamily: RealWorldIntentFamily.PlaceDiscovery,
            IncludedTypes: action.IncludeConcepts,
            ExcludedTypes: action.ExcludeConcepts,
            Preferences: action.Preferences,
            TimeFilters: action.TimeFilters,
            AudienceFilters: [],
            NearMeSemantic: string.Equals(action.LocationQuery, "near me", StringComparison.OrdinalIgnoreCase),
            RequiresLocation: !string.IsNullOrWhiteSpace(action.LocationQuery),
            ResolvedAreaHint: string.Equals(action.LocationQuery, "near me", StringComparison.OrdinalIgnoreCase)
                ? null
                : action.LocationQuery,
            ReasonCodes: ["resolved_action_synthesized_retrieval_plan"]);

        if (retrievalPlan is not null)
        {
            effectivePlan = retrievalPlan with
            {
                PlannerAuthoritative = retrievalPlan.PlannerAuthoritative || !string.IsNullOrWhiteSpace(action.PlaceQuery),
                CanonicalConcept = !string.IsNullOrWhiteSpace(retrievalPlan.CanonicalConcept)
                    ? retrievalPlan.CanonicalConcept
                    : action.PlaceQuery,
                IncludedTypes = retrievalPlan.IncludedTypes.Count > 0
                    ? retrievalPlan.IncludedTypes
                    : action.IncludeConcepts,
                ExcludedTypes = retrievalPlan.ExcludedTypes.Count > 0
                    ? retrievalPlan.ExcludedTypes
                    : action.ExcludeConcepts,
                Preferences = retrievalPlan.Preferences.Count > 0
                    ? retrievalPlan.Preferences
                    : action.Preferences,
                TimeFilters = retrievalPlan.TimeFilters.Count > 0
                    ? retrievalPlan.TimeFilters
                    : action.TimeFilters,
                NearMeSemantic = retrievalPlan.NearMeSemantic
                               || string.Equals(action.LocationQuery, "near me", StringComparison.OrdinalIgnoreCase),
                ResolvedAreaHint = !string.IsNullOrWhiteSpace(retrievalPlan.ResolvedAreaHint)
                    ? retrievalPlan.ResolvedAreaHint
                    : string.Equals(action.LocationQuery, "near me", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : action.LocationQuery,
                ReasonCodes = retrievalPlan.ReasonCodes
                    .Concat(["resolved_action_retrieval_plan_enriched"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        return TurnInterpretationMetadataMapper.Merge(clientMetadata, turnInterpretation, effectivePlan);
    }

    private async Task<OrchestratedConversationResult> ExecutePlaceFollowUpPathAsync(
        UserChatRequest request,
        string? contextSummary,
        ConversationStateSnapshot state,
        ResultContextReadResult resultContextReadResult,
        IReadOnlyList<string> existingWarnings,
        IReadOnlyList<string> intelligenceWarnings,
        CompanionResolvedAction resolvedAction,
        ConversationIntelligenceResult? conversationIntelligence,
        TurnInterpretationV2? turnInterpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        int intelligenceModelCallCount,
        int intelligenceFastModelCallCount,
        int intelligenceHeavyModelCallCount,
        CancellationToken cancellationToken)
    {
        var activeResult = resultContextReadResult.ActiveResultContext
                           ?? throw new InvalidOperationException("Place follow-up path requires an active result context.");
        await telemetry.TrackAsync(
            "places.legacy_follow_up_service.used",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["resolvedActionKind"] = resolvedAction.Kind.ToString(),
                ["reason"] = "v2_session_pool_unavailable_or_disabled"
            },
            cancellationToken);
        var followUpStopwatch = Stopwatch.StartNew();
        var followUpResult = await placeResultFollowUpService!.ExecuteAsync(
            resolvedAction,
            activeResult,
            cancellationToken);
        followUpStopwatch.Stop();

        await telemetry.TrackAsync(
            "chat.turn.tool_execution",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["tool"] = "place_result_follow_up",
                ["actionKind"] = resolvedAction.Kind.ToString(),
                ["candidateCount"] = followUpResult.Candidates.Count,
                ["evidenceQuality"] = followUpResult.EvidenceQuality,
                ["warningCount"] = followUpResult.Warnings.Count,
                ["durationMs"] = followUpStopwatch.ElapsedMilliseconds
            },
            cancellationToken);

        var nextState = state with
        {
            ActiveMode = ConversationMode.Exploration,
            ModeCandidate = ConversationMode.Exploration,
            ReadinessLevel = ConversationReadinessLevel.R4_ToolReady,
            NeedsFollowUp = true,
            FollowUpBindingType = FollowUpBindingType.Refine,
            ResultContextRef = new ConversationResultContextReference(
                ActiveResultSetId: activeResult.ResultSetId,
                BranchRootResultSetId: activeResult.BranchRootResultSetId,
                ActiveUntilUtc: activeResult.ActiveUntilUtc,
                ExpiresUtc: activeResult.ExpiresUtc),
            PendingClarification = null
        };
        var strategy = new ConversationTurnStrategyDecision(
            Strategy: resolvedAction.Kind == CompanionActionKind.ComparePreviousResults
                ? ConversationBehaviorStrategy.RefinePriorResultSet
                : ConversationBehaviorStrategy.ContinueRefinementThread,
            ModeCandidate: ConversationMode.Exploration,
            Readiness: new ReadinessTransition(state.ReadinessLevel, ConversationReadinessLevel.R4_ToolReady),
            Confidence: conversationIntelligence?.UserIntentConfidence ?? turnInterpretation?.Confidence ?? 0.8d,
            FollowUpBindingType: FollowUpBindingType.Refine,
            ClarificationQuestion: null,
            SuggestedOptions: [],
            ToolExecutionPermission: ToolExecutionPermission.EligibleIfGuardPasses,
            ReasonCodes: ["resolved_action_prior_result_follow_up"]);

        var compositionRequest = EnrichCompositionRequest(
            new ResponseCompositionRequest(
                ResponseType: resolvedAction.Kind == CompanionActionKind.ComparePreviousResults
                    ? ResponseCompositionType.Comparison
                    : ResponseCompositionType.ResultSummary,
                ToneDirective: ResolveToneDirective(conversationIntelligence),
                Strategy: strategy.Strategy,
                Mode: ConversationMode.Exploration,
                ReadinessLevel: nextState.ReadinessLevel,
                UserMessage: request.UserMessage,
                GroundedData: BuildFollowUpGroundedData(followUpResult),
                Constraints: nextState.Constraints,
                MissingConstraints: [],
                MaxLengthHint: 700,
                ClarificationQuestion: null,
                SuggestedOptions: []),
            conversationIntelligence,
            turnInterpretation,
            retrievalPlan,
            activeResult,
            resolvedAction,
            followUpResult);

        var responseCompositionStopwatch = Stopwatch.StartNew();
        var composed = await responseComposer.ComposeAsync(
            compositionRequest,
            request.CorrelationId,
            cancellationToken);
        responseCompositionStopwatch.Stop();
        var structuredResults = options.Value.Architecture.UnifiedPlaceCardsEnabled
            ? BuildFollowUpStructuredResults(followUpResult, activeResult, resolvedAction)
            : null;

        var reply = new UserChatResponse(
            ReplyText: CollapseReplyForStructuredResults(
                SanitizeUserFacingReply(composed.ReplyText, structuredResults, resolvedAction),
                structuredResults),
            ModelUsed: composed.ModelUsed,
            ReasoningClass: composed.ReasoningClass,
            SuggestedStructuredStateUpdates: BuildSuggestedStateUpdates(
                nextState,
                activeResult,
                strategy,
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                composed.SuggestedStructuredStateUpdates),
            ReferencedContextSummary: contextSummary,
            Warnings: existingWarnings
                .Concat(resultContextReadResult.ReasonCodes)
                .Concat(intelligenceWarnings)
                .Concat(resolvedAction.Warnings)
                .Concat(followUpResult.Warnings)
                .Concat(composed.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FollowUpIntentHints: composed.FollowUpIntentHints
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Succeeded: true,
            FailureReason: null,
            StructuredResults: structuredResults);

        await TrackResolvedActionExecutionDecisionAsync(
            request,
            resolvedAction,
            executedDirectly: true,
            fellThroughToBehaviorEngine: false,
            hasResultContext: true,
            structuredResultsReturned: structuredResults?.Items.Count > 0,
            structuredResultCount: structuredResults?.Items.Count ?? 0,
            reason: "resolved_action_prior_result_follow_up_completed",
            cancellationToken);

        return new OrchestratedConversationResult(
            Response: reply,
            State: nextState,
            AssistantRoute: new AIModelRoute(
                TaskType: AITaskType.ResponseComposition,
                ModelClass: composed.ReasoningClass,
                Model: composed.ModelUsed,
                Deployment: composed.DeploymentUsed,
                IsFallback: composed.ModelUsed.StartsWith("deterministic", StringComparison.OrdinalIgnoreCase),
                Reason: "place_follow_up_response",
                Notes: []),
            TotalModelCallCount: intelligenceModelCallCount + (composed.UsedModelInvocation ? 1 : 0),
            HeavyModelCallCount: intelligenceHeavyModelCallCount,
            FastModelCallCount: intelligenceFastModelCallCount + (composed.UsedModelInvocation ? 1 : 0),
            UsedDeterministicPath: composed.UsedDeterministicPath,
            UsedDeterministicRecovery: composed.UsedDeterministicPath && composed.FallbackUsed && composed.UsedModelInvocation,
            BehaviorDurationMs: 0,
            ModeExecutionDurationMs: followUpStopwatch.ElapsedMilliseconds,
            ResponseCompositionDurationMs: responseCompositionStopwatch.ElapsedMilliseconds,
            SelectionReasons: BuildSelectionReasons("resolved_action_prior_result_follow_up", null, composed.SelectionReason),
            EscalationJustifications: [],
            ResponseSelectionReason: composed.SelectionReason,
            ResponseFallbackUsed: composed.FallbackUsed,
            ResponseRecoveryReason: composed.RecoveryReason,
            ResponseUsedModelInvocation: composed.UsedModelInvocation);
    }

    private static ResponseToneDirective ResolveToneDirective(ConversationIntelligenceResult? intelligence)
    {
        return intelligence?.ResponseStyle.Tone switch
        {
            "concise" or "direct" => ResponseToneDirective.Concise,
            "apologetic" or "reassuring" or "warm" => ResponseToneDirective.Supportive,
            _ => ResponseToneDirective.Neutral
        };
    }

    private static GroundedDataEnvelope BuildFollowUpGroundedData(PlaceFollowUpExecutionResult result)
    {
        return new GroundedDataEnvelope(
            Entities: result.Candidates
                .Select(candidate => new ConversationSuggestedEntity(
                    EntityId: candidate.PlaceId,
                    Label: candidate.Name,
                    Rank: candidate.NewRank))
                .ToArray(),
            SummaryFacts: result.Candidates
                .Select(candidate => new GroundedDataPoint(
                    candidate.Name,
                    BuildFollowUpFact(candidate)))
                .ToArray(),
            Warnings: result.Warnings
                .Concat(result.Uncertainties.Select(static value => $"uncertainty:{value}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static string BuildFollowUpFact(PlaceFollowUpCandidate candidate)
    {
        if (candidate.MatchedReasons.Any(static reason => reason.Contains("parking", StringComparison.OrdinalIgnoreCase)))
        {
            return "parking evidence found from available place details";
        }

        if (candidate.MissingEvidence.Any(static reason => reason.Contains("parking", StringComparison.OrdinalIgnoreCase)
                                                         || reason.Contains("confirmed", StringComparison.OrdinalIgnoreCase)))
        {
            return "parking evidence unclear";
        }

        return "matched the requested refinement using available place details";
    }

    private static CompanionStructuredResults? BuildFollowUpStructuredResults(
        PlaceFollowUpExecutionResult followUpResult,
        ResultContextSnapshot resultContext,
        CompanionResolvedAction action)
    {
        if (followUpResult.Candidates.Count == 0 || resultContext.SuggestedEntities.Count == 0)
        {
            return null;
        }

        var entityById = resultContext.SuggestedEntities
            .Where(static entity => !string.IsNullOrWhiteSpace(entity.EntityId))
            .GroupBy(static entity => entity.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var maxCount = action.Preferences.Contains("single_best", StringComparer.OrdinalIgnoreCase)
            ? 1
            : 8;
        var cards = followUpResult.Candidates
            .Where(candidate => entityById.ContainsKey(candidate.PlaceId))
            .Take(maxCount)
            .Select(candidate => BuildFollowUpCard(candidate, entityById[candidate.PlaceId]))
            .Where(static card => card is not null)
            .Select(static card => card!)
            .ToArray();

        return cards.Length == 0
            ? null
            : new CompanionStructuredResults("places", cards);
    }

    private static CompanionPlaceCardResult? BuildFollowUpCard(
        PlaceFollowUpCandidate candidate,
        ResultContextEntity entity)
    {
        var attributes = entity.Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var photoUrls = ReadStringList(attributes, "photo_urls");
        var photoUrl = ReadString(attributes, "photo_url") ?? photoUrls.FirstOrDefault();
        if (photoUrls.Count == 0 && !string.IsNullOrWhiteSpace(photoUrl))
        {
            photoUrls = [photoUrl!];
        }

        return new CompanionPlaceCardResult(
            Id: candidate.PlaceId,
            Name: candidate.Name,
            DistanceMeters: ReadDouble(attributes, "distance_meters"),
            PhotoUrl: photoUrl,
            PhotoUrls: photoUrls,
            FormattedAddress: ReadString(attributes, "formatted_address"),
            ShortFormattedAddress: ReadString(attributes, "short_address"),
            Rating: ReadDouble(attributes, "rating"),
            OpenNow: ReadBool(attributes, "open_now"),
            PriceLevel: ReadString(attributes, "price_level"),
            WebsiteUrl: ReadString(attributes, "website_url"),
            Category: entity.Category,
            PrimaryTypeDisplayName: ReadString(attributes, "primary_type_display_name"),
            ClosesInMinutes: null,
            OpensInMinutes: ReadInt(attributes, "opens_in_minutes"),
            PhoneNumber: ReadString(attributes, "phone_number"),
            MenuUrl: ReadString(attributes, "menu_url"),
            GoogleMapsUri: ReadString(attributes, "google_maps_uri") ?? entity.StableReference,
            Latitude: ReadDouble(attributes, "latitude"),
            Longitude: ReadDouble(attributes, "longitude"));
    }

    private static string? ReadString(IReadOnlyDictionary<string, string> attributes, string key)
    {
        return attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static IReadOnlyList<string> ReadStringList(IReadOnlyDictionary<string, string> attributes, string key)
    {
        var value = ReadString(attributes, key);
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, string> attributes, string key)
    {
        return attributes.TryGetValue(key, out var raw)
               && double.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string> attributes, string key)
    {
        return attributes.TryGetValue(key, out var raw)
               && int.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, string> attributes, string key)
    {
        return attributes.TryGetValue(key, out var raw)
               && bool.TryParse(raw, out var parsed)
            ? parsed
            : null;
    }

    private static string BuildCloseReply(string userMessage)
    {
        var normalized = userMessage.Trim().ToLowerInvariant();
        if (normalized.Contains("stop", StringComparison.Ordinal)
            || normalized.Contains("never mind", StringComparison.Ordinal)
            || normalized.Contains("nevermind", StringComparison.Ordinal))
        {
            return "No problem - I'll leave it there.";
        }

        return "No problem.";
    }

    private static string SanitizeUserFacingReply(
        string replyText,
        CompanionStructuredResults? structuredResults,
        CompanionResolvedAction? action)
    {
        if (!ContainsInternalSignal(replyText))
        {
            return replyText;
        }

        if (structuredResults?.Type == "places" && structuredResults.Items.Count > 0)
        {
            if (string.Equals(action?.Requirement, "parking", StringComparison.OrdinalIgnoreCase))
            {
                return "These are the strongest parking matches I can infer from available place data:";
            }

            if (action?.Kind == CompanionActionKind.SortPreviousResults)
            {
                return action.Preferences.Contains("single_best", StringComparer.OrdinalIgnoreCase)
                    ? "The closest match is:"
                    : "I sorted those results by distance:";
            }

            if (action?.Kind is CompanionActionKind.FilterPreviousResults or CompanionActionKind.EnrichPreviousResults)
            {
                return "I filtered those results:";
            }

            return "I found these matching options:";
        }

        return "I used the available place details to refine those results.";
    }

    private static bool ContainsInternalSignal(string value)
    {
        return value.Contains("_signal", StringComparison.OrdinalIgnoreCase)
               || value.Contains("concept_match:", StringComparison.OrdinalIgnoreCase)
               || value.Contains("excluded_concept:", StringComparison.OrdinalIgnoreCase)
               || value.Contains("place_follow_up_", StringComparison.OrdinalIgnoreCase)
               || value.Contains("resolved_action_", StringComparison.OrdinalIgnoreCase)
               || value.Contains("score:", StringComparison.OrdinalIgnoreCase)
               || value.Contains("score ", StringComparison.OrdinalIgnoreCase)
               || value.Contains("ReasonCodes", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, object?> BuildResolvedActionTelemetry(
        string correlationId,
        CompanionResolvedAction action,
        bool hasResultContext)
    {
        return new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId,
            ["kind"] = action.Kind.ToString(),
            ["requiresToolExecution"] = action.RequiresToolExecution,
            ["requiresClarification"] = action.RequiresClarification,
            ["hasResultContext"] = hasResultContext,
            ["placeQuery"] = action.PlaceQuery,
            ["locationQuery"] = action.LocationQuery,
            ["requirement"] = action.Requirement,
            ["sortGoal"] = action.SortGoal,
            ["includeConceptCount"] = action.IncludeConcepts.Count,
            ["excludeConceptCount"] = action.ExcludeConcepts.Count,
            ["warningCount"] = action.Warnings.Count
        };
    }

    private async Task TrackResolvedActionExecutionDecisionAsync(
        UserChatRequest request,
        CompanionResolvedAction action,
        bool executedDirectly,
        bool fellThroughToBehaviorEngine,
        bool hasResultContext,
        bool structuredResultsReturned,
        int structuredResultCount,
        string reason,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "chat.turn.companion_action_execution_decision",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["resolvedActionKind"] = action.Kind.ToString(),
                ["requiresToolExecution"] = action.RequiresToolExecution,
                ["requiresClarification"] = action.RequiresClarification,
                ["executedDirectly"] = executedDirectly,
                ["fellThroughToBehaviorEngine"] = fellThroughToBehaviorEngine,
                ["placeQuery"] = action.PlaceQuery,
                ["locationQuery"] = action.LocationQuery,
                ["hasGps"] = HasGpsMetadata(request.Metadata),
                ["hasActiveResultContext"] = hasResultContext,
                ["requirement"] = action.Requirement,
                ["sortGoal"] = action.SortGoal,
                ["structuredResultsReturned"] = structuredResultsReturned,
                ["structuredResultCount"] = structuredResultCount,
                ["reason"] = reason
            },
            cancellationToken);
    }

    private static string ResolveActionFallthroughReason(
        CompanionResolvedAction action,
        ResultContextSnapshot? activeResultContext)
    {
        if (!action.RequiresToolExecution)
        {
            return "not_tool_action";
        }

        if ((action.Kind is CompanionActionKind.FilterPreviousResults
                or CompanionActionKind.SortPreviousResults
                or CompanionActionKind.EnrichPreviousResults
                or CompanionActionKind.ComparePreviousResults)
            && activeResultContext is null)
        {
            return "missing_result_context";
        }

        return "guard_or_feature_flag";
    }

    private static bool HasGpsMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        return metadata is not null
               && metadata.ContainsKey(CompanionLocationMetadataKeys.Latitude)
               && metadata.ContainsKey(CompanionLocationMetadataKeys.Longitude);
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
            var latestState = await conversationStateService!.GetLatestStateAsync(
                request.UserId!.Value,
                conversationThreadId,
                cancellationToken);
            var mergedState = MergeIncomingStatePatch(
                latestState?.State,
                request.State);
            await conversationStateService!.SaveSnapshotAsync(
                request.UserId!.Value,
                conversationThreadId,
                mergedState,
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

        try
        {
            await conversationStateService!.SaveSnapshotAsync(
                userId,
                conversationThreadId,
                state,
                ConversationStateSnapshotReason.AssistantTurn,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Assistant response succeeded but conversation state snapshot persistence failed threadId={ThreadId}",
                conversationThreadId);
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

    private ConversationStateSnapshot MergeIncomingStatePatch(
        ConversationStateSnapshot? persistedState,
        ConversationStateSnapshot incomingPatch)
    {
        var baseState = NormalizeIncomingState(persistedState);
        var patch = NormalizeIncomingState(incomingPatch);

        var mergedConstraints = new Dictionary<string, string>(baseState.Constraints, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in patch.Constraints)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            mergedConstraints[pair.Key.Trim()] = pair.Value.Trim();
        }

        return ApplyConversationMemoryTtl(baseState with
        {
            ActiveTopic = !string.IsNullOrWhiteSpace(patch.ActiveTopic) ? patch.ActiveTopic : baseState.ActiveTopic,
            UserIntent = !string.IsNullOrWhiteSpace(patch.UserIntent) ? patch.UserIntent : baseState.UserIntent,
            Constraints = mergedConstraints,
            Summaries = patch.Summaries.Count > 0 ? patch.Summaries : baseState.Summaries,
            BudgetPreference = !string.IsNullOrWhiteSpace(patch.BudgetPreference) ? patch.BudgetPreference : baseState.BudgetPreference,
            LocationPreference = !string.IsNullOrWhiteSpace(patch.LocationPreference) ? patch.LocationPreference : baseState.LocationPreference,
            MerchantInvestigationSubject = !string.IsNullOrWhiteSpace(patch.MerchantInvestigationSubject) ? patch.MerchantInvestigationSubject : baseState.MerchantInvestigationSubject,
            RecentConclusions = patch.RecentConclusions.Count > 0 ? patch.RecentConclusions : baseState.RecentConclusions
        });
    }

    private static ConversationStateSnapshot ApplyInterpretationToState(
        ConversationStateSnapshot state,
        TurnInterpretationV2 interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        int ttlMinutes)
    {
        var constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(Math.Max(5, ttlMinutes));

        if (interpretation.IntentFamily is TurnInterpretationIntentFamily.PlaceDiscovery or TurnInterpretationIntentFamily.Mixed)
        {
            constraints[ConversationConstraintKeys.SemanticFamily] = ConversationSemanticFamilies.Exploration;
        }
        else if (interpretation.IntentFamily == TurnInterpretationIntentFamily.FinancialGuidance)
        {
            constraints[ConversationConstraintKeys.SemanticFamily] = ConversationSemanticFamilies.Financial;
        }

        var includeTypes = retrievalPlan?.IncludedTypes ?? interpretation.PlacePlan.IncludeTypes;
        if (includeTypes.Count > 0)
        {
            constraints[ConversationConstraintKeys.ExplorationPlaceTypes] = string.Join('|', includeTypes);
        }

        var preferences = retrievalPlan?.Preferences ?? interpretation.PlacePlan.Preferences;
        if (preferences.Count > 0)
        {
            constraints[ConversationConstraintKeys.ExplorationPreferences] = string.Join('|', preferences);
        }

        var timeFilters = retrievalPlan?.TimeFilters ?? interpretation.PlacePlan.TimeFilters;
        if (timeFilters.Count > 0)
        {
            constraints[ConversationConstraintKeys.ExplorationTime] = string.Join('|', timeFilters);
        }

        var resolvedArea = retrievalPlan?.ResolvedAreaHint
                           ?? interpretation.LocationPlan.ResolvedAreaHint
                           ?? interpretation.LocationPlan.ExplicitAreaText;
        if (!string.IsNullOrWhiteSpace(resolvedArea))
        {
            constraints[ConversationConstraintKeys.ExplorationArea] = resolvedArea.Trim();
            constraints[ConversationConstraintKeys.ExplorationAreaLastUsedUtc] = now.UtcDateTime.ToString("O");
            constraints[ConversationConstraintKeys.ExplorationAreaExpiresUtc] = expires.UtcDateTime.ToString("O");
        }
        else if (interpretation.LocationPlan.NearMeSemantic)
        {
            constraints[ConversationConstraintKeys.ExplorationArea] = "near_me";
        }

        if (!string.IsNullOrWhiteSpace(retrievalPlan?.BrandTerm))
        {
            constraints[ConversationConstraintKeys.ExplorationBrandTerm] = retrievalPlan.BrandTerm!;
        }

        if (!string.IsNullOrWhiteSpace(retrievalPlan?.CanonicalConcept))
        {
            constraints[ConversationConstraintKeys.ExplorationCanonicalConcept] = retrievalPlan.CanonicalConcept!;
        }

        return state with
        {
            Constraints = constraints
        };
    }

    private ConversationStateSnapshot ApplyConversationMemoryTtl(ConversationStateSnapshot state)
    {
        var ttlMinutes = Math.Max(1, options.Value.Architecture.ExplorationConstraintTtlMinutes);
        var nowUtc = DateTimeOffset.UtcNow;
        var thresholdUtc = nowUtc.AddMinutes(-ttlMinutes);

        var constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase);
        var contextStamp = constraints.TryGetValue(ConversationConstraintKeys.ExplorationContextLastUsedUtc, out var rawStamp)
            ? rawStamp
            : null;
        var staleExplorationContext = TryParseUtc(contextStamp, out var parsedStamp)
                                      && parsedStamp < thresholdUtc;

        if (staleExplorationContext)
        {
            constraints.Remove(ConversationConstraintKeys.ExplorationSubtype);
            constraints.Remove(ConversationConstraintKeys.ExplorationArea);
            constraints.Remove(ConversationConstraintKeys.ExplorationPlaceTypes);
            constraints.Remove(ConversationConstraintKeys.ExplorationPreferences);
            constraints.Remove(ConversationConstraintKeys.ExplorationTime);
            constraints.Remove(ConversationConstraintKeys.ExplorationContextLastUsedUtc);
            constraints.Remove(ConversationConstraintKeys.ExplorationAreaLastUsedUtc);
            constraints.Remove(ConversationConstraintKeys.ExplorationAreaExpiresUtc);
            constraints.Remove(ConversationConstraintKeys.ExplorationBrandTerm);
            constraints.Remove(ConversationConstraintKeys.ExplorationCanonicalConcept);
        }

        var pendingClarification = state.PendingClarification;
        if (pendingClarification?.CreatedAtUtc is DateTimeOffset pendingCreatedUtc
            && pendingCreatedUtc < thresholdUtc)
        {
            pendingClarification = null;
        }

        return state with
        {
            Constraints = constraints,
            PendingClarification = pendingClarification
        };
    }

    private ConversationStateSnapshot RefreshExplorationContextUsage(ConversationStateSnapshot state)
    {
        var hasExplorationContext = state.ModeCandidate == ConversationMode.Exploration
                                    || state.ActiveMode == ConversationMode.Exploration
                                    || state.Constraints.ContainsKey(ConversationConstraintKeys.ExplorationArea)
                                    || state.Constraints.ContainsKey(ConversationConstraintKeys.ExplorationPlaceTypes)
                                    || state.Constraints.ContainsKey(ConversationConstraintKeys.ExplorationTime);
        if (!hasExplorationContext)
        {
            return state;
        }

        var constraints = new Dictionary<string, string>(state.Constraints, StringComparer.OrdinalIgnoreCase)
        {
            [ConversationConstraintKeys.ExplorationContextLastUsedUtc] = DateTimeOffset.UtcNow.UtcDateTime.ToString("O")
        };
        if (constraints.TryGetValue(ConversationConstraintKeys.ExplorationArea, out var area)
            && !string.IsNullOrWhiteSpace(area)
            && !string.Equals(area, "near_me", StringComparison.OrdinalIgnoreCase))
        {
            var now = DateTimeOffset.UtcNow;
            constraints[ConversationConstraintKeys.ExplorationAreaLastUsedUtc] = now.UtcDateTime.ToString("O");
            constraints[ConversationConstraintKeys.ExplorationAreaExpiresUtc] = now
                .AddMinutes(Math.Max(5, options.Value.Architecture.ExplorationConstraintTtlMinutes))
                .UtcDateTime
                .ToString("O");
        }

        return state with
        {
            Constraints = constraints
        };
    }

    private static bool TryParseUtc(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
               && DateTimeOffset.TryParse(value, out result);
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
                LoopGuards = state.LoopGuards ?? new ConversationLoopGuards(),
                PendingClarification = state.PendingClarification
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

        if (state.PendingClarification is not null)
        {
            updates["pending_clarification_slot"] = state.PendingClarification.Slot.ToString();
            if (!string.IsNullOrWhiteSpace(state.PendingClarification.PromptIntent))
            {
                updates["pending_clarification_prompt_intent"] = state.PendingClarification.PromptIntent!;
            }

            if (!string.IsNullOrWhiteSpace(state.PendingClarification.KnownPlaceTypes))
            {
                updates["pending_clarification_known_place_types"] = state.PendingClarification.KnownPlaceTypes!;
            }

            if (!string.IsNullOrWhiteSpace(state.PendingClarification.KnownArea))
            {
                updates["pending_clarification_known_area"] = state.PendingClarification.KnownArea!;
            }

            if (!string.IsNullOrWhiteSpace(state.PendingClarification.KnownTime))
            {
                updates["pending_clarification_known_time"] = state.PendingClarification.KnownTime!;
            }

            if (state.PendingClarification.CreatedAtUtc.HasValue)
            {
                updates["pending_clarification_created_utc"] = state.PendingClarification.CreatedAtUtc.Value.UtcDateTime.ToString("O");
            }
        }
        else
        {
            updates["pending_clarification_clear"] = "true";
        }

        if (resultContext?.ResultSetId is Guid resultSetId)
        {
            updates["active_result_set_id"] = resultSetId.ToString("D");
        }
        else if (state.FollowUpBindingType is FollowUpBindingType.None or FollowUpBindingType.NewTopic)
        {
            updates["active_result_set_clear"] = "true";
            updates["selected_entity_clear"] = "true";
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
        bool UsedDeterministicRecovery,
        long BehaviorDurationMs,
        long ModeExecutionDurationMs,
        long ResponseCompositionDurationMs,
        IReadOnlyList<string> SelectionReasons,
        IReadOnlyList<string> EscalationJustifications,
        string ResponseSelectionReason,
        bool ResponseFallbackUsed,
        string? ResponseRecoveryReason,
        bool ResponseUsedModelInvocation);
}
