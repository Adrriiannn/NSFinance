namespace NSFinance.Api.Modules.AI.Services;

public interface IUserChatOrchestrator
{
    Task<UserChatResponse> ExecuteAsync(UserChatRequest request, CancellationToken cancellationToken);
}
