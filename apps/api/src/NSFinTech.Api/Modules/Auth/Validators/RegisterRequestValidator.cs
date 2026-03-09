using NSFinTech.Api.Modules.Auth.DTOs;
using System.Text.RegularExpressions;

namespace NSFinTech.Api.Modules.Auth.Validators;

public static partial class RegisterRequestValidator
{
    public static Dictionary<string, string[]> Validate(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (!EmailPattern().IsMatch(request.Email.Trim()))
        {
            errors["email"] = ["Email format is invalid."];
        }

        var passwordErrors = PasswordPolicyValidator.Validate(request.Password);
        if (passwordErrors.Length > 0)
        {
            errors["password"] = passwordErrors;
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors["displayName"] = ["Display name is required."];
        }
        else if (request.DisplayName.Trim().Length is < 2 or > 120)
        {
            errors["displayName"] = ["Display name must be between 2 and 120 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.PreferredCurrency) && request.PreferredCurrency.Trim().Length != 3)
        {
            errors["preferredCurrency"] = ["Preferred currency must be an ISO 3-letter code."];
        }

        return errors;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
