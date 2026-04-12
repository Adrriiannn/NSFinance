namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public enum MerchantSourceTrustLevel
{
    Unknown = 0,
    OfficialDomain = 1,
    AuthoritativeListing = 2,
    PublicDirectory = 3,
    WeakWebMention = 4,
    AIInferenceOnly = 5,
    NoSource = 6
}
