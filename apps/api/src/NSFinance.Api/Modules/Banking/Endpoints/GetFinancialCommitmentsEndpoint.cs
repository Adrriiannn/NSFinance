using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class GetFinancialCommitmentsEndpoint
{
    public static async Task<IResult> HandleAsync(
        int? limit,
        FinancialCommitmentReadService commitmentReadService,
        CancellationToken cancellationToken)
    {
        var result = await commitmentReadService.ListAsync(limit, cancellationToken);
        return result.Succeeded
            ? Results.Ok(result.Value)
            : result.Error!.ToApiError();
    }
}
