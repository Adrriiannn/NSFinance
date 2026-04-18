using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public interface IProtectedPreferenceHintParser
{
    IReadOnlyList<string> Parse(string? json);
}

public sealed class ProtectedPreferenceHintParser : IProtectedPreferenceHintParser
{
    public IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var hints = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        hints.Add(value.Trim());
                    }

                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in item.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            hints.Add(value.Trim());
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        hints.Add(property.Value.ToString());
                    }
                }
            }

            return hints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
