namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionAmbiguityGuardMatcher(
    ICompanionAmbiguityGuardCatalogueProvider catalogueProvider,
    IChatTelemetry telemetry) : ICompanionAmbiguityGuardMatcher
{
    public IReadOnlyList<CompanionAmbiguityGuardDefinition> Match(
        CompanionPlaceSearchStrategy strategy,
        CompanionSemanticIntent intent)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(intent);

        var strongTerms = BuildStrongTerms(strategy, intent);
        var weakTerms = BuildWeakTerms(strategy, intent);
        var matched = catalogueProvider.GetAll()
            .Select(guard => new
            {
                Guard = guard,
                Score = ScoreGuard(guard, strongTerms, weakTerms)
            })
            .Where(static item => item.Score >= 2)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Guard.GuardId, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Guard)
            .Take(12)
            .ToArray();

        _ = telemetry.TrackAsync(
            "places.guard_catalogue.matched",
            new Dictionary<string, object?>
            {
                ["guardCount"] = matched.Length,
                ["guardIds"] = matched.Select(static guard => guard.GuardId).ToArray(),
                ["matchedDomains"] = matched.Select(static guard => guard.Domain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            },
            CancellationToken.None);

        return matched;
    }

    private static IReadOnlyList<string> BuildStrongTerms(
        CompanionPlaceSearchStrategy strategy,
        CompanionSemanticIntent intent)
    {
        return NormalizeTerms(
            [
                strategy.Role.RequestedRole,
                .. strategy.Role.RequiredCoreRoles,
                .. strategy.Role.AcceptableSubRoles,
                .. strategy.Role.Modifiers,
                .. strategy.HardRequirements,
                .. strategy.SoftPreferences,
                .. intent.Role.RequiredCoreRoles,
                .. intent.Role.AcceptableSubRoles,
                .. intent.Role.Modifiers,
                .. intent.HardFilters,
                .. intent.SoftPreferences,
                .. intent.RequestedDetailFields
            ]);
    }

    private static IReadOnlyList<string> BuildWeakTerms(
        CompanionPlaceSearchStrategy strategy,
        CompanionSemanticIntent intent)
    {
        return NormalizeTerms(
            [
                strategy.CanonicalQuery,
                intent.PlaceQuery,
                .. strategy.SearchVariants.Select(static variant => variant.Query)
            ]);
    }

    private static IReadOnlyList<string> NormalizeTerms(IEnumerable<string?> values)
    {
        return values
            .Select(Normalize)
            .SelectMany(static value => new[] { value, Singularize(value) })
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ScoreGuard(
        CompanionAmbiguityGuardDefinition guard,
        IReadOnlyList<string> strongTerms,
        IReadOnlyList<string> weakTerms)
    {
        var requested = guard.RequestedConcepts.Select(Normalize).Where(static value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var score = 0;
        foreach (var concept in requested)
        {
            if (MatchesAny(strongTerms, concept))
            {
                score += 3;
            }
            else if (MatchesAny(weakTerms, concept))
            {
                score += 2;
            }
        }

        return score;
    }

    private static bool MatchesAny(IReadOnlyList<string> terms, string concept)
    {
        return terms.Any(term => TermMatchesConcept(term, concept));
    }

    private static bool TermMatchesConcept(string term, string concept)
    {
        if (term.Equals(concept, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsPhrase(term, concept) || ContainsPhrase(Singularize(term), Singularize(concept));
    }

    private static bool ContainsPhrase(string source, string phrase)
    {
        if (phrase.Length < 3)
        {
            return false;
        }

        var paddedSource = $" {source} ";
        var paddedPhrase = $" {phrase} ";
        return paddedSource.Contains(paddedPhrase, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return JsonCompanionAmbiguityGuardCatalogueProvider.NormalizeConcept(value);
    }

    private static string Singularize(string value)
    {
        if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && value.Length > 4)
        {
            return $"{value[..^3]}y";
        }

        if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !value.EndsWith("ss", StringComparison.OrdinalIgnoreCase) && value.Length > 3)
        {
            return value[..^1];
        }

        return value;
    }
}
