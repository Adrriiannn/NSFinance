namespace NSFinance.Api.Modules.AI.Services;

public interface IAIClient
{
    Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken);
}
