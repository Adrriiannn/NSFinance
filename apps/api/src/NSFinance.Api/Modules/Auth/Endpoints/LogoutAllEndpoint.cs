using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class LogoutAllEndpoint
{
    public static async Task<IResult> HandleAsync(
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.LogoutAllAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(new { revokedSessions = result.Value });
    }
}
