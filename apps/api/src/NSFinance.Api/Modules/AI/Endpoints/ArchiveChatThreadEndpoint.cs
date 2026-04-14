using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.AI.DTOs;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.AI.Endpoints;

public static class ArchiveChatThreadEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid threadId,
        ICurrentUserProvider currentUserProvider,
        IConversationThreadService conversationThreadService,
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

        await conversationThreadService.ArchiveThreadAsync(userId, threadId, cancellationToken);
        return Results.Ok(new ArchiveChatThreadResponse(threadId, "Archived"));
    }
}
