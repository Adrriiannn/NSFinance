using NSFinTech.Api.Modules.Banking.DTOs;

namespace NSFinTech.Api.Modules.Banking.Validators;

public static class TrueLayerCallbackQueryValidator
{
    public static Dictionary<string, string[]> Validate(TrueLayerCallbackQuery query)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(query.State))
        {
            errors["state"] = ["State is required."];
        }

        var hasCode = !string.IsNullOrWhiteSpace(query.Code);
        var hasError = !string.IsNullOrWhiteSpace(query.Error);
        if (!hasCode && !hasError)
        {
            errors["code"] = ["Authorization code or error must be provided."];
        }

        if (hasCode && hasError)
        {
            errors["code"] = ["Authorization code and provider error cannot both be present."];
        }

        return errors;
    }
}
