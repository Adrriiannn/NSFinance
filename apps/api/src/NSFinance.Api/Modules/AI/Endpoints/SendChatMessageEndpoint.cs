using Microsoft.Extensions.Options;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.AI.DTOs;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.AI.Validators;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Endpoints;

public static class SendChatMessageEndpoint
{
    public static async Task<IResult> HandleAsync(
        SendChatMessageRequest request,
        ICurrentUserProvider currentUserProvider,
        IUserChatOrchestrator userChatOrchestrator,
        IConversationThreadService conversationThreadService,
        IOptions<AIIntegrationOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("AI.SendChatMessageEndpoint");
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var errors = AiEndpointRequestValidators.ValidateSendChatRequest(request, options.Value);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (request.ConversationThreadId.HasValue)
        {
            var existingThread = await conversationThreadService.GetThreadAsync(userId, request.ConversationThreadId.Value, cancellationToken);
            if (existingThread is null)
            {
                return Results.NotFound(new ApiErrorResponse("Conversation thread not found for user.", "thread_not_found"));
            }
        }

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();

        var mapped = new UserChatRequest(
            UserMessage: request.Message.Trim(),
            RecentTurns: MapRecentTurns(request.RecentTurns),
            State: MapState(request.State),
            CorrelationId: correlationId,
            Metadata: request.Metadata,
            ClientRequestId: request.ClientRequestId.Trim(),
            UserId: userId,
            ConversationThreadId: request.ConversationThreadId,
            UsePersistentMemory: request.RequirePersistentMemory,
            AllowTransientFallbackOnPersistentFailure: request.RequirePersistentMemory
                ? false
                : request.AllowFallbackOnPersistentFailure);

        UserChatResponse result;
        try
        {
            result = await userChatOrchestrator.ExecuteAsync(mapped, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Chat endpoint orchestration failed userId={UserId} correlationId={CorrelationId} threadId={ThreadId} clientRequestId={ClientRequestId}",
                userId,
                correlationId,
                request.ConversationThreadId,
                request.ClientRequestId);
            return Results.Json(
                new ApiErrorResponse("Chat service is currently unavailable.", "chat_orchestration_failed"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var fallbackUsed = result.Warnings.Any(warning =>
            warning.Contains("fallback", StringComparison.OrdinalIgnoreCase)
            || warning.Contains("persistent_memory_unavailable", StringComparison.OrdinalIgnoreCase)
            || warning.Contains("structured_parse_failed", StringComparison.OrdinalIgnoreCase)
            || warning.Contains("recovery", StringComparison.OrdinalIgnoreCase));

        var response = new SendChatMessageResponse(
            ConversationThreadId: result.ConversationThreadId,
            TurnId: result.ConversationTurnId,
            Status: ResolveStatus(result),
            Message: result.ReplyText,
            ModelUsed: result.ModelUsed,
            ReasoningClass: result.ReasoningClass.ToString(),
            Succeeded: result.Succeeded,
            Deduped: result.IsDuplicateRequest,
            InProgress: result.IsTurnInProgress,
            FallbackUsed: fallbackUsed,
            FailureCode: result.FailureReason,
            FailureReason: result.FailureReason,
            SuggestedStateUpdates: result.SuggestedStructuredStateUpdates,
            Warnings: result.Warnings,
            FollowUpIntentHints: result.FollowUpIntentHints,
            ContextSummary: result.ReferencedContextSummary);

        logger.LogInformation(
            "AI chat endpoint completed userId={UserId} threadId={ThreadId} turnId={TurnId} requestId={RequestId} deduped={Deduped} inProgress={InProgress} succeeded={Succeeded} taskClass={TaskClass} model={Model} fallbackUsed={FallbackUsed} failure={Failure} warnings={Warnings}",
            userId,
            response.ConversationThreadId,
            response.TurnId,
            request.ClientRequestId,
            response.Deduped,
            response.InProgress,
            response.Succeeded,
            response.ReasoningClass,
            response.ModelUsed,
            response.FallbackUsed,
            response.FailureReason,
            string.Join(',', response.Warnings));

        if (response.InProgress)
        {
            return Results.Json(response, statusCode: StatusCodes.Status202Accepted);
        }

        if (response.Succeeded)
        {
            return Results.Ok(response);
        }

        var statusCode = ResolveFailureStatusCode(response.FailureCode);
        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            return Results.Json(response, statusCode: statusCode);
        }

        if (statusCode == StatusCodes.Status409Conflict)
        {
            return Results.Conflict(response);
        }

        return Results.Json(response, statusCode: statusCode);
    }

    private static IReadOnlyList<UserChatTurn> MapRecentTurns(IReadOnlyList<ChatTurnDto>? turns)
    {
        if (turns is null || turns.Count == 0)
        {
            return [];
        }

        var mapped = new List<UserChatTurn>(turns.Count);
        foreach (var turn in turns)
        {
            var role = turn.Role?.Trim();
            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            if (!Enum.TryParse<AIMessageRole>(role, ignoreCase: true, out var parsedRole))
            {
                continue;
            }

            mapped.Add(new UserChatTurn(
                Role: parsedRole,
                Content: turn.Content ?? string.Empty,
                TimestampUtc: turn.TimestampUtc ?? DateTime.UtcNow,
                Topic: turn.Topic,
                IsResolved: turn.IsResolved));
        }

        return mapped;
    }

    private static NSFinance.Api.Modules.AI.Services.ConversationStateSnapshot? MapState(ChatStateDto? state)
    {
        if (state is null)
        {
            return null;
        }

        return new NSFinance.Api.Modules.AI.Services.ConversationStateSnapshot(
            ActiveTopic: state.ActiveTopic,
            UserIntent: state.UserIntent,
            Constraints: state.Constraints ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Summaries: state.Summaries ?? [],
            BudgetPreference: state.BudgetPreference,
            LocationPreference: state.LocationPreference,
            MerchantInvestigationSubject: state.MerchantInvestigationSubject,
            RecentConclusions: state.RecentConclusions ?? []);
    }

    private static string ResolveStatus(UserChatResponse response)
    {
        if (response.IsTurnInProgress)
        {
            return "in_progress";
        }

        if (response.Succeeded)
        {
            return "completed";
        }

        return response.TurnStatus?.ToString().ToLowerInvariant() ?? "failed";
    }

    private static int ResolveFailureStatusCode(string? failureCode)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            return StatusCodes.Status500InternalServerError;
        }

        if (failureCode.Contains("not_found", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status404NotFound;
        }

        if (failureCode.Contains("in_progress", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status409Conflict;
        }

        if (failureCode.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("provider", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("circuit", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status503ServiceUnavailable;
        }

        if (failureCode.Contains("validation", StringComparison.OrdinalIgnoreCase)
            || failureCode.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status400BadRequest;
        }

        return StatusCodes.Status422UnprocessableEntity;
    }
}
