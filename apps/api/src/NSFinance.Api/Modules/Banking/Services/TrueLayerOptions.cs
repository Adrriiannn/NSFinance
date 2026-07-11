namespace NSFinance.Api.Modules.Banking.Services;

public sealed class TrueLayerOptions
{
    public const string SectionName = "TrueLayer";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Environment { get; set; } = "live";
    public string AuthBaseUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
}
