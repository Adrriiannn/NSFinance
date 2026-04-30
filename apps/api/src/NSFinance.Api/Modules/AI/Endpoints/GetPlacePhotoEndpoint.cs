using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;

namespace NSFinance.Api.Modules.AI.Endpoints;

public static class GetPlacePhotoEndpoint
{
    public static IResult Handle(
        string name,
        int? maxWidthPx,
        int? maxHeightPx,
        IOptions<GooglePlacesOptions> options)
    {
        if (string.IsNullOrWhiteSpace(name) || !IsValidPhotoName(name))
        {
            return Results.BadRequest();
        }

        var placesOptions = options.Value;
        if (!placesOptions.Enabled || string.IsNullOrWhiteSpace(placesOptions.ApiKey))
        {
            return Results.NotFound();
        }

        var width = Math.Clamp(maxWidthPx ?? 1200, 100, 1600);
        var height = maxHeightPx.HasValue ? Math.Clamp(maxHeightPx.Value, 100, 1600) : (int?)null;
        var escapedName = string.Join(
            '/',
            name.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var dimensionQuery = height.HasValue
            ? $"maxHeightPx={height.Value}"
            : $"maxWidthPx={width}";
        var redirectUrl =
            $"https://places.googleapis.com/v1/{escapedName}/media?{dimensionQuery}&key={Uri.EscapeDataString(placesOptions.ApiKey)}";

        return Results.Redirect(redirectUrl, permanent: false);
    }

    private static bool IsValidPhotoName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith("places/", StringComparison.Ordinal)
               && trimmed.Contains("/photos/", StringComparison.Ordinal)
               && !trimmed.Contains("..", StringComparison.Ordinal)
               && trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 4
               && trimmed.Length <= 256;
    }
}
