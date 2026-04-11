namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public enum MerchantInvestigationRecommendation
{
    AcceptCandidate = 0,
    AcceptCautiously = 1,
    Unresolved = 2,
    InsufficientEvidence = 3,
    ConflictingCandidates = 4
}
