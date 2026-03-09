using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Auth.Services;

namespace NSFinTech.Api.Modules.Auth.Endpoints;

public static class LogoutEndpoint
{
    public static async Task<IResult> HandleAsync(
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.LogoutCurrentSessionAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.NoContent();
    }
}
