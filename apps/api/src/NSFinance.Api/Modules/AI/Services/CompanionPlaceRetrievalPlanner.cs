using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceRetrievalPlanner(
    IOptions<AIIntegrationOptions> options,
    IChatTelemetry telemetry) : ICompanionPlaceRetrievalPlanner
{
    private static readonly IReadOnlyDictionary<string, string> RoleToGoogleType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hotel"] = "lodging",
        ["lodging"] = "lodging",
        ["ev_charging"] = "electric_vehicle_charging_station",
        ["ev_charging_station"] = "electric_vehicle_charging_station",
        ["electric_vehicle_charging_station"] = "electric_vehicle_charging_station",
        ["atm"] = "atm",
        ["parking"] = "parking",
        ["car_park"] = "parking",
        ["gas_station"] = "gas_station",
        ["petrol_station"] = "gas_station",
        ["restaurant"] = "restaurant",
        ["cafe"] = "cafe",
        ["coffee_shop"] = "cafe",
        ["pharmacy"] = "pharmacy",
        ["gym"] = "gym",
        ["fitness"] = "gym",
        ["hospital"] = "hospital",
        ["doctor"] = "doctor",
        ["dentist"] = "dentist",
        ["vet"] = "veterinary_care",
        ["veterinary_care"] = "veterinary_care",
        ["post_office"] = "post_office",
        ["bank"] = "bank",
        ["bank_branch"] = "bank",
        ["supermarket"] = "grocery_store",
        ["grocery"] = "grocery_store",
        ["convenience_store"] = "convenience_store",
        ["library"] = "library",
        ["museum"] = "museum",
        ["movie_theater"] = "movie_theater",
        ["cinema"] = "movie_theater",
        ["park"] = "park"
    };

    public CompanionPlaceRetrievalPlan Build(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy? strategy,
        CompanionPlaceLocationBoundaryPlan? boundaryPlan = null)
    {
        var architecture = options.Value.Architecture;
        var passes = new List<CompanionPlaceRetrievalPass>();
        var country = boundaryPlan?.CountryCode ?? ResolveCountryCode(request);
        var latitude = boundaryPlan?.CenterLatitude ?? intent.Location.Latitude;
        var longitude = boundaryPlan?.CenterLongitude ?? intent.Location.Longitude;
        var radius = boundaryPlan?.RadiusMeters ?? architecture.PlacesNearMeDefaultRadiusMeters;
        var includedType = ResolveIncludedType(intent, strategy);

        if (architecture.PlacesTypedNearbyRetrievalEnabled
            && includedType is not null
            && latitude.HasValue
            && longitude.HasValue)
        {
            passes.Add(new CompanionPlaceRetrievalPass(
                PassId: $"nearby:{includedType}",
                Mode: "nearby",
                Query: null,
                IncludedTypes: [includedType],
                Latitude: latitude,
                Longitude: longitude,
                RadiusMeters: radius,
                CountryCode: country,
                RequiresLocation: true,
                Purpose: "typed_nearby"));
        }

        foreach (var variant in strategy?.SearchVariants ?? [])
        {
            var query = ApplyExplicitLocation(variant.Query, boundaryPlan);
            passes.Add(new CompanionPlaceRetrievalPass(
                $"text:{passes.Count + 1}",
                "text",
                query,
                [],
                intent.Location.Latitude,
                intent.Location.Longitude,
                intent.Location.Latitude.HasValue ? radius : null,
                country,
                false,
                variant.Purpose is "primary" ? "primary_text" : "alias_text"));
        }

        if (passes.All(static pass => pass.Mode != "text"))
        {
            var query = ApplyExplicitLocation(intent.PlaceQuery ?? request.UserMessage, boundaryPlan);
            passes.Add(new CompanionPlaceRetrievalPass("text:fallback", "text", query, [], intent.Location.Latitude, intent.Location.Longitude, intent.Location.Latitude.HasValue ? radius : null, country, false, "fallback"));
        }

        var distinct = passes
            .Where(static pass => pass.Mode == "nearby" || !string.IsNullOrWhiteSpace(pass.Query))
            .DistinctBy(static pass => $"{pass.Mode}:{pass.Query}:{string.Join(',', pass.IncludedTypes)}", StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        _ = telemetry.TrackAsync(
            "places.retrieval_plan.created",
            new Dictionary<string, object?>
            {
                ["passCount"] = distinct.Length,
                ["typedNearbyEnabled"] = architecture.PlacesTypedNearbyRetrievalEnabled,
                ["includedType"] = includedType,
                ["passes"] = distinct.Select(static pass => new { pass.Mode, pass.Query, pass.IncludedTypes, pass.Purpose }).ToArray()
            },
            CancellationToken.None);

        return new CompanionPlaceRetrievalPlan(distinct, 50, 20, includedType is null ? [] : ["places_retrieval_typed_nearby_available"]);
    }

    private static string? ResolveIncludedType(CompanionSemanticIntent intent, CompanionPlaceSearchStrategy? strategy)
    {
        foreach (var role in new[]
                 {
                     intent.Role.RequestedRole,
                     strategy?.Role.RequestedRole
                 }
                 .Concat(intent.Role.RequiredCoreRoles)
                 .Concat(intent.Role.AcceptableSubRoles)
                 .Concat(strategy?.Role.RequiredCoreRoles ?? [])
                 .Concat(strategy?.Role.AcceptableSubRoles ?? [])
                 .Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            if (RoleToGoogleType.TryGetValue(Normalize(role), out var includedType))
            {
                return includedType;
            }
        }

        var query = Normalize($"{intent.PlaceQuery} {strategy?.CanonicalQuery}");
        if (query.Contains("hotel", StringComparison.Ordinal)) return "lodging";
        if (query.Contains("ev charging", StringComparison.Ordinal) || query.Contains("electric vehicle", StringComparison.Ordinal)) return "electric_vehicle_charging_station";
        if (query.Contains("atm", StringComparison.Ordinal)) return "atm";
        if (query.Contains("car park", StringComparison.Ordinal) || query.Contains("parking", StringComparison.Ordinal)) return "parking";
        return null;
    }

    private static string? ApplyExplicitLocation(string? query, CompanionPlaceLocationBoundaryPlan? boundaryPlan)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        if (boundaryPlan?.BoundaryMode is "explicit_city" or "explicit_district"
            && !string.IsNullOrWhiteSpace(boundaryPlan.RawLocationText)
            && !query.Contains(boundaryPlan.RawLocationText, StringComparison.OrdinalIgnoreCase))
        {
            return $"{query} {boundaryPlan.RawLocationText}";
        }

        return query;
    }

    private static string? ResolveCountryCode(UserChatRequest request)
    {
        return request.Metadata?.TryGetValue("country_code", out var value) == true && !string.IsNullOrWhiteSpace(value)
            ? value.Trim().ToUpperInvariant()
            : null;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    }
}
