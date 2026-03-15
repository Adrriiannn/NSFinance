namespace NSFinance.Api.Modules.ExpenseTracker.DTOs;

public sealed record ExpenseTaxonomySubcategoryDto(
    int Id,
    int DomainId,
    int CategoryId,
    string Name,
    string Description,
    bool IsUserSelectable,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> MerchantHints,
    bool IsLikelyRecurring,
    bool IsLikelyRefundable,
    string? Notes);

public sealed record ExpenseTaxonomyCategoryDto(
    int Id,
    int DomainId,
    string Name,
    string Description,
    bool IsUserSelectable,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> MerchantHints,
    bool IsLikelyRecurring,
    bool IsLikelyRefundable,
    string? Notes,
    IReadOnlyList<ExpenseTaxonomySubcategoryDto> Subcategories);

public sealed record ExpenseTaxonomyDomainDto(
    int Id,
    string Name,
    string Description,
    bool IsUserSelectable,
    bool IsSystemDomain,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> MerchantHints,
    bool IsLikelyRecurring,
    bool IsLikelyRefundable,
    string? Notes,
    IReadOnlyList<ExpenseTaxonomyCategoryDto> Categories);

public sealed record ExpenseTaxonomyResponseDto(
    string Version,
    IReadOnlyList<ExpenseTaxonomyDomainDto> Domains);
