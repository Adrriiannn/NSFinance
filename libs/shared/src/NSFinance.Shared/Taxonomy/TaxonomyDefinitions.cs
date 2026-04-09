using System.Collections.ObjectModel;

namespace NSFinance.Shared.Taxonomy;

public sealed record TaxonomyDomainDefinition(
    int Id,
    string Name,
    string Description,
    bool IsUserSelectable,
    bool IsSystemDomain,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<TaxonomyCategoryDefinition> Categories,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? Keywords = null,
    IReadOnlyList<string>? MerchantHints = null,
    bool IsLikelyRecurring = false,
    bool IsLikelyRefundable = false,
    string? Notes = null);

public sealed record TaxonomyCategoryDefinition(
    int Id,
    int DomainId,
    string Name,
    string Description,
    bool IsUserSelectable,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<TaxonomySubcategoryDefinition> Subcategories,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? Keywords = null,
    IReadOnlyList<string>? MerchantHints = null,
    bool IsLikelyRecurring = false,
    bool IsLikelyRefundable = false,
    string? Notes = null);

public sealed record TaxonomySubcategoryDefinition(
    int Id,
    int DomainId,
    int CategoryId,
    string Name,
    string Description,
    bool IsUserSelectable,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? Keywords = null,
    IReadOnlyList<string>? MerchantHints = null,
    bool IsLikelyRecurring = false,
    bool IsLikelyRefundable = false,
    string? Notes = null);

public sealed class NSFinanceTaxonomyCatalog
{
    public const string Version = "2026-04-09-v1";

    public static NSFinanceTaxonomyCatalog Instance { get; } = CreateValidated();

    public NSFinanceTaxonomyCatalog(IReadOnlyList<TaxonomyDomainDefinition> domains)
    {
        Domains = new ReadOnlyCollection<TaxonomyDomainDefinition>(domains.ToList());
        DomainsById = new ReadOnlyDictionary<int, TaxonomyDomainDefinition>(Domains.ToDictionary(x => x.Id));
        CategoriesById = new ReadOnlyDictionary<int, TaxonomyCategoryDefinition>(
            Domains.SelectMany(x => x.Categories).ToDictionary(x => x.Id));
        SubcategoriesById = new ReadOnlyDictionary<int, TaxonomySubcategoryDefinition>(
            Domains.SelectMany(x => x.Categories).SelectMany(x => x.Subcategories).ToDictionary(x => x.Id));
    }

    public IReadOnlyList<TaxonomyDomainDefinition> Domains { get; }
    public IReadOnlyDictionary<int, TaxonomyDomainDefinition> DomainsById { get; }
    public IReadOnlyDictionary<int, TaxonomyCategoryDefinition> CategoriesById { get; }
    public IReadOnlyDictionary<int, TaxonomySubcategoryDefinition> SubcategoriesById { get; }

    public IReadOnlyList<TaxonomyDomainDefinition> GetDomains(bool includeSystem)
    {
        return Domains
            .Where(domain => domain.IsActive && (includeSystem || (domain.IsUserSelectable && !domain.IsSystemDomain)))
            .ToList();
    }

    public bool TryGetSubcategory(int subcategoryId, out TaxonomySubcategoryDefinition? subcategory)
    {
        var found = SubcategoriesById.TryGetValue(subcategoryId, out var resolved);
        subcategory = resolved;
        return found;
    }

    public TaxonomySubcategoryDefinition GetRequiredSubcategory(int subcategoryId)
    {
        if (!SubcategoriesById.TryGetValue(subcategoryId, out var subcategory))
        {
            throw new KeyNotFoundException($"Taxonomy sub-category {subcategoryId} was not found.");
        }

        return subcategory;
    }

    public static void Validate(IReadOnlyList<TaxonomyDomainDefinition> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);

        var domainIds = new HashSet<int>();
        var categoryIds = new HashSet<int>();
        var subcategoryIds = new HashSet<int>();

        foreach (var domain in domains)
        {
            if (!domainIds.Add(domain.Id))
            {
                throw new InvalidOperationException($"Duplicate taxonomy domain id detected: {domain.Id}.");
            }

            if (domain.Id < 100 || domain.Id > 999 || domain.Id % 10 != 0)
            {
                throw new InvalidOperationException($"Domain id {domain.Id} does not follow the canonical pattern.");
            }

            foreach (var category in domain.Categories)
            {
                if (!categoryIds.Add(category.Id))
                {
                    throw new InvalidOperationException($"Duplicate taxonomy category id detected: {category.Id}.");
                }

                if (category.DomainId != domain.Id)
                {
                    throw new InvalidOperationException(
                        $"Category {category.Id} has domain {category.DomainId} but is nested under {domain.Id}.");
                }

                var minCategoryId = domain.Id * 100;
                if (category.Id <= minCategoryId || category.Id >= minCategoryId + 1000 || category.Id % 10 != 0)
                {
                    throw new InvalidOperationException($"Category id {category.Id} does not follow the canonical pattern.");
                }

                foreach (var subcategory in category.Subcategories)
                {
                    if (!subcategoryIds.Add(subcategory.Id))
                    {
                        throw new InvalidOperationException($"Duplicate taxonomy sub-category id detected: {subcategory.Id}.");
                    }

                    if (subcategory.DomainId != domain.Id)
                    {
                        throw new InvalidOperationException(
                            $"Sub-category {subcategory.Id} has domain {subcategory.DomainId} but belongs under domain {domain.Id}.");
                    }

                    if (subcategory.CategoryId != category.Id)
                    {
                        throw new InvalidOperationException(
                            $"Sub-category {subcategory.Id} has category {subcategory.CategoryId} but belongs under category {category.Id}.");
                    }

                    var minSubcategoryId = category.Id * 10;
                    if (subcategory.Id <= minSubcategoryId || subcategory.Id >= minSubcategoryId + 100)
                    {
                        throw new InvalidOperationException(
                            $"Sub-category id {subcategory.Id} does not follow the canonical pattern for domain {domain.Id} and category {category.Id}.");
                    }
                }
            }
        }
    }

    private static NSFinanceTaxonomyCatalog CreateValidated()
    {
        Validate(NSFinanceTaxonomyData.Domains);
        return new NSFinanceTaxonomyCatalog(NSFinanceTaxonomyData.Domains);
    }
}
