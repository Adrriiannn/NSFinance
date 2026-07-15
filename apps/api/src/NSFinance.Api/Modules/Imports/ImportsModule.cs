using Microsoft.AspNetCore.Mvc;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Imports.Endpoints;
using NSFinance.Api.Modules.Imports.Services;

namespace NSFinance.Api.Modules.Imports;

public static class ImportsModule
{
    public static IEndpointRouteBuilder MapImportsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports/statements")
            .WithTags("Statement imports")
            .RequireAuthorization();

        var uploadMetadata = new RequestFormLimitsAttribute
        {
            MultipartBodyLengthLimit = StatementImportUploadPolicy.MaximumMultipartBodyBytes,
            ValueCountLimit = 24,
            ValueLengthLimit = 16 * 1024,
            MultipartHeadersLengthLimit = 8 * 1024
        };

        group.MapPost("/inspect", InspectStatementCsvEndpoint.HandleAsync)
            .WithName("InspectStatementCsv")
            .Accepts<IFormFile>("multipart/form-data")
            .RequireRateLimiting("statement-import-upload")
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(StatementImportUploadPolicy.MaximumMultipartBodyBytes),
                uploadMetadata);

        group.MapPost("/preview", PreviewStatementCsvEndpoint.HandleAsync)
            .WithName("PreviewStatementCsv")
            .Accepts<IFormFile>("multipart/form-data")
            .RequireRateLimiting("statement-import-upload")
            .DisableAntiforgery()
            .WithMetadata(
                new RequestSizeLimitAttribute(StatementImportUploadPolicy.MaximumMultipartBodyBytes),
                uploadMetadata);

        group.MapGet("/{batchId:guid}", GetStatementImportBatchEndpoint.HandleAsync)
            .WithName("GetStatementImportBatch");

        group.MapGet("/{batchId:guid}/rows", GetStatementImportRowsEndpoint.HandleAsync)
            .WithName("GetStatementImportRows");

        group.MapPatch("/{batchId:guid}/review", ReviewStatementImportRowsEndpoint.HandleAsync)
            .WithName("ReviewStatementImportRows")
            .Accepts<ReviewStatementImportRowsRequest>("application/json")
            .RequireRateLimiting("statement-import-mutation")
            .WithMetadata(new RequestSizeLimitAttribute(
                StatementImportReviewPolicy.MaximumRequestBodyBytes));

        return app;
    }
}
