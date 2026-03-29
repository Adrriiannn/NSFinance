using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions.DTOs;

namespace NSFinance.Api.Modules.Transactions.Validators;

public static class TransactionMetadataRequestValidator
{
    public static Dictionary<string, string[]> Validate(
        UpdateTransactionMetadataRequest request,
        ExpenseTaxonomyService expenseTaxonomyService)
    {
        var errors = new Dictionary<string, string[]>();

        if (!request.TaxonomyCategoryId.HasValue || request.TaxonomyCategoryId.Value <= 0)
        {
            errors["taxonomyCategoryId"] = ["Category is required."];
        }
        else if (expenseTaxonomyService.GetTransactionAssignableCategory(request.TaxonomyCategoryId.Value) is null)
        {
            errors["taxonomyCategoryId"] = ["Select a valid category."];
        }

        if (request.TaxonomySubcategoryId.HasValue)
        {
            if (request.TaxonomySubcategoryId.Value <= 0)
            {
                errors["taxonomySubcategoryId"] = ["Subcategory is invalid."];
            }
            else
            {
                var subcategory = expenseTaxonomyService.GetTransactionAssignableSubcategory(request.TaxonomySubcategoryId.Value);
                if (subcategory is null)
                {
                    errors["taxonomySubcategoryId"] = ["Select a valid subcategory."];
                }
                else if (request.TaxonomyCategoryId.HasValue && subcategory.CategoryId != request.TaxonomyCategoryId.Value)
                {
                    errors["taxonomySubcategoryId"] = ["Subcategory must belong to the selected category."];
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Trim().Length > 140)
        {
            errors["reason"] = ["Reason must be 140 characters or fewer."];
        }

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > 1200)
        {
            errors["notes"] = ["Notes must be 1200 characters or fewer."];
        }

        return errors;
    }
}
