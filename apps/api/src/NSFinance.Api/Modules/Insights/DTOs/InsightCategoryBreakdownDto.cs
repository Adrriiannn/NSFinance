namespace NSFinance.Api.Modules.Insights.DTOs;

// Where the money went, per month and category (INS-001 + CAT-001). Same
// honesty rules as the periods contract: ordinary rows only, expense-bucket
// outflows only (transfer-neutralized rows count toward nothing), currencies
// never mix. The uncategorized remainder is always reported - hiding it
// would make the categorized share look like the whole story.
public sealed record InsightCategoryBreakdownDto(
    DateTime AsOfUtc,
    int MonthsRequested,
    IReadOnlyList<InsightCategoryCurrencyGroupDto> CurrencyGroups);

public sealed record InsightCategoryCurrencyGroupDto(
    string Currency,
    IReadOnlyList<InsightCategoryPeriodDto> Periods);

public sealed record InsightCategoryPeriodDto(
    int Year,
    int Month,
    decimal TotalSpend,
    decimal CategorizedSpend,
    decimal UncategorizedSpend,
    int UncategorizedTransactionCount,
    bool IsPartial,
    IReadOnlyList<InsightCategorySpendDto> Categories);

public sealed record InsightCategorySpendDto(
    int TaxonomyDomainId,
    string DomainName,
    int TaxonomyCategoryId,
    string CategoryName,
    decimal Spend,
    int TransactionCount);
