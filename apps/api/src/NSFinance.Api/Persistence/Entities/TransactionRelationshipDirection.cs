namespace NSFinance.Api.Persistence.Entities;

public enum TransactionRelationshipDirection
{
    None = 0,
    OutflowToInflow = 1,
    OutflowToSavings = 2,
    InflowFromSavings = 3
}

