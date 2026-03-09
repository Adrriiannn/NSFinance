namespace NSFinTech.Api.Modules.Auth.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "NSFinTech.Api";
    public string Audience { get; set; } = "NSFinTech.Mobile";
    public string SigningKey { get; set; } = "CHANGE_ME_FOR_PRODUCTION_NSFINTECH_SIGNING_KEY";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int PasswordResetTokenMinutes { get; set; } = 30;
    public int EmailVerificationTokenMinutes { get; set; } = 60;
    public int MaxFailedLoginAttempts { get; set; } = 6;
    public int FailedLoginWindowMinutes { get; set; } = 15;
    public int LoginLockoutMinutes { get; set; } = 10;
}
