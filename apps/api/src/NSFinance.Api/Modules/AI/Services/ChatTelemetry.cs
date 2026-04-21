using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface IChatTelemetry
{
    Task TrackAsync(
        string eventName,
        IReadOnlyDictionary<string, object?> properties,
        CancellationToken cancellationToken);
}

public sealed class ChatTelemetry(
    ILogger<ChatTelemetry> logger,
    IOptions<AIIntegrationOptions> options) : IChatTelemetry
{
    public Task TrackAsync(
        string eventName,
        IReadOnlyDictionary<string, object?> properties,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!options.Value.Architecture.EmitTelemetryEvents
            || string.IsNullOrWhiteSpace(eventName))
        {
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "ChatTelemetry event={EventName} properties={PropertiesJson}",
            eventName.Trim(),
            JsonSerializer.Serialize(properties));

        return Task.CompletedTask;
    }
}
