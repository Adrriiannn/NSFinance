using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.AI.DTOs;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.AI.Endpoints;

public static class GetChatThreadEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid threadId,
        int? take,
        ICurrentUserProvider currentUserProvider,
        IConversationThreadService conversationThreadService,
        IConversationMessageService conversationMessageService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var thread = await conversationThreadService.GetThreadAsync(userId, threadId, cancellationToken);
        if (thread is null)
        {
            return Results.NotFound(new ApiErrorResponse("Conversation thread not found for user.", "thread_not_found"));
        }

        var safeTake = Math.Clamp(take ?? 80, 1, 200);
        var messages = await conversationMessageService.GetRecentMessagesAsync(userId, threadId, safeTake, cancellationToken);
        var response = new ChatThreadDetailResponse(
            Thread: new ChatThreadSummaryDto(
                ThreadId: thread.Id,
                Title: thread.Title,
                Status: thread.Status.ToString(),
                StartedUtc: thread.StartedUtc,
                LastMessageUtc: thread.LastMessageUtc,
                LastContextRefreshUtc: thread.LastContextRefreshUtc,
                ActiveSummaryVersion: thread.ActiveSummaryVersion),
            Messages: messages
                .Select(message => new ChatMessageDto(
                    MessageId: message.Id,
                    TurnId: message.ConversationTurnId,
                    Role: message.Role.ToString(),
                    Content: message.Content,
                    MessageOrder: message.MessageOrder,
                    Topic: message.Topic,
                    ModelUsed: message.ModelUsed,
                    TaskType: message.TaskType,
                    IsResolved: message.IsResolved,
                    CreatedUtc: message.CreatedUtc))
                .ToArray());

        return Results.Ok(response);
    }
}
