using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class VerifyPasswordRecoveryCodeEndpoint
{
    public static async Task<IResult> HandleAsync(
        VerifyPasswordRecoveryCodeRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        if (request.ChallengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Code)
            || request.Code.Length != 6
            || !request.Code.All(char.IsDigit))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["Enter the six-digit code."]
            });
        }

        var result = await authService.VerifyPasswordRecoveryCodeAsync(request, cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : result.Error!.ToApiError();
    }
}
