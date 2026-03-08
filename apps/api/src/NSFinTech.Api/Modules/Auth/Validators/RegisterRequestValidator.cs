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

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (request.Password.Length < 8)
        {
            errors["password"] = ["Password must contain at least 8 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName) && request.FirstName.Trim().Length > 100)
        {
            errors["firstName"] = ["First name cannot exceed 100 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.LastName) && request.LastName.Trim().Length > 100)
        {
            errors["lastName"] = ["Last name cannot exceed 100 characters."];
        }

        return errors;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
