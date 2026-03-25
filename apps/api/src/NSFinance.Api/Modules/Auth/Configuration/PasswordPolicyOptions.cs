namespace NSFinance.Api.Modules.Auth.Configuration;

public sealed class PasswordPolicyOptions
{
    public const string SectionName = "PasswordPolicy";

    public int MinLength { get; set; } = 12;
    public int MaxLength { get; set; } = 64;
    public bool RequireNumberOrSymbol { get; set; } = true;
    public bool BreachCheckEnabled { get; set; } = true;
    public string BreachApiBaseUrl { get; set; } = "https://api.pwnedpasswords.com/";
    public int BreachApiTimeoutSeconds { get; set; } = 8;
}
