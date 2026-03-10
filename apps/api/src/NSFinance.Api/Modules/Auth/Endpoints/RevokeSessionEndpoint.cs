using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class RevokeSessionEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid sessionId,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.RevokeSessionAsync(sessionId, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.NoContent();
    }
}
