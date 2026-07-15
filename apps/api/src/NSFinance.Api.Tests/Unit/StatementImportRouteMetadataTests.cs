using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSFinance.Api.Modules.Imports;
using NSFinance.Api.Modules.Imports.Services;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportRouteMetadataTests
{
    [Fact]
    public async Task ImportRoutes_AreAuthenticatedAndUploadsAreBoundedRateLimitedForms()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<StatementImportBatchService>();
        builder.Services.AddScoped<StatementImportLifecycleService>();
        builder.Services.AddScoped<StatementImportReviewService>();
        builder.Services.AddScoped<StatementImportUploadService>();
        await using var app = builder.Build();
        app.MapImportsModule();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        var inspect = Find(endpoints, "/api/imports/statements/inspect");
        var preview = Find(endpoints, "/api/imports/statements/preview");
        var batch = Find(endpoints, "/api/imports/statements/{batchId:guid}");
        var rows = Find(endpoints, "/api/imports/statements/{batchId:guid}/rows");
        var review = Find(endpoints, "/api/imports/statements/{batchId:guid}/review");
        var commit = Find(endpoints, "/api/imports/statements/{batchId:guid}/commit");
        var discard = Find(endpoints, "/api/imports/statements/{batchId:guid}/discard");
        var undo = Find(endpoints, "/api/imports/statements/{batchId:guid}/undo");

        Assert.All([inspect, preview, batch, rows, review, commit, discard, undo], endpoint =>
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
        AssertUploadMetadata(inspect);
        AssertUploadMetadata(preview);
        AssertMutationMetadata(review, StatementImportReviewPolicy.MaximumRequestBodyBytes);
        AssertMutationMetadata(commit, StatementImportLifecyclePolicy.MaximumRequestBodyBytes);
        AssertMutationMetadata(discard, StatementImportLifecyclePolicy.MaximumRequestBodyBytes);
        AssertMutationMetadata(undo, StatementImportLifecyclePolicy.MaximumRequestBodyBytes);
    }

    private static RouteEndpoint Find(
        IReadOnlyList<RouteEndpoint> endpoints,
        string pattern) =>
        Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == pattern);

    private static void AssertUploadMetadata(RouteEndpoint endpoint)
    {
        var rateLimit = Assert.Single(
            endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>());
        Assert.Equal("statement-import-upload", rateLimit.PolicyName);

        var antiforgery = Assert.Single(
            endpoint.Metadata.GetOrderedMetadata<IAntiforgeryMetadata>());
        Assert.False(antiforgery.RequiresValidation);

        var requestLimit = Assert.Single(
            endpoint.Metadata.GetOrderedMetadata<RequestSizeLimitAttribute>());
        Assert.Equal(
            StatementImportUploadPolicy.MaximumMultipartBodyBytes,
            ((IRequestSizeLimitMetadata)requestLimit).MaxRequestBodySize);

        var formLimit = Assert.Single(
            endpoint.Metadata.GetOrderedMetadata<RequestFormLimitsAttribute>());
        Assert.Equal(StatementImportUploadPolicy.MaximumMultipartBodyBytes, formLimit.MultipartBodyLengthLimit);
        Assert.Equal(24, formLimit.ValueCountLimit);
        Assert.Equal(16 * 1024, formLimit.ValueLengthLimit);
    }

    private static void AssertMutationMetadata(RouteEndpoint endpoint, long maximumRequestBodyBytes)
    {
        var rateLimit = Assert.Single(
            endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>());
        Assert.Equal("statement-import-mutation", rateLimit.PolicyName);

        var requestLimit = Assert.Single(
            endpoint.Metadata.GetOrderedMetadata<RequestSizeLimitAttribute>());
        Assert.Equal(
            maximumRequestBodyBytes,
            ((IRequestSizeLimitMetadata)requestLimit).MaxRequestBodySize);
    }
}
