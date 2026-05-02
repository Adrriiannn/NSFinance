namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceDuplicateClusterService(IChatTelemetry telemetry) : ICompanionPlaceDuplicateClusterService
{
    public IReadOnlyList<CompanionPlacePoolCandidate> Cluster(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var parking = intent.Role.RequestedRole == "parking"
                      || Normalize(intent.PlaceQuery).Contains("parking", StringComparison.Ordinal)
                      || Normalize(intent.PlaceQuery).Contains("car park", StringComparison.Ordinal);
        var threshold = parking ? 80d : 25d;
        var clusters = new List<List<CompanionPlacePoolCandidate>>();
        foreach (var candidate in candidates)
        {
            var cluster = clusters.FirstOrDefault(existing => IsDuplicate(intent, parking, threshold, existing[0], candidate));
            if (cluster is null)
            {
                clusters.Add([candidate]);
            }
            else
            {
                cluster.Add(candidate);
            }
        }

        var clustered = clusters.Select(ChooseRepresentative).ToArray();
        _ = telemetry.TrackAsync(
            "places.duplicates.clustered",
            new Dictionary<string, object?>
            {
                ["beforeCount"] = candidates.Count,
                ["afterCount"] = clustered.Length,
                ["clusterCount"] = clusters.Count(item => item.Count > 1),
                ["largestClusterSize"] = clusters.Max(item => item.Count),
                ["searchKind"] = parking ? "parking" : intent.Role.RequestedRole ?? "generic"
            },
            CancellationToken.None);

        return clustered;
    }

    private static bool IsDuplicate(
        CompanionSemanticIntent intent,
        bool parking,
        double thresholdMeters,
        CompanionPlacePoolCandidate left,
        CompanionPlacePoolCandidate right)
    {
        var distance = DistanceMeters(left, right);
        if (!distance.HasValue || distance > thresholdMeters)
        {
            return false;
        }

        if (parking)
        {
            return IsParking(left) && IsParking(right) && (NamesOverlap(left, right) || AddressOverlap(left, right));
        }

        return string.Equals(NormalizeName(left.DisplayName), NormalizeName(right.DisplayName), StringComparison.Ordinal)
               && TypeFamily(left) == TypeFamily(right);
    }

    private static CompanionPlacePoolCandidate ChooseRepresentative(IReadOnlyList<CompanionPlacePoolCandidate> cluster)
    {
        return cluster
            .OrderByDescending(item => item.UserRatingCount ?? 0)
            .ThenByDescending(item => item.Rating ?? 0)
            .ThenBy(item => item.DistanceMeters ?? double.MaxValue)
            .First();
    }

    private static bool IsParking(CompanionPlacePoolCandidate candidate) => Haystack(candidate).Contains("parking", StringComparison.Ordinal) || Haystack(candidate).Contains("car park", StringComparison.Ordinal);

    private static bool NamesOverlap(CompanionPlacePoolCandidate left, CompanionPlacePoolCandidate right)
    {
        var leftName = NormalizeName(left.DisplayName);
        var rightName = NormalizeName(right.DisplayName);
        return leftName.Contains(rightName, StringComparison.Ordinal) || rightName.Contains(leftName, StringComparison.Ordinal);
    }

    private static bool AddressOverlap(CompanionPlacePoolCandidate left, CompanionPlacePoolCandidate right)
    {
        var leftTokens = Tokens(left.ShortFormattedAddress);
        var rightTokens = Tokens(right.ShortFormattedAddress);
        return leftTokens.Count > 0 && rightTokens.Count > 0 && leftTokens.Intersect(rightTokens).Count() >= 2;
    }

    private static HashSet<string> Tokens(string? value) => Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(token => token.Length > 2).ToHashSet(StringComparer.Ordinal);

    private static string TypeFamily(CompanionPlacePoolCandidate candidate) => Normalize(candidate.PrimaryType ?? candidate.Types.FirstOrDefault() ?? string.Empty);

    private static string Haystack(CompanionPlacePoolCandidate candidate) => Normalize(string.Join(' ', candidate.DisplayName, candidate.PrimaryType, candidate.PrimaryTypeDisplayName, string.Join(' ', candidate.Types), candidate.ShortFormattedAddress));

    private static string NormalizeName(string value) => Normalize(value).Replace(" ltd", string.Empty).Replace(" limited", string.Empty);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');

    private static double? DistanceMeters(CompanionPlacePoolCandidate left, CompanionPlacePoolCandidate right)
    {
        if (!left.Latitude.HasValue || !left.Longitude.HasValue || !right.Latitude.HasValue || !right.Longitude.HasValue)
        {
            return null;
        }

        const double EarthRadiusMeters = 6_371_000d;
        var lat1 = Degrees(left.Latitude.Value);
        var lat2 = Degrees(right.Latitude.Value);
        var dLat = Degrees(right.Latitude.Value - left.Latitude.Value);
        var dLon = Degrees(right.Longitude.Value - left.Longitude.Value);
        var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
        return EarthRadiusMeters * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
    }

    private static double Degrees(double value) => value * (Math.PI / 180d);
}
