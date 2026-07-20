using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

// Resolves a characteristics definition to its full validated taxonomy
// triple. Shared by knowledge seeding and AI growth so every writer stamps
// identical domain/category/subcategory values.
public static class CharacteristicsTaxonomyResolver
{
    public static bool TryResolve(
        CategoryCharacteristicsDefinition definition,
        out int domainId,
        out int categoryId,
        out int? subcategoryId)
    {
        var catalog = NSFinanceTaxonomyCatalog.Instance;

        if (definition.TaxonomySubcategoryId is { } subId
            && catalog.SubcategoriesById.TryGetValue(subId, out var subcategory))
        {
            domainId = subcategory.DomainId;
            categoryId = subcategory.CategoryId;
            subcategoryId = subcategory.Id;
            return true;
        }

        if (definition.TaxonomyCategoryId is { } catId
            && catalog.CategoriesById.TryGetValue(catId, out var category))
        {
            domainId = category.DomainId;
            categoryId = category.Id;
            subcategoryId = null;
            return true;
        }

        domainId = 0;
        categoryId = 0;
        subcategoryId = null;
        return false;
    }

    // Stable key a constrained AI response uses to reference a definition:
    // "sub:910101" when subcategory-level, otherwise "cat:13010".
    public static string DefinitionKey(CategoryCharacteristicsDefinition definition)
    {
        return definition.TaxonomySubcategoryId is { } subId
            ? $"sub:{subId}"
            : $"cat:{definition.TaxonomyCategoryId}";
    }

    public static string DefinitionDisplayName(CategoryCharacteristicsDefinition definition)
    {
        var catalog = NSFinanceTaxonomyCatalog.Instance;
        if (definition.TaxonomySubcategoryId is { } subId
            && catalog.SubcategoriesById.TryGetValue(subId, out var subcategory)
            && catalog.CategoriesById.TryGetValue(subcategory.CategoryId, out var parent))
        {
            return $"{parent.Name} > {subcategory.Name}";
        }

        if (definition.TaxonomyCategoryId is { } catId
            && catalog.CategoriesById.TryGetValue(catId, out var category))
        {
            return category.Name;
        }

        return DefinitionKey(definition);
    }
}
