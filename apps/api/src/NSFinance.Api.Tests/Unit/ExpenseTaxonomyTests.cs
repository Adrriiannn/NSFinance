using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Shared.Taxonomy;
using Xunit;

namespace NSFinance.Api.Tests.Unit;

public class ExpenseTaxonomyTests
{
    [Fact]
    public void CanonicalCatalog_HasStableVersionAndValidRootCounts()
    {
        Assert.Equal("2026-04-09-v1", NSFinanceTaxonomyCatalog.Version);
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

    [Fact]
    public void ExpenseTaxonomyService_DefaultTaxonomy_UsesReducedTransferStructure()
    {
        var service = new ExpenseTaxonomyService();
        var taxonomy = service.GetTaxonomy();

        var transferDomain = Assert.Single(taxonomy.Domains.Where(x => x.Id == ExpenseTaxonomyService.TransferDomainId));
        var transferCategoryIds = transferDomain.Categories.Select(x => x.Id).ToArray();

        Assert.Equal([92010, 92020, 92030], transferCategoryIds);

        var internalTransfers = Assert.Single(transferDomain.Categories.Where(x => x.Id == 92010));
        var internalTransferSubcategoryIds = internalTransfers.Subcategories.Select(x => x.Id).ToArray();
        Assert.Equal(
            [
                ExpenseTaxonomyService.TransferDefaultSubcategoryId,
                ExpenseTaxonomyService.TransferCurrencySubcategoryId,
                ExpenseTaxonomyService.TransferOtherInternalMoneyMovementSubcategoryId
            ],
            internalTransferSubcategoryIds);
    }

    [Fact]
    public void ExpenseTaxonomyService_DefaultTaxonomy_DoesNotExposeRetiredTransferSubcategories()
    {
        var service = new ExpenseTaxonomyService();
        var taxonomy = service.GetTaxonomy();

        var transferDomain = Assert.Single(taxonomy.Domains.Where(x => x.Id == ExpenseTaxonomyService.TransferDomainId));
        var transferSubcategoryIds = transferDomain.Categories
            .SelectMany(x => x.Subcategories)
            .Select(x => x.Id)
            .ToHashSet();

        Assert.DoesNotContain(920102, transferSubcategoryIds);
        Assert.DoesNotContain(920103, transferSubcategoryIds);
        Assert.DoesNotContain(920104, transferSubcategoryIds);
        Assert.DoesNotContain(920303, transferSubcategoryIds);
        Assert.DoesNotContain(920401, transferSubcategoryIds);
        Assert.DoesNotContain(920402, transferSubcategoryIds);
        Assert.DoesNotContain(920403, transferSubcategoryIds);
    }

    [Fact]
    public void ExpenseTaxonomyService_LegacySavingsTransferSubcategory_MapsForwardToGeneralSavingsTransfer()
    {
        var service = new ExpenseTaxonomyService();

        var mapped = service.GetUserSelectableSubcategory(920102);
        Assert.NotNull(mapped);
        Assert.Equal(ExpenseTaxonomyService.GeneralSavingsTransferSubcategoryId, mapped!.Id);
        Assert.Equal("General Savings Transfer", mapped.Name);
    }
}
