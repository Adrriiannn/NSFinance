namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceParkingEvidenceService(
    ICompanionPlaceDiscoveryService discoveryService,
    IChatTelemetry telemetry) : ICompanionPlaceParkingEvidenceService
{
    public async Task<CompanionParkingEvidenceResult> EvaluateAsync(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CancellationToken cancellationToken)
    {
        await telemetry.TrackAsync(
            "places.parking_evidence.started",
            new Dictionary<string, object?>
            {
                ["candidateCount"] = candidates.Count,
                ["evaluatedCandidateLimit"] = Math.Min(10, candidates.Count)
            },
            cancellationToken);

        var evidence = new Dictionary<string, CompanionParkingEvidence>(StringComparer.OrdinalIgnoreCase);
        var queryPasses = new List<string>();
        foreach (var candidate in candidates.Take(10))
        {
            var local = EvaluateLocal(candidate);
            if (local is not null)
            {
                evidence[candidate.PlaceId] = local;
                continue;
            }

            if (candidate.Latitude.HasValue && candidate.Longitude.HasValue)
            {
                var nearby = await FindNearbyParkingAsync(candidate, 300, cancellationToken);
                queryPasses.Add($"{candidate.PlaceId}:parking_300m");
                if (nearby is not null)
                {
                    evidence[candidate.PlaceId] = nearby;
                }
            }
        }

        await telemetry.TrackAsync(
            "places.parking_evidence.completed",
            new Dictionary<string, object?>
            {
                ["candidateCount"] = candidates.Count,
                ["evaluatedCandidateCount"] = Math.Min(10, candidates.Count),
                ["evidenceCount"] = evidence.Count,
                ["queryPasses"] = queryPasses.ToArray()
            },
            cancellationToken);

        return new CompanionParkingEvidenceResult(evidence, queryPasses, evidence.Count == 0 ? ["parking_evidence_none"] : []);
    }

    private static CompanionParkingEvidence? EvaluateLocal(CompanionPlacePoolCandidate candidate)
    {
        var haystack = Haystack(candidate);
        if (haystack.Contains("free parking", StringComparison.Ordinal)
            || haystack.Contains("paid parking", StringComparison.Ordinal)
            || haystack.Contains("parking lot", StringComparison.Ordinal)
            || haystack.Contains("parking garage", StringComparison.Ordinal)
            || haystack.Contains("wheelchair accessible parking", StringComparison.Ordinal)
            || haystack.Contains(" parking ", StringComparison.Ordinal))
        {
            return new CompanionParkingEvidence(candidate.PlaceId, "confirmed_on_site", 0.95d, null, null, ["explicit_parking_metadata"]);
        }

        if (haystack.Contains("shopping centre", StringComparison.Ordinal)
            || haystack.Contains("shopping center", StringComparison.Ordinal)
            || haystack.Contains("retail park", StringComparison.Ordinal)
            || haystack.Contains("airport", StringComparison.Ordinal)
            || haystack.Contains("hotel", StringComparison.Ordinal)
            || haystack.Contains("omni park", StringComparison.Ordinal))
        {
            return new CompanionParkingEvidence(candidate.PlaceId, "likely_on_site", 0.72d, null, null, ["venue_context_likely_parking"]);
        }

        return null;
    }

    private async Task<CompanionParkingEvidence?> FindNearbyParkingAsync(
        CompanionPlacePoolCandidate candidate,
        int radiusMeters,
        CancellationToken cancellationToken)
    {
        var result = await discoveryService.DiscoverNearbyAsync(
            new CompanionNearbyDiscoveryRequest(
                Latitude: candidate.Latitude!.Value,
                Longitude: candidate.Longitude!.Value,
                RadiusMeters: radiusMeters,
                IncludedTypes: ["parking"],
                MaxCandidates: 3),
            cancellationToken);
        var nearest = result.Candidates
            .Where(item => item.Location is not null)
            .Select(item => new
            {
                Candidate = item,
                Distance = DistanceMeters(candidate.Latitude.Value, candidate.Longitude.Value, item.Location!)
            })
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        return nearest is null
            ? null
            : new CompanionParkingEvidence(candidate.PlaceId, "nearby_parking", 0.68d, nearest.Candidate.PlaceId, nearest.Distance, ["nearby_parking_search"]);
    }

    private static string Haystack(CompanionPlacePoolCandidate candidate) => Normalize(string.Join(' ', candidate.DisplayName, candidate.PrimaryType, candidate.PrimaryTypeDisplayName, string.Join(' ', candidate.Types), candidate.ShortFormattedAddress, string.Join(' ', candidate.LightweightAttributes.Values)));

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');

    private static double DistanceMeters(double latitude, double longitude, PlaceLocationSummary target)
    {
        const double EarthRadiusMeters = 6_371_000d;
        var lat1 = Degrees(latitude);
        var lat2 = Degrees(target.Latitude);
        var dLat = Degrees(target.Latitude - latitude);
        var dLon = Degrees(target.Longitude - longitude);
        var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
        return EarthRadiusMeters * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
    }

    private static double Degrees(double value) => value * (Math.PI / 180d);
}
