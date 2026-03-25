using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Services;

public static class PasswordPolicyErrorCodes
{
    public const string PasswordRequired = "PasswordRequired";
    public const string PasswordTooShort = "PasswordTooShort";
    public const string PasswordTooLong = "PasswordTooLong";
    public const string PasswordRequiresNumberOrSymbol = "PasswordRequiresNumberOrSymbol";
    public const string PasswordCompromised = "PasswordCompromised";
    public const string PasswordCheckUnavailable = "PasswordCheckUnavailable";
}

public sealed record PasswordPolicyIssue(string Code, string Message);

public sealed record PasswordPolicyEvaluation(
    IReadOnlyList<PasswordPolicyIssue> Issues,
    int MinLength,
    int MaxLength,
    bool HasNumberOrSymbol,
    bool BreachCheckAvailable,
    bool IsCompromised)
{
    public bool IsLengthValid => Issues.All(issue =>
        issue.Code != PasswordPolicyErrorCodes.PasswordTooShort
        && issue.Code != PasswordPolicyErrorCodes.PasswordTooLong
        && issue.Code != PasswordPolicyErrorCodes.PasswordRequired);

    public bool IsValid => Issues.Count == 0;
}

public sealed class PasswordPolicyService(
    IOptions<PasswordPolicyOptions> options,
    PwnedPasswordService pwnedPasswordService)
{
    private readonly PasswordPolicyOptions _options = options.Value;

    public PasswordPolicyEvaluation EvaluateLocal(string password)
    {
        var issues = new List<PasswordPolicyIssue>();

        if (string.IsNullOrWhiteSpace(password))
        {
            issues.Add(new PasswordPolicyIssue(
                PasswordPolicyErrorCodes.PasswordRequired,
                "Password is required."));

            return new PasswordPolicyEvaluation(
                issues,
                _options.MinLength,
                _options.MaxLength,
                HasNumberOrSymbol: false,
                BreachCheckAvailable: true,
                IsCompromised: false);
        }

        if (password.Length < _options.MinLength)
        {
            issues.Add(new PasswordPolicyIssue(
                PasswordPolicyErrorCodes.PasswordTooShort,
                $"Password must be at least {_options.MinLength} characters."));
        }

        if (password.Length > _options.MaxLength)
        {
            issues.Add(new PasswordPolicyIssue(
                PasswordPolicyErrorCodes.PasswordTooLong,
                $"Password must be {_options.MaxLength} characters or fewer."));
        }

        var hasNumberOrSymbol = password.Any(char.IsDigit) || password.Any(ch => !char.IsLetterOrDigit(ch));
        if (_options.RequireNumberOrSymbol && !hasNumberOrSymbol)
        {
            issues.Add(new PasswordPolicyIssue(
                PasswordPolicyErrorCodes.PasswordRequiresNumberOrSymbol,
                "Password must include at least one number or symbol."));
        }

        return new PasswordPolicyEvaluation(
            issues,
            _options.MinLength,
            _options.MaxLength,
            hasNumberOrSymbol,
            BreachCheckAvailable: true,
            IsCompromised: false);
    }

    public async Task<PasswordPolicyEvaluation> EvaluateAsync(string password, CancellationToken cancellationToken)
    {
        var local = EvaluateLocal(password);
        if (string.IsNullOrWhiteSpace(password))
        {
            return local;
        }

        if (local.Issues.Any(issue =>
            issue.Code == PasswordPolicyErrorCodes.PasswordTooShort
            || issue.Code == PasswordPolicyErrorCodes.PasswordTooLong))
        {
            return local;
        }

        var pwnedStatus = await pwnedPasswordService.CheckAsync(password, cancellationToken);
        if (pwnedStatus == PwnedPasswordCheckStatus.Safe)
        {
            return local;
        }

        var issues = local.Issues.ToList();
        if (pwnedStatus == PwnedPasswordCheckStatus.Compromised)
        {
            issues.Add(new PasswordPolicyIssue(
                PasswordPolicyErrorCodes.PasswordCompromised,
                "This password has appeared in known data breaches. Choose a different one."));

            return new PasswordPolicyEvaluation(
                issues,
                local.MinLength,
                local.MaxLength,
                local.HasNumberOrSymbol,
                BreachCheckAvailable: true,
                IsCompromised: true);
        }

        issues.Add(new PasswordPolicyIssue(
            PasswordPolicyErrorCodes.PasswordCheckUnavailable,
            "Unable to verify password safety right now. Please try again."));

        return new PasswordPolicyEvaluation(
            issues,
            local.MinLength,
            local.MaxLength,
            local.HasNumberOrSymbol,
            BreachCheckAvailable: false,
            IsCompromised: false);
    }

    public static Dictionary<string, string[]> ToValidationErrors(string fieldName, PasswordPolicyEvaluation evaluation)
    {
        if (evaluation.IsValid)
        {
            return [];
        }

        return new Dictionary<string, string[]>
        {
            [fieldName] = evaluation.Issues.Select(issue => issue.Message).Distinct().ToArray()
        };
    }
}
