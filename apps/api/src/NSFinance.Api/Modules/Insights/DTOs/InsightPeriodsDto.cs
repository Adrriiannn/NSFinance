namespace NSFinance.Api.Modules.Insights.DTOs;

// Monthly income/spend/net aggregates (INS-001). Values follow the same
// transfer and analytics-treatment policy as the dashboard's honest outflow:
// balance-only entries never count, and transfer-neutralized rows count
// toward neither income nor spend. Amounts never mix currencies; each
// currency reports its own period series.
public sealed record InsightPeriodsDto(
    DateTime AsOfUtc,
    int MonthsRequested,
    IReadOnlyList<InsightPeriodCurrencyGroupDto> CurrencyGroups);

public sealed record InsightPeriodCurrencyGroupDto(
    string Currency,
    IReadOnlyList<InsightPeriodDto> Periods);

public sealed record InsightPeriodDto(
    int Year,
    int Month,
    decimal Income,
    decimal Spend,
    decimal Net,
    int CountedTransactionCount,
    bool IsPartial);
