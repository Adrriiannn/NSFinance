using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceCandidatePoolService(
    ICompanionPlaceDiscoveryService discoveryService,
    IPlaceRegistryService placeRegistryService,
    IOptions<GooglePlacesOptions> options,
    ICompanionPlaceLocationBoundaryService? locationBoundaryService,
    ICompanionPlaceRetrievalPlanner? retrievalPlanner,
    IChatTelemetry telemetry) : ICompanionPlaceCandidatePoolService
{
    private const int PoolTargetCount = 50;
    private const int ProviderPageSize = 20;
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<CompanionPlaceCandidatePoolResult> BuildPoolAsync(
        CompanionSemanticIntent intent,
        UserChatRequest request,
        CancellationToken cancellationToken)
    {
        return await BuildPoolAsync(intent, request, strategy: null, cancellationToken);
    }

    public async Task<CompanionPlaceCandidatePoolResult> BuildPoolAsync(
        CompanionSemanticIntent intent,
        UserChatRequest request,
        CompanionPlaceSearchStrategy? strategy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var diagnostics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var boundaryPlan = locationBoundaryService?.CreatePlan(request, intent, strategy);
        var retrievalPlan = retrievalPlanner?.Build(request, intent, strategy, boundaryPlan);
        var queryPasses = retrievalPlan?.Passes.Select(static pass => pass.PassId).ToArray() ?? BuildQueryPasses(intent, strategy);
        var queryPassesUsed = new List<string>();
        var candidatesById = new Dictionary<string, CompanionPlacePoolCandidate>(StringComparer.OrdinalIgnoreCase);
        var country = ResolveCountryCode(request);
        var radiusMeters = ResolveRadiusMeters(intent);
        var providerReturnedCount = 0;

        await telemetry.TrackAsync(
            "places.pool.build_started",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["queryPassCount"] = queryPasses.Count,
                ["radiusMeters"] = radiusMeters
            },
            cancellationToken);

        if (retrievalPlan is not null)
        {
            foreach (var pass in retrievalPlan.Passes)
            {
                if (candidatesById.Count >= PoolTargetCount)
                {
                    break;
                }

                queryPassesUsed.Add(pass.PassId);
                await telemetry.TrackAsync(
                    "places.retrieval_pass.started",
                    new Dictionary<string, object?>
                    {
                        ["correlationId"] = request.CorrelationId,
                        ["passId"] = pass.PassId,
                        ["mode"] = pass.Mode,
                        ["query"] = pass.Query,
                        ["includedTypes"] = pass.IncludedTypes.ToArray(),
                        ["purpose"] = pass.Purpose
                    },
                    cancellationToken);
                var result = pass.Mode == "nearby"
                    ? await discoveryService.DiscoverNearbyAsync(
                        new CompanionNearbyDiscoveryRequest(
                            Latitude: pass.Latitude ?? intent.Location.Latitude ?? 0d,
                            Longitude: pass.Longitude ?? intent.Location.Longitude ?? 0d,
                            RadiusMeters: (int)Math.Clamp(pass.RadiusMeters ?? ResolveRadiusMeters(intent), 1_000, 50_000),
                            IncludedTypes: pass.IncludedTypes,
                            CountryCode: pass.CountryCode,
                            MaxCandidates: retrievalPlan.ProviderPageSize),
                        cancellationToken)
                    : await discoveryService.DiscoverAsync(
                        new CompanionPlaceDiscoveryRequest(
                            Query: pass.Query!,
                            CountryCode: pass.CountryCode,
                            Latitude: pass.Latitude ?? intent.Location.Latitude,
                            Longitude: pass.Longitude ?? intent.Location.Longitude,
                            RadiusMeters: (pass.Latitude ?? intent.Location.Latitude).HasValue ? (int?)Math.Clamp(pass.RadiusMeters ?? ResolveRadiusMeters(intent), 1_000, 50_000) : null,
                            MaxCandidates: retrievalPlan.ProviderPageSize),
                        cancellationToken);

                diagnostics.UnionWith(result.Warnings);
                providerReturnedCount += result.Candidates.Count;
                foreach (var candidate in result.Candidates.Select(item => Map(item, intent)))
                {
                    candidatesById.TryAdd(candidate.PlaceId, candidate);
                    await placeRegistryService.RegisterSeenAsync("google_places", candidate.PlaceId, BuildRegistryTags(intent), cancellationToken);
                }

                await telemetry.TrackAsync(
                    "places.retrieval_pass.completed",
                    new Dictionary<string, object?>
                    {
                        ["correlationId"] = request.CorrelationId,
                        ["passId"] = pass.PassId,
                        ["providerReturnedCount"] = result.Candidates.Count,
                        ["dedupedCount"] = candidatesById.Count
                    },
                    cancellationToken);
            }
        }
        else
        {
        foreach (var pass in queryPasses)
        {
            if (candidatesById.Count >= PoolTargetCount)
            {
                break;
            }

            if (pass == "nearby:parking" && intent.Location.Latitude.HasValue && intent.Location.Longitude.HasValue)
            {
                queryPassesUsed.Add(pass);
                var nearby = await discoveryService.DiscoverNearbyAsync(
                    new CompanionNearbyDiscoveryRequest(
                        Latitude: intent.Location.Latitude.Value,
                        Longitude: intent.Location.Longitude.Value,
                        RadiusMeters: radiusMeters,
                        IncludedTypes: ["parking"],
                        CountryCode: country,
                        MaxCandidates: ProviderPageSize),
                    cancellationToken);
                diagnostics.UnionWith(nearby.Warnings);
                providerReturnedCount += nearby.Candidates.Count;
                foreach (var candidate in nearby.Candidates.Select(item => Map(item, intent)))
                {
                    candidatesById.TryAdd(candidate.PlaceId, candidate);
                    await placeRegistryService.RegisterSeenAsync("google_places", candidate.PlaceId, BuildRegistryTags(intent), cancellationToken);
                }

                continue;
            }

            queryPassesUsed.Add(pass);
            var text = await discoveryService.DiscoverAsync(
                new CompanionPlaceDiscoveryRequest(
                    Query: pass,
                    CountryCode: country,
                    Latitude: intent.Location.Latitude,
                    Longitude: intent.Location.Longitude,
                    RadiusMeters: intent.Location.Latitude.HasValue ? radiusMeters : null,
                    MaxCandidates: ProviderPageSize),
                cancellationToken);
            diagnostics.UnionWith(text.Warnings);
            providerReturnedCount += text.Candidates.Count;
            foreach (var candidate in text.Candidates.Select(item => Map(item, intent)))
            {
                candidatesById.TryAdd(candidate.PlaceId, candidate);
                await placeRegistryService.RegisterSeenAsync("google_places", candidate.PlaceId, BuildRegistryTags(intent), cancellationToken);
            }
        }
        }

        var candidates = candidatesById.Values.Take(PoolTargetCount).ToArray();
        await telemetry.TrackAsync(
            "places.pool.provider_count",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["candidatePoolCount"] = candidates.Length,
                ["targetCount"] = PoolTargetCount,
                ["providerReturnedCount"] = providerReturnedCount,
                ["targetReached"] = candidates.Length >= PoolTargetCount,
                ["queryPasses"] = queryPassesUsed.ToArray()
            },
            cancellationToken);
        await telemetry.TrackAsync(
            "places.pool.deduped_count",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["targetCount"] = PoolTargetCount,
                ["providerReturnedCount"] = providerReturnedCount,
                ["dedupedCount"] = candidates.Length,
                ["targetReached"] = candidates.Length >= PoolTargetCount,
                ["queryPasses"] = queryPassesUsed.ToArray()
            },
            cancellationToken);
        await telemetry.TrackAsync(
            "places.pool.target_reached",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["targetCount"] = PoolTargetCount,
                ["providerReturnedCount"] = providerReturnedCount,
                ["dedupedCount"] = candidates.Length,
                ["targetReached"] = candidates.Length >= PoolTargetCount,
                ["queryPassesUsed"] = queryPassesUsed.Count
            },
            cancellationToken);

        return new CompanionPlaceCandidatePoolResult(
            Candidates: candidates,
            QueryPasses: queryPassesUsed,
            Diagnostics: diagnostics.ToArray(),
            UsedCache: candidates.Any(static c => c.LightweightAttributes.TryGetValue("from_cache", out var value)
                                                  && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)),
            FailureReason: candidates.Length == 0 ? "places_pool_empty" : null);
    }

    private IReadOnlyList<string> BuildQueryPasses(CompanionSemanticIntent intent, CompanionPlaceSearchStrategy? strategy)
    {
        if (strategy is not null && strategy.SearchVariants.Count > 0)
        {
            var strategyPasses = new List<string>();
            if (strategy.Role.RequestedRole == "parking"
                && strategy.Location.Latitude.HasValue
                && strategy.Location.Longitude.HasValue)
            {
                strategyPasses.Add("nearby:parking");
            }

            strategyPasses.AddRange(strategy.SearchVariants.Select(static variant => variant.Query));
            return strategyPasses
                .Where(static pass => !string.IsNullOrWhiteSpace(pass))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();
        }

        var passes = new List<string>();
        if (intent.PlaceQuery is not null && IsParkingIntent(intent))
        {
            if (intent.Location.Latitude.HasValue && intent.Location.Longitude.HasValue)
            {
                passes.Add("nearby:parking");
            }

            passes.AddRange(["car parks", "parking", "parking garage"]);
        }
        else if (!string.IsNullOrWhiteSpace(intent.BrandOrEntity))
        {
            passes.Add(intent.BrandOrEntity!);
            passes.Add($"{intent.BrandOrEntity} coffee");
            passes.Add($"{intent.BrandOrEntity} cafe");
            if (!string.IsNullOrWhiteSpace(intent.PlaceQuery)
                && !string.Equals(intent.PlaceQuery, intent.BrandOrEntity, StringComparison.OrdinalIgnoreCase))
            {
                passes.Add(intent.PlaceQuery!);
            }
        }
        else if (!string.IsNullOrWhiteSpace(intent.PlaceQuery))
        {
            passes.Add(intent.PlaceQuery!);
            if (intent.PlaceQuery.Contains("fine dining", StringComparison.OrdinalIgnoreCase)
                || intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase))
            {
                passes.Add("upscale restaurants");
                passes.Add("michelin style restaurants");
            }
            else if (intent.Location.Mode == "near_me")
            {
                passes.Add($"{intent.PlaceQuery} nearby");
                passes.Add($"best {intent.PlaceQuery}");
            }
        }
        else
        {
            passes.Add("local places");
        }

        if (intent.SoftPreferences.Contains("upscale", StringComparer.OrdinalIgnoreCase)
            && !passes.Any(static pass => pass.Contains("fine dining", StringComparison.OrdinalIgnoreCase)))
        {
            passes.Insert(0, "fine dining restaurants");
        }

        return passes
            .Where(static pass => !string.IsNullOrWhiteSpace(pass))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    private int ResolveRadiusMeters(CompanionSemanticIntent intent)
    {
        if (intent.Location.Mode == "near_me")
        {
            return 15_000;
        }

        return Math.Clamp(placesOptions.DefaultSearchRadiusMeters <= 0 ? 15_000 : placesOptions.DefaultSearchRadiusMeters, 1_000, 25_000);
    }

    private static bool IsParkingIntent(CompanionSemanticIntent intent)
    {
        return Normalize(intent.PlaceQuery).Contains("car park", StringComparison.Ordinal)
               || Normalize(intent.PlaceQuery).Contains("parking", StringComparison.Ordinal)
               || intent.HardFilters.Contains("parking_available_or_nearby", StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveCountryCode(UserChatRequest request)
    {
        if (request.Metadata?.TryGetValue("country_code", out var value) == true
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().ToUpperInvariant();
        }

        return null;
    }

    private static CompanionPlacePoolCandidate Map(CompanionPlaceCandidate candidate, CompanionSemanticIntent intent)
    {
        var distance = TryComputeDistanceMeters(intent.Location.Latitude, intent.Location.Longitude, candidate.Location);
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add("business_status", candidate.BusinessStatus);
        Add("formatted_address", candidate.FormattedAddress);

        return new CompanionPlacePoolCandidate(
            PlaceId: candidate.PlaceId,
            DisplayName: candidate.DisplayName ?? candidate.PlaceId,
            PrimaryType: candidate.PrimaryType,
            PrimaryTypeDisplayName: candidate.PrimaryTypeDisplayName,
            Types: candidate.Types,
            Latitude: candidate.Location?.Latitude,
            Longitude: candidate.Location?.Longitude,
            DistanceMeters: distance,
            ShortFormattedAddress: candidate.ShortFormattedAddress,
            Rating: candidate.Rating,
            UserRatingCount: candidate.UserRatingCount,
            PriceLevel: candidate.PriceLevel,
            OpenNow: candidate.OpeningHours.OpenNow,
            LightweightAttributes: attributes);

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[key] = value.Trim();
            }
        }
    }

    private static IReadOnlyList<string> BuildRegistryTags(CompanionSemanticIntent intent)
    {
        return new[]
            {
                intent.BrandOrEntity is null ? null : "brand",
                intent.PlaceQuery,
                intent.RankingGoal
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static double? TryComputeDistanceMeters(double? sourceLatitude, double? sourceLongitude, PlaceLocationSummary? target)
    {
        if (!sourceLatitude.HasValue || !sourceLongitude.HasValue || target is null)
        {
            return null;
        }

        const double EarthRadiusMeters = 6_371_000d;
        var sourceLatRad = DegreesToRadians(sourceLatitude.Value);
        var targetLatRad = DegreesToRadians(target.Latitude);
        var deltaLat = DegreesToRadians(target.Latitude - sourceLatitude.Value);
        var deltaLon = DegreesToRadians(target.Longitude - sourceLongitude.Value);
        var a = Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d)
                + (Math.Cos(sourceLatRad) * Math.Cos(targetLatRad)
                   * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d));
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double value) => value * (Math.PI / 180d);

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
    }
}
