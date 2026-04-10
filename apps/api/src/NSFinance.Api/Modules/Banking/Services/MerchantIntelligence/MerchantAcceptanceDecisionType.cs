namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public enum MerchantAcceptanceDecisionType
{
    AcceptedTrusted = 0,
    AcceptedCautious = 1,
    LowConfidence = 2,
    Unresolved = 3,
    Rejected = 4
}
