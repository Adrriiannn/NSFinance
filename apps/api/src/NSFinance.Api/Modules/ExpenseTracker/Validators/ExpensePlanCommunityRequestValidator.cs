using NSFinance.Api.Modules.ExpenseTracker.DTOs;
using NSFinance.Api.Modules.ExpenseTracker.Models;

namespace NSFinance.Api.Modules.ExpenseTracker.Validators;

public static class ExpensePlanCommunityRequestValidator
{
    public static Dictionary<string, string[]> ValidatePublish(PublishExpensePlanRequest request)
    {
        return ValidateMetadata(request.SourcePlanId, request.PublicTitle, request.PublicDescription, request.Tags);
    }

    public static Dictionary<string, string[]> ValidateUpdate(UpdateExpensePlanPublicationRequest request)
    {
        return ValidateMetadata(Guid.Empty, request.PublicTitle, request.PublicDescription, request.Tags, requireSourcePlan: false);
    }

    public static Dictionary<string, string[]> ValidateReport(ReportExpensePlanPublicationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!ExpensePlanReportReasons.All.Contains(request.Reason?.Trim() ?? string.Empty))
        {
            errors["reason"] = ["Choose a valid report reason."];
        }

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > 1200)
        {
            errors["notes"] = ["Report note cannot exceed 1200 characters."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateMetadata(
        Guid? sourcePlanId,
        string publicTitle,
        string? publicDescription,
        IReadOnlyList<string>? tags,
        bool requireSourcePlan = true)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (requireSourcePlan && sourcePlanId.HasValue == false)
        {
            errors["sourcePlanId"] = ["Choose a source plan to publish."];
        }

        if (string.IsNullOrWhiteSpace(publicTitle) || publicTitle.Trim().Length < 3)
        {
            errors["publicTitle"] = ["Public title must be at least 3 characters."];
        }
        else if (publicTitle.Trim().Length > 160)
        {
            errors["publicTitle"] = ["Public title cannot exceed 160 characters."];
        }

        if (!string.IsNullOrWhiteSpace(publicDescription) && publicDescription.Trim().Length > 2000)
        {
            errors["publicDescription"] = ["Public description cannot exceed 2000 characters."];
        }

        if ((tags?.Count ?? 0) > 12)
        {
            errors["tags"] = ["Use at most 12 tags."];
        }
        else if (tags?.Any(tag => tag.Trim().Length > 32) == true)
        {
            errors["tags"] = ["Each tag must be 32 characters or fewer."];
        }

        return errors;
    }
}
