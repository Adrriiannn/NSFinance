using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceLocationBoundaryService(
    IOptions<AIIntegrationOptions> options,
    IChatTelemetry telemetry) : ICompanionPlaceLocationBoundaryService
{
    public CompanionPlaceLocationBoundaryPlan CreatePlan(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy? strategy = null)
    {
        var architecture = options.Value.Architecture;
        var rawLocation = ResolveRawLocation(request, intent, strategy);
        var gpsLatitude = strategy?.Location.Latitude ?? intent.Location.Latitude ?? TryReadDouble(request, CompanionLocationMetadataKeys.Latitude);
        var gpsLongitude = strategy?.Location.Longitude ?? intent.Location.Longitude ?? TryReadDouble(request, CompanionLocationMetadataKeys.Longitude);
        var countryCode = ResolveCountryCode(request, strategy) ?? "IE";

        CompanionPlaceLocationBoundaryPlan plan;
        if (IsTooBroad(rawLocation))
        {
            plan = new CompanionPlaceLocationBoundaryPlan(
                "too_broad",
                rawLocation,
                rawLocation,
                countryCode,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                [],
                true,
                false,
                0.8d,
                ["places_location_too_broad"]);
        }
        else if (IsDistrict(rawLocation, out var district))
        {
            plan = new CompanionPlaceLocationBoundaryPlan(
                "explicit_district",
                rawLocation,
                $"{district}, Dublin, Ireland",
                "IE",
                null,
                "Dublin",
                district,
                null,
                null,
                null,
                architecture.PlacesExplicitCityDefaultRadiusMeters,
                [district, "Dublin"],
                ["Ireland"],
                ["United Kingdom", "England", "USA", "California", "London"],
                true,
                false,
                0.86d,
                []);
        }
        else if (IsDublin(rawLocation))
        {
            plan = new CompanionPlaceLocationBoundaryPlan(
                "explicit_city",
                rawLocation,
                "Dublin, Ireland",
                "IE",
                null,
                "Dublin",
                null,
                null,
                53.3498d,
                -6.2603d,
                architecture.PlacesExplicitCityDefaultRadiusMeters,
                ["Dublin"],
                ["Ireland", "D0", "Dublin"],
                ["United Kingdom", "England", "USA", "California", "London"],
                true,
                false,
                0.9d,
                []);
        }
        else if (intent.Location.Mode == "near_me" || strategy?.Location.Mode == "near_me")
        {
            plan = new CompanionPlaceLocationBoundaryPlan(
                "near_me_radius",
                rawLocation ?? "near me",
                "near me",
                countryCode,
                null,
                null,
                null,
                null,
                gpsLatitude,
                gpsLongitude,
                architecture.PlacesNearMeDefaultRadiusMeters,
                [],
                [],
                [],
                gpsLatitude.HasValue && gpsLongitude.HasValue,
                false,
                gpsLatitude.HasValue && gpsLongitude.HasValue ? 0.9d : 0.35d,
                gpsLatitude.HasValue && gpsLongitude.HasValue ? [] : ["places_location_missing_gps"]);
        }
        else
        {
            plan = new CompanionPlaceLocationBoundaryPlan(
                "unknown",
                rawLocation,
                rawLocation,
                countryCode,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                [],
                false,
                false,
                0.35d,
                []);
        }

        _ = telemetry.TrackAsync(
            "places.location_boundary.plan_created",
            new Dictionary<string, object?>
            {
                ["mode"] = plan.BoundaryMode,
                ["rawLocation"] = plan.RawLocationText,
                ["canonicalLocation"] = plan.CanonicalLocationText,
                ["hardBoundary"] = plan.HardBoundary,
                ["radiusMeters"] = plan.RadiusMeters
            },
            CancellationToken.None);

        return plan;
    }

    private static string? ResolveRawLocation(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy? strategy)
    {
        if (!string.IsNullOrWhiteSpace(strategy?.Location.AreaText))
        {
            return strategy.Location.AreaText;
        }

        if (!string.IsNullOrWhiteSpace(intent.Location.AreaText))
        {
            return intent.Location.AreaText;
        }

        var message = request.UserMessage ?? string.Empty;
        var district = RegexDublinDistrict().Match(message);
        if (district.Success)
        {
            return district.Value;
        }

        if (message.Contains("Dublin", StringComparison.OrdinalIgnoreCase))
        {
            return "Dublin";
        }

        if (message.Contains("near me", StringComparison.OrdinalIgnoreCase)
            || message.Contains("nearby", StringComparison.OrdinalIgnoreCase)
            || message.Contains("around me", StringComparison.OrdinalIgnoreCase))
        {
            return "near me";
        }

        return null;
    }

    private static bool IsDublin(string? rawLocation)
    {
        return rawLocation?.Contains("Dublin", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsDistrict(string? rawLocation, out string district)
    {
        district = string.Empty;
        if (string.IsNullOrWhiteSpace(rawLocation))
        {
            return false;
        }

        var match = RegexDublinDistrict().Match(rawLocation);
        if (!match.Success)
        {
            return false;
        }

        district = match.Value.ToUpperInvariant();
        if (district.StartsWith("D", StringComparison.OrdinalIgnoreCase) && district.Length == 3)
        {
            district = $"Dublin {int.Parse(district[1..], CultureInfo.InvariantCulture)}";
        }

        return true;
    }

    private static bool IsTooBroad(string? rawLocation)
    {
        var normalized = rawLocation?.Trim().ToLowerInvariant();
        return normalized is "ireland" or "united states" or "usa" or "europe" or "worldwide";
    }

    private static string? ResolveCountryCode(UserChatRequest request, CompanionPlaceSearchStrategy? strategy)
    {
        if (request.Metadata?.TryGetValue("country_code", out var value) == true && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().ToUpperInvariant();
        }

        return null;
    }

    private static double? TryReadDouble(UserChatRequest request, string key)
    {
        return request.Metadata?.TryGetValue(key, out var value) == true
               && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static Regex RegexDublinDistrict() => new(@"\b(?:Dublin\s*(?:[1-9]|1[0-9]|2[024])|D0?[1-9]|D1[0-9]|D2[024])\b", RegexOptions.IgnoreCase);
}

public sealed class CompanionPlaceLocationBoundaryFilter(
    IOptions<AIIntegrationOptions> options,
    IChatTelemetry telemetry) : ICompanionPlaceLocationBoundaryFilter
{
    public IReadOnlyList<CompanionPlacePoolCandidate> Apply(
        CompanionPlaceLocationBoundaryPlan plan,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        if (!options.Value.Architecture.PlacesLocationBoundaryEnabled
            || !plan.HardBoundary
            || plan.BoundaryMode is "unknown")
        {
            return candidates;
        }

        if (plan.BoundaryMode == "too_broad")
        {
            _ = telemetry.TrackAsync("places.location_boundary.too_broad", new Dictionary<string, object?> { ["rawLocation"] = plan.RawLocationText }, CancellationToken.None);
            return [];
        }

        var decisions = candidates
            .Select(candidate => Decide(plan, candidate))
            .ToArray();
        var kept = candidates
            .Where(candidate => decisions.First(decision => decision.PlaceId == candidate.PlaceId).IsInsideBoundary)
            .Take(Math.Clamp(options.Value.Architecture.PlacesMaxBoundaryFilteredCandidates, 1, 50))
            .ToArray();

        _ = telemetry.TrackAsync(
            "places.location_boundary.filter_applied",
            new Dictionary<string, object?>
            {
                ["mode"] = plan.BoundaryMode,
                ["rawLocation"] = plan.RawLocationText,
                ["canonicalLocation"] = plan.CanonicalLocationText,
                ["candidateCountBefore"] = candidates.Count,
                ["candidateCountAfter"] = kept.Length,
                ["rejectedOutsideBoundary"] = candidates.Count - kept.Length
            },
            CancellationToken.None);

        return kept;
    }

    private static CompanionPlaceLocationBoundaryDecision Decide(
        CompanionPlaceLocationBoundaryPlan plan,
        CompanionPlacePoolCandidate candidate)
    {
        if (plan.CenterLatitude.HasValue
            && plan.CenterLongitude.HasValue
            && candidate.Latitude.HasValue
            && candidate.Longitude.HasValue)
        {
            var distance = DistanceMeters(plan.CenterLatitude.Value, plan.CenterLongitude.Value, candidate.Latitude.Value, candidate.Longitude.Value);
            var inside = distance <= (plan.RadiusMeters ?? 15_000d);
            return new CompanionPlaceLocationBoundaryDecision(candidate.PlaceId, inside, 0.95d, [$"distance_meters:{Math.Round(distance)}"], inside ? [] : ["outside_near_me_radius"]);
        }

        var address = $"{candidate.ShortFormattedAddress} {candidate.LightweightAttributes.GetValueOrDefault("formatted_address")}".Trim();
        if (plan.AddressMustContain.Count > 0)
        {
            var containsMust = plan.AddressMustContain.All(term => address.Contains(term, StringComparison.OrdinalIgnoreCase));
            var containsForbidden = plan.AddressMustNotContain.Any(term => address.Contains(term, StringComparison.OrdinalIgnoreCase));
            return new CompanionPlaceLocationBoundaryDecision(candidate.PlaceId, containsMust && !containsForbidden, containsMust ? 0.85d : 0.65d, ["address_boundary"], containsForbidden ? ["address_forbidden_area"] : []);
        }

        return new CompanionPlaceLocationBoundaryDecision(candidate.PlaceId, true, 0.4d, ["boundary_not_applicable"], []);
    }

    private static double DistanceMeters(double sourceLatitude, double sourceLongitude, double latitude, double longitude)
    {
        const double EarthRadiusMeters = 6_371_000d;
        var sourceLatRad = DegreesToRadians(sourceLatitude);
        var targetLatRad = DegreesToRadians(latitude);
        var deltaLat = DegreesToRadians(latitude - sourceLatitude);
        var deltaLon = DegreesToRadians(longitude - sourceLongitude);
        var a = Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d)
                + (Math.Cos(sourceLatRad) * Math.Cos(targetLatRad)
                   * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d));
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double value) => value * (Math.PI / 180d);
}
