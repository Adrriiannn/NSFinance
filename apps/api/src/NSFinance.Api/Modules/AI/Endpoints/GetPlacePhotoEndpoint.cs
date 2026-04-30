using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Modules.AI.Endpoints;

public static class GetPlacePhotoEndpoint
{
    public static async Task<IResult> HandleAsync(
        string name,
        int? maxWidthPx,
        int? maxHeightPx,
        IGooglePlacesPhotoService photoService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await photoService.ResolvePhotoAsync(
            new GooglePlacesPhotoMediaRequest(
                PhotoResourceName: name,
                MaxWidthPx: maxWidthPx,
                MaxHeightPx: maxHeightPx,
                SkipHttpRedirect: true),
            cancellationToken);

        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.RedirectUri))
        {
            httpContext.Response.Headers.CacheControl = "private, max-age=1800";
            return Results.Redirect(result.RedirectUri, permanent: false);
        }

        var statusCode = result.ErrorCode switch
        {
            "invalid_photo_name" => StatusCodes.Status400BadRequest,
            "places_disabled" => StatusCodes.Status503ServiceUnavailable,
            "places_photo_timeout" => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Problem(
            title: result.ErrorCode ?? "places_photo_provider_error",
            detail: result.ErrorMessage,
            statusCode: statusCode);
    }
}
