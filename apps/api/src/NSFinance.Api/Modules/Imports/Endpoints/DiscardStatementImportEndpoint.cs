using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Imports.Services;

namespace NSFinance.Api.Modules.Imports.Endpoints;

internal static class DiscardStatementImportEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid batchId,
        StatementImportRevisionRequest request,
        StatementImportLifecycleService lifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await lifecycleService.DiscardAsync(
            batchId,
            new StatementImportRevisionCommand(request.ExpectedRevision),
            cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}
