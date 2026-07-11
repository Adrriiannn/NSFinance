namespace NSFinance.Api.Modules.Banking.Services;

public static class TrueLayerReturnUriContract
{
    public const string LegacyAppPathMarker = "modals/add-account";
    public const string CurrentAppPathMarker = "accounts/connect-bank";

    public const string LegacyDefaultAppReturnUri = "nsfinance://modals/add-account?intent=new";
    public const string CurrentDefaultAppReturnUri = "nsfinance://accounts/connect-bank?intent=new";

    private static readonly HashSet<string> SupportedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nsfinance"
    };

    public static string? Normalize(string? appReturnUri)
    {
        if (string.IsNullOrWhiteSpace(appReturnUri))
        {
            return null;
        }

        if (!Uri.TryCreate(appReturnUri.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!SupportedSchemes.Contains(uri.Scheme.Trim()))
        {
            return null;
        }

        // Support both current and legacy route markers to keep older clients working.
        // URI parsing for custom schemes can split route parts between Host and AbsolutePath.
        var routeCandidate = BuildRouteCandidate(uri);
        if (!ContainsSupportedMarker(routeCandidate))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
    }

    public static string BuildDefaultAppReturnUri() => CurrentDefaultAppReturnUri;

    private static bool ContainsSupportedMarker(string routeCandidate)
    {
        return routeCandidate.Contains(CurrentAppPathMarker, StringComparison.Ordinal)
               || routeCandidate.Contains(LegacyAppPathMarker, StringComparison.Ordinal);
    }

    private static string BuildRouteCandidate(Uri uri)
    {
        var host = uri.Host.Trim().Trim('/');
        var path = uri.AbsolutePath.Trim().Trim('/');
        var combined = $"{host}/{path}".Trim('/');

        return combined.ToLowerInvariant();
    }
}
