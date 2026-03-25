using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class RegisterEndpoint
{
    public static async Task<IResult> HandleAsync(
        RegisterRequest request,
        AuthService authService,
        PasswordPolicyService passwordPolicyService,
        TurnstileVerificationService turnstileVerificationService,
        CancellationToken cancellationToken)
    {
        var errors = RegisterRequestValidator.Validate(request);
        if (errors.Count == 0)
        {
            var passwordPolicy = await passwordPolicyService.EvaluateAsync(request.Password, cancellationToken);
            foreach (var entry in PasswordPolicyService.ToValidationErrors("password", passwordPolicy))
            {
                errors[entry.Key] = entry.Value;
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var captchaResult = await turnstileVerificationService.VerifyRegisterTokenAsync(
            request.CaptchaToken,
            cancellationToken);
        if (!captchaResult.Succeeded)
        {
            return captchaResult.Error!.ToApiError();
        }

        var result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
