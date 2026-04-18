using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record FinancialAdviceSanitizedAdjudicationInput(
    FinancialAdviceAdjudicationInputPacket Packet,
    string PacketJson);

public interface IAdjudicationInputSanitizer
{
    FinancialAdviceSanitizedAdjudicationInput Sanitize(
        FinancialAdviceAdjudicationInputPacket packet,
        int maxInputChars);
}

public sealed class AdjudicationInputSanitizer : IAdjudicationInputSanitizer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FinancialAdviceSanitizedAdjudicationInput Sanitize(
        FinancialAdviceAdjudicationInputPacket packet,
        int maxInputChars)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var safeMax = Math.Max(1_000, maxInputChars);
        var packetJson = JsonSerializer.Serialize(packet, SerializerOptions);
        if (packetJson.Length <= safeMax)
        {
            return new FinancialAdviceSanitizedAdjudicationInput(packet, packetJson);
        }

        var trimmedPacket = packet with
        {
            Findings = packet.Findings.Take(Math.Max(1, packet.Findings.Count - 1)).ToArray(),
            EvidenceSummary = packet.EvidenceSummary.Take(4).ToArray()
        };
        var trimmedJson = JsonSerializer.Serialize(trimmedPacket, SerializerOptions);
        return new FinancialAdviceSanitizedAdjudicationInput(trimmedPacket, trimmedJson);
    }
}
