using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Users.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class ConfirmBankConnectionAttemptReturnEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid attemptId,
        ICurrentUserProvider currentUserProvider,
        BankConnectionAttemptService attemptService,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized).Error!.ToApiError();
        }

        var status = await attemptService.ConfirmAppReturnHandledAsync(userId, attemptId, cancellationToken);
        if (status is null)
        {
            return ServiceResult.Fail(
                    "Connection attempt not found.",
                    "bank_connection_attempt_not_found",
                    StatusCodes.Status404NotFound)
                .Error!
                .ToApiError();
        }

        return Results.Ok(status);
    }
}
