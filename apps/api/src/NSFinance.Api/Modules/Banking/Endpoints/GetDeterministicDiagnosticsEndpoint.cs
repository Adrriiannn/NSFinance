using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class GetDeterministicDiagnosticsEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid connectionId,
        ICurrentUserProvider currentUserProvider,
        BankConnectionService bankConnectionService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await bankConnectionService.GetDeterministicCategorizationDiagnosticsAsync(
            userId,
            connectionId,
            cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error!.ToApiError();
        }

        return Results.Ok(result.Value);
    }
}
