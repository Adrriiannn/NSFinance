using NSFinance.Api.Modules.AI.DTOs;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.AI.Endpoints;

public static class GetChatThreadsEndpoint
{
    public static async Task<IResult> HandleAsync(
        int? limit,
        ICurrentUserProvider currentUserProvider,
        IConversationThreadService conversationThreadService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var safeLimit = Math.Clamp(limit ?? 20, 1, 100);
        var threads = await conversationThreadService.GetRecentThreadsAsync(userId, safeLimit, cancellationToken);
        var response = threads
            .Select(thread => new ChatThreadSummaryDto(
                ThreadId: thread.Id,
                Title: thread.Title,
                Status: thread.Status.ToString(),
                StartedUtc: thread.StartedUtc,
                LastMessageUtc: thread.LastMessageUtc,
                LastContextRefreshUtc: thread.LastContextRefreshUtc,
                ActiveSummaryVersion: thread.ActiveSummaryVersion))
            .ToArray();

        return Results.Ok(response);
    }
}
