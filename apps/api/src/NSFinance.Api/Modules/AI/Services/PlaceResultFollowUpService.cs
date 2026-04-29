namespace NSFinance.Api.Modules.AI.Services;

public interface IPlaceResultFollowUpService
{
    Task<PlaceFollowUpExecutionResult> ExecuteAsync(
        CompanionResolvedAction action,
        ResultContextSnapshot resultContext,
        CancellationToken cancellationToken);
}

public sealed class PlaceResultFollowUpService(
    IPlaceDetailsService placeDetailsService,
    ILogger<PlaceResultFollowUpService> logger) : IPlaceResultFollowUpService
{
    public async Task<PlaceFollowUpExecutionResult> ExecuteAsync(
        CompanionResolvedAction action,
        ResultContextSnapshot resultContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resultContext);
        cancellationToken.ThrowIfCancellationRequested();

        var entities = resultContext.SuggestedEntities
            .Where(static entity => !string.IsNullOrWhiteSpace(entity.EntityId))
            .OrderBy(static entity => entity.Rank <= 0 ? int.MaxValue : entity.Rank)
            .ToArray();
        if (entities.Length == 0)
        {
            return new PlaceFollowUpExecutionResult(
                Candidates: [],
                EvidenceQuality: "none",
                Uncertainties: ["The previous result set did not include place entities."],
                Warnings: ["place_follow_up_no_entities"]);
        }

        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uncertainty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<PlaceFollowUpCandidate>(entities.Length);
        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = await TryGetDetailsAsync(entity.EntityId, warnings, cancellationToken);
            var score = Score(entity, details, action, out var matched, out var missing);
            candidates.Add(new PlaceFollowUpCandidate(
                PlaceId: entity.EntityId,
                Name: ResolveName(entity, details),
                OriginalRank: entity.Rank,
                NewRank: entity.Rank,
                Score: score,
                MatchedReasons: matched,
                MissingEvidence: missing));
        }

        if (RequiresParking(action)
            && candidates.All(candidate => candidate.MissingEvidence.Count > 0))
        {
            uncertainty.Add("Google Places does not always expose confirmed on-site parking for every venue.");
            warnings.Add("place_follow_up_parking_evidence_weak");
        }

        var ordered = action.Kind == CompanionActionKind.SortPreviousResults
                      && string.Equals(action.SortGoal, "distance", StringComparison.OrdinalIgnoreCase)
            ? SortByDistanceAttribute(candidates, entities)
            : candidates
                .OrderByDescending(static candidate => candidate.Score ?? 0d)
                .ThenBy(static candidate => candidate.OriginalRank)
                .ToArray();

        var reranked = ordered
            .Select((candidate, index) => candidate with
            {
                NewRank = index + 1
            })
            .ToArray();

        return new PlaceFollowUpExecutionResult(
            Candidates: reranked,
            EvidenceQuality: ResolveEvidenceQuality(reranked, warnings),
            Uncertainties: uncertainty.ToArray(),
            Warnings: warnings.ToArray());
    }

    private async Task<PlaceDetailsResult?> TryGetDetailsAsync(
        string placeId,
        ISet<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var details = await placeDetailsService.GetDetailsAsync(placeId, cancellationToken);
            return string.IsNullOrWhiteSpace(details.PlaceId) ? null : details;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Place follow-up details lookup failed placeId={PlaceId}",
                placeId);
            warnings.Add("place_follow_up_details_lookup_failed");
            return null;
        }
    }

    private static double? Score(
        ResultContextEntity entity,
        PlaceDetailsResult? details,
        CompanionResolvedAction action,
        out IReadOnlyList<string> matchedReasons,
        out IReadOnlyList<string> missingEvidence)
    {
        var matched = new List<string>();
        var missing = new List<string>();
        var score = 0.50d;

        if (RequiresParking(action))
        {
            if (details?.AccessibilityOptions?.WheelchairAccessibleParking == true)
            {
                score += 0.45d;
                matched.Add("parking_signal:wheelchair_accessible_parking");
            }
            else if (HasParkingType(entity, details))
            {
                score += 0.35d;
                matched.Add("parking_signal:place_type");
            }
            else
            {
                score -= 0.15d;
                missing.Add("confirmed_parking");
            }
        }

        foreach (var include in action.IncludeConcepts)
        {
            if (MatchesConcept(entity, details, include))
            {
                score += 0.08d;
                matched.Add($"concept_match:{include}");
            }
        }

        foreach (var exclude in action.ExcludeConcepts)
        {
            if (!MatchesConcept(entity, details, exclude))
            {
                continue;
            }

            score -= 0.35d;
            missing.Add($"excluded_concept:{exclude}");
        }

        if (details?.Rating is >= 4.3d)
        {
            score += 0.06d;
            matched.Add("rating_signal");
        }

        matchedReasons = matched.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        missingEvidence = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return Math.Clamp(score, 0d, 1.25d);
    }

    private static bool RequiresParking(CompanionResolvedAction action)
    {
        return string.Equals(action.Requirement, "parking", StringComparison.OrdinalIgnoreCase)
               || action.Preferences.Any(static value => string.Equals(value, "parking", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasParkingType(ResultContextEntity entity, PlaceDetailsResult? details)
    {
        return ContainsToken(entity.Category, "parking")
               || (entity.Attributes?.Values.Any(static value => ContainsToken(value, "parking")) ?? false)
               || ContainsToken(details?.PrimaryType, "parking")
               || (details?.Types?.Any(static type => ContainsToken(type, "parking") || ContainsToken(type, "car_park")) ?? false);
    }

    private static bool MatchesConcept(ResultContextEntity entity, PlaceDetailsResult? details, string concept)
    {
        if (string.IsNullOrWhiteSpace(concept))
        {
            return false;
        }

        var normalizedConcept = Normalize(concept);
        return ContainsToken(entity.Label, normalizedConcept)
               || ContainsToken(entity.Category, normalizedConcept)
               || (entity.Attributes?.Values.Any(value => ContainsToken(value, normalizedConcept)) ?? false)
               || ContainsToken(details?.Name, normalizedConcept)
               || ContainsToken(details?.PrimaryType, normalizedConcept)
               || ContainsToken(details?.PrimaryTypeDisplayName, normalizedConcept)
               || (details?.Types?.Any(type => ContainsToken(type, normalizedConcept)) ?? false);
    }

    private static bool ContainsToken(string? source, string token)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var sourceNormalized = Normalize(source);
        return sourceNormalized.Contains(token, StringComparison.OrdinalIgnoreCase)
               || token.Contains(sourceNormalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    }

    private static IReadOnlyList<PlaceFollowUpCandidate> SortByDistanceAttribute(
        IReadOnlyList<PlaceFollowUpCandidate> candidates,
        IReadOnlyList<ResultContextEntity> entities)
    {
        var rankByPlaceId = entities.ToDictionary(
            static entity => entity.EntityId,
            static entity => TryReadDistance(entity.Attributes),
            StringComparer.OrdinalIgnoreCase);
        return candidates
            .OrderBy(candidate => rankByPlaceId.TryGetValue(candidate.PlaceId, out var distance) ? distance ?? double.MaxValue : double.MaxValue)
            .ThenBy(static candidate => candidate.OriginalRank)
            .ToArray();
    }

    private static double? TryReadDistance(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null)
        {
            return null;
        }

        return attributes.TryGetValue("distance_meters", out var raw)
               && double.TryParse(raw, out var parsed)
            ? parsed
            : null;
    }

    private static string ResolveName(ResultContextEntity entity, PlaceDetailsResult? details)
    {
        return !string.IsNullOrWhiteSpace(details?.Name)
            ? details.Name
            : entity.Label;
    }

    private static string ResolveEvidenceQuality(
        IReadOnlyList<PlaceFollowUpCandidate> candidates,
        ISet<string> warnings)
    {
        if (candidates.Count == 0)
        {
            return "none";
        }

        if (warnings.Contains("place_follow_up_parking_evidence_weak"))
        {
            return "weak";
        }

        return candidates.Any(candidate => candidate.MatchedReasons.Count > 0)
            ? "partial"
            : "weak";
    }
}
