namespace NSFinance.Api.Modules.Auth.Configuration;

public sealed class MicrosoftAuthOptions
{
    public const string SectionName = "MicrosoftAuth";
    public const string DelegatedScopeName = "access_as_user";

    public string ClientId { get; set; } = string.Empty;
    public string Authority { get; set; } = "https://login.microsoftonline.com/common/v2.0";
    public string MetadataAddress { get; set; } = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
    public string ApiScope => IsConfigured
        ? $"api://{ClientId}/{DelegatedScopeName}"
        : string.Empty;
}
