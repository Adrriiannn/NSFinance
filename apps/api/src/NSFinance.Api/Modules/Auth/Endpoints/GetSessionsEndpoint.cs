using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Auth.Services;

namespace NSFinance.Api.Modules.Auth.Endpoints;

public static class GetSessionsEndpoint
{
    public static async Task<IResult> HandleAsync(
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.GetSessionsAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
