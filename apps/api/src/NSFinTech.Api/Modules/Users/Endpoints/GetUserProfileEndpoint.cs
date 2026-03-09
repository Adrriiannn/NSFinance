using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Users.Services;

namespace NSFinTech.Api.Modules.Users.Endpoints;

public static class GetUserProfileEndpoint
{
    public static async Task<IResult> HandleAsync(
        UserService userService,
        CancellationToken cancellationToken)
    {
        var result = await userService.GetProfileAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
