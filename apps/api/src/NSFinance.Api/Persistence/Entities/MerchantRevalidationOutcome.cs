namespace NSFinance.Api.Persistence.Entities;

public enum MerchantRevalidationOutcome
{
    KeepTrusted = 0,
    DowngradedToCautious = 1,
    KeepCautious = 2,
    PromotedToTrusted = 3,
    MarkedForUnresolvedReview = 4,
    Failed = 5
}
