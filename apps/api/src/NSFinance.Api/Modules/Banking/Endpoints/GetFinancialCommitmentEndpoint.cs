using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class GetFinancialCommitmentEndpoint
{
    public static async Task<IResult> HandleAsync(
        string commitmentId,
        bool? includeDismissed,
        FinancialCommitmentReadService commitmentReadService,
        CancellationToken cancellationToken)
    {
        var commitment = await commitmentReadService.FindAsync(
            commitmentId,
            includeDismissed == true,
            cancellationToken);
        return commitment is null ? Results.NotFound() : Results.Ok(commitment);
    }
}
