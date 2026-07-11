namespace NSFinance.Api.Modules.Auth.Services;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string ClientId { get; set; } = string.Empty;
    public string WebClientId { get; set; } = string.Empty;
    public string AndroidClientIdProd { get; set; } = string.Empty;

    public string[] AdditionalClientIds { get; set; } = [];

    public IReadOnlyList<string> GetConfiguredClientIds()
    {
        var configured = new HashSet<string>(StringComparer.Ordinal);

        AddIfPresent(configured, ClientId);
        AddIfPresent(configured, WebClientId);
        AddIfPresent(configured, AndroidClientIdProd);

        foreach (var clientId in AdditionalClientIds)
        {
            AddIfPresent(configured, clientId);
        }

        return configured.ToArray();
    }

    private static void AddIfPresent(ISet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }
}
