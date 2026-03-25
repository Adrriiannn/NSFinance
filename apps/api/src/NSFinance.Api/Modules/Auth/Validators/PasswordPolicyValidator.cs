namespace NSFinance.Api.Modules.Auth.Validators;

public static class PasswordPolicyValidator
{
    private const int MinimumLength = 12;
    private const int MaximumLength = 64;

    public static string[] Validate(string password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("Password is required.");
            return errors.ToArray();
        }

        if (password.Length < MinimumLength)
        {
            errors.Add($"Password must be at least {MinimumLength} characters.");
        }

        if (password.Length > MaximumLength)
        {
            errors.Add($"Password must be {MaximumLength} characters or fewer.");
        }

        var hasNumberOrSymbol = password.Any(char.IsDigit) || password.Any(ch => !char.IsLetterOrDigit(ch));
        if (!hasNumberOrSymbol)
        {
            errors.Add("Password must include at least one number or symbol.");
        }

        return errors.ToArray();
    }
}
