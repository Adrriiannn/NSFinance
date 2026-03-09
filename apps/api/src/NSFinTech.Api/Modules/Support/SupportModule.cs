using NSFinTech.Api.Modules.Support.Endpoints;

namespace NSFinTech.Api.Modules.Support;

public static class SupportModule
{
    public static IEndpointRouteBuilder MapSupportModule(this IEndpointRouteBuilder app)
    {
        var publicGroup = app.MapGroup("/api/support")
            .WithTags("Support");

        publicGroup.MapPost("/requests", CreateSupportRequestEndpoint.HandleAsync)
            .WithName("CreateSupportRequest")
            .RequireRateLimiting("support-public");

        var authenticated = app.MapGroup("/api/support")
            .WithTags("Support")
            .RequireAuthorization();

        authenticated.MapGet("/requests/me", GetMySupportRequestsEndpoint.HandleAsync)
            .WithName("GetMySupportRequests");

        authenticated.MapPost("/deletion-requests", CreateDeletionRequestEndpoint.HandleAsync)
            .WithName("CreateDeletionRequest");

        authenticated.MapPost("/export-requests", CreateExportRequestEndpoint.HandleAsync)
            .WithName("CreateExportRequest");

        return app;
    }
}
