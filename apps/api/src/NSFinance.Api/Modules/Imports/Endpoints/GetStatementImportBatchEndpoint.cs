using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.Services;

namespace NSFinance.Api.Modules.Imports.Endpoints;

internal static class GetStatementImportBatchEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid batchId,
        StatementImportBatchService batchService,
        CancellationToken cancellationToken)
    {
        var result = await batchService.GetAsync(batchId, cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}
