namespace NSFinance.Api.Persistence.Entities;

public static class FinancialAccountSources
{
    public const string Manual = "manual";
    public const string ProviderProjected = "provider_projected";
}

public static class TransactionEntryKinds
{
    public const string Ordinary = "ordinary";
    public const string OpeningBalanceAdjustment = "opening_balance_adjustment";
    public const string ManualAdjustment = "manual_adjustment";
    public const string StatementImport = "statement_import";
}

public static class TransactionAnalyticsTreatments
{
    public const string Ordinary = "ordinary";
    public const string BalanceOnly = "balance_only";
}
