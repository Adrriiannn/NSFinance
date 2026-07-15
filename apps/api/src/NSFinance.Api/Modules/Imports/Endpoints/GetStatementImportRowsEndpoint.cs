using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.Services;

namespace NSFinance.Api.Modules.Imports.Endpoints;

internal static class GetStatementImportRowsEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid batchId,
        string? cursor,
        int? pageSize,
        string? validationStatus,
        string? duplicateClassification,
        string? reviewDisposition,
        StatementImportBatchService batchService,
        CancellationToken cancellationToken)
    {
        var result = await batchService.GetRowsAsync(
            batchId,
            new StatementImportRowsQuery(
                cursor,
                pageSize ?? 50,
                validationStatus,
                duplicateClassification,
                reviewDisposition),
            cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}
