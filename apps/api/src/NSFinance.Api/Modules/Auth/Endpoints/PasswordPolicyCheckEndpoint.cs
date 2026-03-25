using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class PasswordPolicyCheckEndpoint
{
    public static async Task<IResult> HandleAsync(
        PasswordPolicyCheckRequest request,
        PasswordPolicyService passwordPolicyService,
        CancellationToken cancellationToken)
    {
        var evaluation = await passwordPolicyService.EvaluateAsync(
            request.Password ?? string.Empty,
            cancellationToken);

        var breachStatus = evaluation.IsCompromised
            ? "compromised"
            : evaluation.BreachCheckAvailable
                ? "safe"
                : "unavailable";

        var response = new PasswordPolicyCheckResponse(
            breachStatus,
            evaluation.MinLength,
            evaluation.MaxLength,
            evaluation.HasNumberOrSymbol,
            evaluation.IsLengthValid);

        return Results.Ok(response);
    }
}
