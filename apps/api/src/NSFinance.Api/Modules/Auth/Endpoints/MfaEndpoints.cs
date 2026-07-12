using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class GetMfaStatusEndpoint
{
    public static async Task<IResult> HandleAsync(
        TotpMfaService mfaService,
        CancellationToken cancellationToken)
    {
        var result = await mfaService.GetStatusAsync(cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}

public static class BeginTotpEnrollmentEndpoint
{
    public static async Task<IResult> HandleAsync(
        TotpMfaService mfaService,
        CancellationToken cancellationToken)
    {
        var result = await mfaService.BeginEnrollmentAsync(cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}

public static class ConfirmTotpEnrollmentEndpoint
{
    public static async Task<IResult> HandleAsync(
        ConfirmTotpEnrollmentRequest request,
        TotpMfaService mfaService,
        CancellationToken cancellationToken)
    {
        if (request.AuthenticatorId == Guid.Empty
            || request.Code.Length != 6
            || !request.Code.All(char.IsDigit))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["Enter the six-digit authenticator code."]
            });
        }

        var result = await mfaService.ConfirmEnrollmentAsync(request, cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}

public static class VerifyMfaLoginEndpoint
{
    public static async Task<IResult> HandleAsync(
        VerifyMfaLoginRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        if (request.ChallengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ChallengeToken)
            || string.IsNullOrWhiteSpace(request.Code)
            || (request.Method != "totp" && request.Method != "recovery_code"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["Enter a valid authentication or recovery code."]
            });
        }

        var result = await authService.VerifyMfaLoginAsync(request, cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}

public static class DisableMfaEndpoint
{
    public static async Task<IResult> HandleAsync(
        DisableMfaRequest request,
        TotpMfaService mfaService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code)
            || (request.Method != "totp" && request.Method != "recovery_code"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["Enter a valid authentication or recovery code."]
            });
        }

        var result = await mfaService.DisableAsync(request, cancellationToken);
        return result.Succeeded ? Results.NoContent() : result.Error!.ToApiError();
    }
}
