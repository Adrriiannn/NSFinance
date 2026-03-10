using NSFinance.Api.Modules.Auth.DTOs;

namespace NSFinance.Api.Modules.Auth.Validators;

public static class LoginRequestValidator
{
    public static Dictionary<string, string[]> Validate(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (request.Password.Length > 256)
        {
            errors["password"] = ["Password is invalid."];
        }

        return errors;
    }
}
