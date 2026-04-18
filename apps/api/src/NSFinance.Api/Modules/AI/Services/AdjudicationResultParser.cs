using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public interface IAdjudicationResultParser
{
    FinancialAdviceAdjudicationStructuredResponse? Parse(string payload);
}

public sealed class AdjudicationResultParser : IAdjudicationResultParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FinancialAdviceAdjudicationStructuredResponse? Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FinancialAdviceAdjudicationStructuredResponse>(payload, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }
}
