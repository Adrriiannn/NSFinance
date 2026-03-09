using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Banking.Services;
using NSFinTech.Api.Modules.Users.Services;

namespace NSFinTech.Api.Modules.Banking.Endpoints;

public static class StartTrueLayerLinkEndpoint
{
    public static async Task<IResult> HandleAsync(
        ICurrentUserProvider currentUserProvider,
        TrueLayerAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized).Error!.ToApiError();
        }

        var result = await authService.StartLinkAsync(userId, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
