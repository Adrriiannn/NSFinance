using NSFinTech.Api.Modules.Support.DTOs;

namespace NSFinTech.Api.Modules.Support.Validators;

public static class CreateSupportRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateSupportRequestRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 80)
        {
            errors["category"] = ["Category is required and must not exceed 80 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Subcategory) || request.Subcategory.Trim().Length > 120)
        {
            errors["subcategory"] = ["Subcategory is required and must not exceed 120 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length is < 4 or > 160)
        {
            errors["title"] = ["Title must be between 4 and 160 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length is < 5 or > 4000)
        {
            errors["message"] = ["Message must be between 5 and 4000 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.ContactEmail) && request.ContactEmail.Trim().Length > 256)
        {
            errors["contactEmail"] = ["Contact email must not exceed 256 characters."];
        }

        if (request.Screenshots is { Count: > 3 })
        {
            errors["screenshots"] = ["Up to 3 screenshots can be attached."];
        }

        if (request.Screenshots is not null)
        {
            var invalidContentType = request.Screenshots
                .Any(x => x.ContentType is not ("image/jpeg" or "image/png" or "image/webp"));
            if (invalidContentType)
            {
                errors["screenshots"] = ["Only JPG, PNG, and WebP screenshots are supported."];
            }
        }

        return errors;
    }
}
