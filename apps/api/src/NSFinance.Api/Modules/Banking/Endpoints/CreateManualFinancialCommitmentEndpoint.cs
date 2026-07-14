using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;

namespace NSFinance.Api.Modules.Banking.Endpoints;

public static class CreateManualFinancialCommitmentEndpoint
{
    public static async Task<IResult> HandleAsync(
        CreateManualFinancialCommitmentRequest request,
        UserFinancialCommitmentService commitmentService,
        CancellationToken cancellationToken)
    {
        var result = await commitmentService.CreateManualAsync(request, cancellationToken);
        return result.Succeeded
            ? Results.Created($"/api/banking/commitments/{result.Value!.Id}", result.Value)
            : result.Error!.ToApiError();
    }
}
