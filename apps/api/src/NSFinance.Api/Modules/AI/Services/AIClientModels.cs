using System.Collections.ObjectModel;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record AIMessage(
    AIMessageRole Role,
    string Content,
    DateTime TimestampUtc)
{
    public static AIMessage System(string content) => new(AIMessageRole.System, content, DateTime.UtcNow);
    public static AIMessage Developer(string content) => new(AIMessageRole.Developer, content, DateTime.UtcNow);
    public static AIMessage User(string content) => new(AIMessageRole.User, content, DateTime.UtcNow);
    public static AIMessage Assistant(string content) => new(AIMessageRole.Assistant, content, DateTime.UtcNow);
}

public sealed record AIRequest(
    AITaskType TaskType,
    AIModelClass PreferredModelClass,
    IReadOnlyList<AIMessage> Messages,
    string? SystemInstructions,
    string? StructuredOutputSchemaName,
    double? Temperature,
    int? MaxOutputTokens,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static AIRequest Create(
        AITaskType taskType,
        AIModelClass preferredModelClass,
        IReadOnlyList<AIMessage> messages,
        string correlationId,
        string? systemInstructions = null,
        string? structuredOutputSchemaName = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new AIRequest(
            taskType,
            preferredModelClass,
            messages,
            systemInstructions,
            structuredOutputSchemaName,
            temperature,
            maxOutputTokens,
            correlationId,
            metadata ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
    }
}

public sealed record AIResponse(
    string? Content,
    string? StructuredPayloadJson,
    string? FinishReason,
    string Provider,
    string Model,
    string Deployment,
    int? InputTokenEstimate,
    int? OutputTokenEstimate,
    long LatencyMs,
    bool WasMocked,
    string? RawDiagnostics,
    bool Succeeded,
    string? FailureReason)
{
    public static AIResponse Failed(string provider, string model, string deployment, string failureReason, bool wasMocked = false)
    {
        return new AIResponse(
            Content: null,
            StructuredPayloadJson: null,
            FinishReason: null,
            Provider: provider,
            Model: model,
            Deployment: deployment,
            InputTokenEstimate: null,
            OutputTokenEstimate: null,
            LatencyMs: 0,
            WasMocked: wasMocked,
            RawDiagnostics: null,
            Succeeded: false,
            FailureReason: failureReason);
    }
}
