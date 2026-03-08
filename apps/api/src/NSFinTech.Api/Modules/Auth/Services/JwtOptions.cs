namespace NSFinTech.Api.Modules.Auth.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "NSFinTech.Api";
    public string Audience { get; set; } = "NSFinTech.Mobile";
    public string SigningKey { get; set; } = "CHANGE_ME_FOR_PRODUCTION_NSFINTECH";
    public int AccessTokenMinutes { get; set; } = 120;
}
