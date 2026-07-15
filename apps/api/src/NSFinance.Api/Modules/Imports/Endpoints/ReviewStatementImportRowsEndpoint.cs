using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Imports.Services;

namespace NSFinance.Api.Modules.Imports.Endpoints;

internal static class ReviewStatementImportRowsEndpoint
{
    public static async Task<IResult> HandleAsync(
        Guid batchId,
        ReviewStatementImportRowsRequest request,
        StatementImportReviewService reviewService,
        CancellationToken cancellationToken)
    {
        var result = await reviewService.ReviewRowsAsync(
            batchId,
            new ReviewStatementImportRowsCommand(
                request.ExpectedRevision,
                request.Decisions?
                    .Select(decision => new StatementImportRowReviewDecision(
                        decision.RowId,
                        decision.ReviewDisposition))
                    .ToList()),
            cancellationToken);
        return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToApiError();
    }
}
