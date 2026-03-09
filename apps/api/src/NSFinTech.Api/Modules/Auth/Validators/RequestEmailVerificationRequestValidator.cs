using NSFinTech.Api.Modules.Auth.DTOs;
using System.Text.RegularExpressions;

namespace NSFinTech.Api.Modules.Auth.Validators;

public static partial class RequestEmailVerificationRequestValidator
{
    public static Dictionary<string, string[]> Validate(RequestEmailVerificationRequest request)
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

        return errors;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
