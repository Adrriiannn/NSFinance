using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Shared.Taxonomy;
using Xunit;

namespace NSFinance.Api.Tests.Unit;

public class ExpenseTaxonomyTests
{
    [Fact]
    public void CanonicalCatalog_HasStableVersionAndValidRootCounts()
    {
        Assert.Equal("2026-03-29-v1", NSFinanceTaxonomyCatalog.Version);
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

        Assert.DoesNotContain(taxonomy.Domains, domain => domain.Id is 900 or 910);
        Assert.Contains(taxonomy.Domains, domain => domain.Id == 920 && domain.IsUserSelectable && !domain.IsSystemDomain);
        Assert.All(taxonomy.Domains, domain => Assert.True(domain.IsUserSelectable));
    }

    [Fact]
    public void ExpenseTaxonomyService_GetUserSelectableSubcategory_RejectsSystemSubcategories()
    {
        var service = new ExpenseTaxonomyService();

        Assert.Null(service.GetUserSelectableSubcategory(900101));
        Assert.NotNull(service.GetUserSelectableSubcategory(920101));
        Assert.NotNull(service.GetUserSelectableSubcategory(130111));
    }

    [Fact]
    public void ExpenseTaxonomyService_DefaultTaxonomy_PlacesTransfersBetweenSavingsAndPersonalCare()
    {
        var service = new ExpenseTaxonomyService();
        var taxonomy = service.GetTaxonomy();

        var domainIds = taxonomy.Domains.Select(x => x.Id).ToList();
        var savingsIndex = domainIds.IndexOf(180);
        var transferIndex = domainIds.IndexOf(920);
        var personalCareIndex = domainIds.IndexOf(190);

        Assert.True(savingsIndex >= 0);
        Assert.True(transferIndex >= 0);
        Assert.True(personalCareIndex >= 0);
        Assert.True(savingsIndex < transferIndex);
        Assert.True(transferIndex < personalCareIndex);
    }

    [Fact]
    public void ExpenseTaxonomyService_TransferCategoryAndSubcategory_AreTransactionAssignable()
    {
        var service = new ExpenseTaxonomyService();

        var transferCategory = service.GetTransactionAssignableCategory(ExpenseTaxonomyService.TransferDefaultCategoryId);
        var transferSubcategory = service.GetTransactionAssignableSubcategory(ExpenseTaxonomyService.TransferDefaultSubcategoryId);

        Assert.NotNull(transferCategory);
        Assert.NotNull(transferSubcategory);
        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, transferCategory!.DomainId);
        Assert.Equal(transferCategory.Id, transferSubcategory!.CategoryId);
    }
}
