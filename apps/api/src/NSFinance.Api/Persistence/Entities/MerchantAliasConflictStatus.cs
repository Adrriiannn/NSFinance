namespace NSFinance.Api.Persistence.Entities;

public enum MerchantAliasConflictStatus
{
    Open = 0,
    ResolvedRejected = 1,
    ResolvedReassigned = 2,
    Ignored = 3
}
