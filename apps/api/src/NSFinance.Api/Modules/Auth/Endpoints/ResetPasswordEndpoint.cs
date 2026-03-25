using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class ResetPasswordEndpoint
{
    public static async Task<IResult> HandleAsync(
        ResetPasswordRequest request,
        AuthService authService,
        PasswordPolicyService passwordPolicyService,
        CancellationToken cancellationToken)
    {
        var errors = ResetPasswordRequestValidator.Validate(request);
        if (errors.Count == 0)
        {
            var passwordPolicy = await passwordPolicyService.EvaluateAsync(request.NewPassword, cancellationToken);
            foreach (var entry in PasswordPolicyService.ToValidationErrors("newPassword", passwordPolicy))
            {
                errors[entry.Key] = entry.Value;
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await authService.ResetPasswordAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
