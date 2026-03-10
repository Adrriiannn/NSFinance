namespace NSFinance.Api.Modules.Auth.Validators;

public static class PasswordPolicyValidator
{
    public static string[] Validate(string password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("Password is required.");
            return errors.ToArray();
        }

        if (password.Length < 10)
        {
            errors.Add("Password must contain at least 10 characters.");
        }

        if (password.Length > 128)
        {
            errors.Add("Password must not exceed 128 characters.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("Password must include at least one uppercase letter.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("Password must include at least one lowercase letter.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("Password must include at least one number.");
        }

        return errors.ToArray();
    }
}
