using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Users.Endpoints;

public static class GetUserPreferencesEndpoint
{
    public static async Task<IResult> HandleAsync(
        UserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.GetPreferencesAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
