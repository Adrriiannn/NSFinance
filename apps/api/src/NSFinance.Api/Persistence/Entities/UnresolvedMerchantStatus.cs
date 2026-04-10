namespace NSFinance.Api.Persistence.Entities;

public enum UnresolvedMerchantStatus
{
    New = 0,
    Investigating = 1,
    AwaitingEvidence = 2,
    Resolved = 3
}
