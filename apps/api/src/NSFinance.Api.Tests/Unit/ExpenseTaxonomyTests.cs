using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Shared.Taxonomy;
using Xunit;

namespace NSFinance.Api.Tests.Unit;

public class ExpenseTaxonomyTests
{
    [Fact]
    public void CanonicalCatalog_HasStableVersionAndValidRootCounts()
    {
        Assert.Equal("2026-03-14-v1", NSFinanceTaxonomyCatalog.Version);
        Assert.Equal(25, NSFinanceTaxonomyCatalog.Instance.Domains.Count);
    }

    [Fact]
    public void CanonicalCatalog_HasNoDuplicateIdsAndRespectsParentPatterns()
    {
        var exception = Record.Exception(() => NSFinanceTaxonomyCatalog.Validate(NSFinanceTaxonomyCatalog.Instance.Domains));
        Assert.Null(exception);
    }

    [Fact]
    public void ExpenseTaxonomyService_DefaultTaxonomy_ExcludesSystemDomains()
    {
        var service = new ExpenseTaxonomyService();

        var taxonomy = service.GetTaxonomy();

        Assert.DoesNotContain(taxonomy.Domains, domain => domain.Id is 900 or 910 or 920);
        Assert.All(taxonomy.Domains, domain => Assert.True(domain.IsUserSelectable));
    }

    [Fact]
    public void ExpenseTaxonomyService_GetUserSelectableSubcategory_RejectsSystemSubcategories()
    {
        var service = new ExpenseTaxonomyService();

        Assert.Null(service.GetUserSelectableSubcategory(900101));
        Assert.NotNull(service.GetUserSelectableSubcategory(130111));
    }
}
