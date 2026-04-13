using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class GetTrueLayerConnectionAttemptStatusEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid attemptId,
        string? token,
        BankConnectionAttemptService attemptService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.BadRequest(new { code = "attempt_token_required", message = "A callback attempt token is required." });
        }

        var status = await attemptService.GetPublicStatusAsync(attemptId, token, cancellationToken);
        if (status is null)
        {
            return Results.NotFound(new { code = "attempt_not_found", message = "Connection attempt not found." });
        }

        return Results.Ok(status);
    }
}
