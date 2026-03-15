using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public sealed class ExpenseTaxonomyService
{
    private readonly NSFinanceTaxonomyCatalog catalog = NSFinanceTaxonomyCatalog.Instance;

    public ExpenseTaxonomyResponseDto GetTaxonomy(bool includeSystem = false)
    {
        var domains = catalog.GetDomains(includeSystem)
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
            domain.Categories.Select(MapCategory).ToList());
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
            category.Subcategories.Select(MapSubcategory).ToList());
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
