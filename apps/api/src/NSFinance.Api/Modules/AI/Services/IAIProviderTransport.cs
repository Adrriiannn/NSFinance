namespace NSFinance.Api.Modules.AI.Services;

public interface IAIProviderTransport
{
    AIProviderKind Kind { get; }

    Task<AIResponse> SendAsync(AIRequest request, AIModelRoute route, CancellationToken cancellationToken);
}
