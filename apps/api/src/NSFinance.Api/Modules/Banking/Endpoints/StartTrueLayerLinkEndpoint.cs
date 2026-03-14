using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class StartTrueLayerLinkEndpoint
{
    public static async Task<IResult> HandleAsync(
        ICurrentUserProvider currentUserProvider,
        StartTrueLayerLinkRequest? request,
        TrueLayerAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized).Error!.ToApiError();
        }

        var result = await authService.StartLinkAsync(userId, request?.AppReturnUri, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
