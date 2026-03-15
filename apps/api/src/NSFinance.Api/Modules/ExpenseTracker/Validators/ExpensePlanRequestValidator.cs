using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Models;
using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.ExpenseTracker.Validators;

public static class ExpensePlanRequestValidator
{
    private static readonly IReadOnlySet<string> AllowedCreateStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ExpensePlanStatuses.Drafted,
        ExpensePlanStatuses.Scheduled,
        ExpensePlanStatuses.Active
    };

    public static Dictionary<string, string[]> ValidateCreate(CreateExpensePlanRequest request, ExpenseTaxonomyService taxonomyService)
    {
        var errors = ValidateCore(
            request.Title,
            request.Description,
            request.Notes,
            request.PlanType,
            request.StartDateUtc,
            request.EndDateUtc,
            request.CurrencyCode,
            request.ExpectedIncomeTotal,
            request.LineItems,
            request.Tags,
            request.IsTemplate,
            request.IsRecurring,
            request.Recurrence,
            request.IsShared,
            request.SharingMode,
            taxonomyService);

        if (!AllowedCreateStatuses.Contains(request.Status.Trim()))
        {
            errors[nameof(request.Status)] = ["Create status must be drafted, scheduled, or active."];
        }

        if (!string.IsNullOrWhiteSpace(request.PlanOriginType)
            && !ExpensePlanOriginTypes.All.Contains(request.PlanOriginType.Trim()))
        {
            errors[nameof(request.PlanOriginType)] = ["Plan origin type is invalid."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateUpdate(UpdateExpensePlanRequest request, ExpenseTaxonomyService taxonomyService)
    {
        return ValidateCore(
            request.Title,
            request.Description,
            request.Notes,
            request.PlanType,
            request.StartDateUtc,
            request.EndDateUtc,
            request.CurrencyCode,
            request.ExpectedIncomeTotal,
            request.LineItems,
            request.Tags,
            request.IsTemplate,
            request.IsRecurring,
            request.Recurrence,
            request.IsShared,
            request.SharingMode,
            taxonomyService);
    }

    public static Dictionary<string, string[]> ValidateTransition(TransitionExpensePlanRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.TargetStatus) || !ExpensePlanStatuses.All.Contains(request.TargetStatus.Trim()))
        {
            errors[nameof(request.TargetStatus)] = ["Target status is invalid."];
        }

        if (!string.IsNullOrWhiteSpace(request.StatusReason) && request.StatusReason.Trim().Length > 200)
        {
            errors[nameof(request.StatusReason)] = ["Status reason must be 200 characters or fewer."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateCore(
        string title,
        string? description,
        string? notes,
        string planType,
        DateTime startDateUtc,
        DateTime endDateUtc,
        string currencyCode,
        decimal expectedIncomeTotal,
        IReadOnlyList<ExpensePlanLineItemRequest> lineItems,
        IReadOnlyList<string>? tags,
        bool isTemplate,
        bool isRecurring,
        ExpensePlanRecurrenceDto? recurrence,
        bool isShared,
        string? sharingMode,
        ExpenseTaxonomyService taxonomyService)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors[nameof(title)] = ["Title is required."];
        }
        else if (title.Trim().Length > 160)
        {
            errors[nameof(title)] = ["Title must be 160 characters or fewer."];
        }

        if (!string.IsNullOrWhiteSpace(description) && description.Trim().Length > 1200)
        {
            errors[nameof(description)] = ["Description must be 1200 characters or fewer."];
        }

        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > 2000)
        {
            errors[nameof(notes)] = ["Notes must be 2000 characters or fewer."];
        }

        if (!ExpensePlanTypes.All.Contains(planType.Trim()))
        {
            errors[nameof(planType)] = ["Plan type is invalid."];
        }
        else if (!ExpensePlanLifecycleService.TryValidatePeriod(planType, startDateUtc, endDateUtc, out var periodError))
        {
            errors[nameof(startDateUtc)] = [periodError ?? "Plan dates are invalid."];
        }

        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
        {
            errors[nameof(currencyCode)] = ["Currency code must be a 3-letter code."];
        }

        if (expectedIncomeTotal < 0m)
        {
            errors[nameof(expectedIncomeTotal)] = ["Expected income total cannot be negative."];
        }

        if (lineItems is null || lineItems.Count == 0)
        {
            errors[nameof(lineItems)] = ["At least one plan line item is required."];
        }
        else
        {
            var duplicateTargets = lineItems
                .Select(item => item.TaxonomySubcategoryId ?? item.TaxonomyCategoryId)
                .GroupBy(item => item)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicateTargets.Count > 0)
            {
                errors[nameof(lineItems)] = ["Plan line items must target unique taxonomy categories/subcategories."];
            }

            if (lineItems.Any(item => item.ExpectedAmount <= 0m))
            {
                errors[nameof(lineItems)] = ["Plan line items must have an expected amount greater than zero."];
            }

            if (lineItems.Any(item => !string.IsNullOrWhiteSpace(item.Notes) && item.Notes.Trim().Length > 800))
            {
                errors[nameof(lineItems)] = ["Line item notes must be 800 characters or fewer."];
            }

            foreach (var lineItem in lineItems)
            {
                if (!lineItem.TaxonomySubcategoryId.HasValue)
                {
                    errors[nameof(lineItems)] = ["Sub-category selection is required for manual plan building."];
                    break;
                }

                var subcategory = taxonomyService.GetUserSelectableSubcategory(lineItem.TaxonomySubcategoryId.Value);
                if (subcategory is null)
                {
                    errors[nameof(lineItems)] = ["Each plan line item must reference a valid user-visible taxonomy sub-category."];
                    break;
                }

                if (subcategory.CategoryId != lineItem.TaxonomyCategoryId)
                {
                    errors[nameof(lineItems)] = ["Line item category and sub-category references must match the canonical taxonomy."];
                    break;
                }
            }
        }

        if (tags is not null && tags.Any(tag => tag.Trim().Length > 32))
        {
            errors[nameof(tags)] = ["Tags must be 32 characters or fewer."];
        }

        if (isTemplate && isRecurring)
        {
            errors[nameof(isTemplate)] = ["Templates should not also be marked recurring in this pass."];
        }

        if (isRecurring)
        {
            if (recurrence is null)
            {
                errors[nameof(recurrence)] = ["Recurring plans must include recurrence settings."];
            }
            else
            {
                if (!ExpensePlanRecurrenceTypes.All.Contains(recurrence.RecurrenceType.Trim()))
                {
                    errors[nameof(recurrence)] = ["Recurrence type is invalid."];
                }
                else if (recurrence.Interval <= 0)
                {
                    errors[nameof(recurrence)] = ["Recurrence interval must be greater than zero."];
                }
            }
        }

        if (isShared && !string.IsNullOrWhiteSpace(sharingMode) && !ExpensePlanSharingModes.All.Contains(sharingMode.Trim()))
        {
            errors[nameof(sharingMode)] = ["Sharing mode is invalid."];
        }

        return errors;
    }
}
