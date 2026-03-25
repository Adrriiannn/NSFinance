namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record PasswordPolicyCheckResponse(
    string BreachStatus,
    int MinLength,
    int MaxLength,
    bool HasNumberOrSymbol,
    bool IsLengthValid);
