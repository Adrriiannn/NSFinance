using NSFinance.Api.Modules.AI.Endpoints;

namespace NSFinance.Api.Modules.AI;

public static class AIModule
{
    public static IEndpointRouteBuilder MapAIModule(this IEndpointRouteBuilder app)
    {
        var chat = app.MapGroup("/api/ai/chat")
            .WithTags("AI")
            .RequireAuthorization();

        chat.MapPost("/send", SendChatMessageEndpoint.HandleAsync)
            .WithName("SendAIChatMessage");

        chat.MapGet("/threads", GetChatThreadsEndpoint.HandleAsync)
            .WithName("GetAIChatThreads");

        chat.MapGet("/threads/{threadId:guid}", GetChatThreadEndpoint.HandleAsync)
            .WithName("GetAIChatThread");

        chat.MapPost("/threads/{threadId:guid}/archive", ArchiveChatThreadEndpoint.HandleAsync)
            .WithName("ArchiveAIChatThread");

        app.MapGet("/api/ai/places/photos", GetPlacePhotoEndpoint.HandleAsync)
            .WithTags("AI")
            .WithName("GetAIPlacePhotos")
            .RequireRateLimiting("places-photo");

        app.MapGet("/api/ai/places/photo", GetPlacePhotoEndpoint.HandleAsync)
            .WithTags("AI")
            .WithName("GetAIPlacePhoto")
            .RequireRateLimiting("places-photo");

        var internalAi = app.MapGroup("/api/internal/ai")
            .WithTags("AIInternal")
            .RequireAuthorization("SupportOrAdmin");

        internalAi.MapPost("/merchant-investigation/test", TestMerchantInvestigationEndpoint.HandleAsync)
            .WithName("TestMerchantInvestigation");

        return app;
    }
}
