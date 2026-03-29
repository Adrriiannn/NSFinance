using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public sealed class ExpenseTaxonomyService
{
    public const int TransferDomainId = 920;
    public const int TransferDefaultCategoryId = 92010;
    public const int TransferDefaultSubcategoryId = 920101;
    private readonly NSFinanceTaxonomyCatalog catalog = NSFinanceTaxonomyCatalog.Instance;

    public ExpenseTaxonomyResponseDto GetTaxonomy(bool includeSystem = false)
    {
        var domains = catalog.GetDomains(includeSystem)
            .OrderBy(domain => domain.SortOrder)
            .ThenBy(domain => domain.Id)
            .Select(MapDomain)
            .ToList();

        return new ExpenseTaxonomyResponseDto(NSFinanceTaxonomyCatalog.Version, domains);
    }

    public bool TryGetSubcategory(int subcategoryId, out TaxonomySubcategoryDefinition? subcategory)
    {
        return catalog.TryGetSubcategory(subcategoryId, out subcategory);
    }

    public TaxonomySubcategoryDefinition? GetUserSelectableSubcategory(int subcategoryId)
    {
        if (!catalog.TryGetSubcategory(subcategoryId, out var subcategory) || subcategory is null)
        {
            return null;
        }

        if (!subcategory.IsActive || !subcategory.IsUserSelectable)
        {
            return null;
        }

        if (!catalog.DomainsById.TryGetValue(subcategory.DomainId, out var domain) || domain.IsSystemDomain || !domain.IsUserSelectable || !domain.IsActive)
        {
            return null;
        }

        return subcategory;
    }

    public TaxonomyCategoryDefinition? GetUserSelectableCategory(int categoryId)
    {
        if (!catalog.CategoriesById.TryGetValue(categoryId, out var category) || category is null)
        {
            return null;
        }

        if (!category.IsActive || !category.IsUserSelectable)
        {
            return null;
        }

        if (!catalog.DomainsById.TryGetValue(category.DomainId, out var domain) || domain.IsSystemDomain || !domain.IsUserSelectable || !domain.IsActive)
        {
            return null;
        }

        return category;
    }

    public TaxonomyCategoryDefinition? GetTransactionAssignableCategory(int categoryId)
    {
        if (!catalog.CategoriesById.TryGetValue(categoryId, out var category) || category is null || !category.IsActive)
        {
            return null;
        }

        if (!catalog.DomainsById.TryGetValue(category.DomainId, out var domain) || !domain.IsActive)
        {
            return null;
        }

        return !domain.IsSystemDomain && domain.IsUserSelectable && category.IsUserSelectable
            ? category
            : null;
    }

    public TaxonomySubcategoryDefinition? GetTransactionAssignableSubcategory(int subcategoryId)
    {
        if (!catalog.TryGetSubcategory(subcategoryId, out var subcategory) || subcategory is null || !subcategory.IsActive)
        {
            return null;
        }

        if (!catalog.DomainsById.TryGetValue(subcategory.DomainId, out var domain) || !domain.IsActive)
        {
            return null;
        }

        return !domain.IsSystemDomain && domain.IsUserSelectable && subcategory.IsUserSelectable
            ? subcategory
            : null;
    }

    public string? GetDomainName(int? domainId)
    {
        return domainId.HasValue && catalog.DomainsById.TryGetValue(domainId.Value, out var domain)
            ? domain.Name
            : null;
    }

    public string? GetCategoryName(int? categoryId)
    {
        return categoryId.HasValue && catalog.CategoriesById.TryGetValue(categoryId.Value, out var category)
            ? category.Name
            : null;
    }

    public string? GetSubcategoryName(int? subcategoryId)
    {
        return subcategoryId.HasValue && catalog.SubcategoriesById.TryGetValue(subcategoryId.Value, out var subcategory)
            ? subcategory.Name
            : null;
    }

    private static ExpenseTaxonomyDomainDto MapDomain(TaxonomyDomainDefinition domain)
    {
        return new ExpenseTaxonomyDomainDto(
            domain.Id,
            domain.Name,
            domain.Description,
            domain.IsUserSelectable,
            domain.IsSystemDomain,
            domain.SortOrder,
            domain.IsActive,
            [.. (domain.Aliases ?? [])],
            [.. (domain.Keywords ?? [])],
            [.. (domain.MerchantHints ?? [])],
            domain.IsLikelyRecurring,
            domain.IsLikelyRefundable,
            domain.Notes,
            domain.Categories
                .OrderBy(category => category.SortOrder)
                .ThenBy(category => category.Id)
                .Select(MapCategory)
                .ToList());
    }

    private static ExpenseTaxonomyCategoryDto MapCategory(TaxonomyCategoryDefinition category)
    {
        return new ExpenseTaxonomyCategoryDto(
            category.Id,
            category.DomainId,
            category.Name,
            category.Description,
            category.IsUserSelectable,
            category.SortOrder,
            category.IsActive,
            [.. (category.Aliases ?? [])],
            [.. (category.Keywords ?? [])],
            [.. (category.MerchantHints ?? [])],
            category.IsLikelyRecurring,
            category.IsLikelyRefundable,
            category.Notes,
            category.Subcategories
                .OrderBy(subcategory => subcategory.SortOrder)
                .ThenBy(subcategory => subcategory.Id)
                .Select(MapSubcategory)
                .ToList());
    }

    private static ExpenseTaxonomySubcategoryDto MapSubcategory(TaxonomySubcategoryDefinition subcategory)
    {
        return new ExpenseTaxonomySubcategoryDto(
            subcategory.Id,
            subcategory.DomainId,
            subcategory.CategoryId,
            subcategory.Name,
            subcategory.Description,
            subcategory.IsUserSelectable,
            subcategory.SortOrder,
            subcategory.IsActive,
            [.. (subcategory.Aliases ?? [])],
            [.. (subcategory.Keywords ?? [])],
            [.. (subcategory.MerchantHints ?? [])],
            subcategory.IsLikelyRecurring,
            subcategory.IsLikelyRefundable,
            subcategory.Notes);
    }
}
