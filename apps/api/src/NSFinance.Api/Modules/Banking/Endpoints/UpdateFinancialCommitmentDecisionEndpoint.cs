using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class UpdateFinancialCommitmentDecisionEndpoint
{
    public static async Task<IResult> HandleAsync(
        string commitmentId,
        FinancialCommitmentDecisionRequest request,
        FinancialCommitmentReadService readService,
        UserFinancialCommitmentService commitmentService,
        CancellationToken cancellationToken)
    {
        var liveSource = commitmentId.StartsWith("user_manual:", StringComparison.Ordinal)
            ? null
            : await readService.FindBaseAsync(commitmentId, cancellationToken);
        var result = await commitmentService.DecideAsync(
            commitmentId,
            liveSource,
            request,
            cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : result.Error!.ToApiError();
    }
}
