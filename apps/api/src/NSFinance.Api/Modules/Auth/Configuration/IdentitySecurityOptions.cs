namespace NSFinance.Api.Modules.Auth.Configuration;

public sealed class IdentitySecurityOptions
{
    public const string SectionName = "IdentitySecurity";

    public string CodePepper { get; set; } = string.Empty;
    public int ChallengeLifetimeMinutes { get; set; } = 10;
    public int RecoveryGrantLifetimeMinutes { get; set; } = 10;
    public int MfaChallengeLifetimeMinutes { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxCodeAttempts { get; set; } = 5;
    public int RecoveryCodeCount { get; set; } = 10;
    public string TotpIssuer { get; set; } = "NSFinance";
}
