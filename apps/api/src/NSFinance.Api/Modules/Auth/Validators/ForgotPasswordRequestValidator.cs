using NSFinance.Api.Modules.Auth.DTOs;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.Auth.Validators;

public static partial class ForgotPasswordRequestValidator
{
    public static Dictionary<string, string[]> Validate(ForgotPasswordRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Identity))
        {
            errors["identity"] = ["Email or phone number is required."];
        }
        else if (!EmailPattern().IsMatch(request.Identity.Trim())
            && !PhonePattern().IsMatch(request.Identity.Trim()))
        {
            errors["identity"] = ["Enter a valid email address or international phone number."];
        }

        return errors;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
