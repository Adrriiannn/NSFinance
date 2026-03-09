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

        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length is < 5 or > 4000)
        {
            errors["message"] = ["Message must be between 5 and 4000 characters."];
        }

        return errors;
    }
}
