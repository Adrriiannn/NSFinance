namespace NSFinance.Api.Modules.AI.Services;

public sealed class DeterministicCompanionPlaceSearchStrategyFallback(
    ICompanionPlacePhrasePreservingFallbackStrategyBuilder phraseFallbackBuilder,
    ICompanionPlaceAmbiguitySafetyClassifier ambiguitySafetyClassifier,
    IChatTelemetry telemetry) : IDeterministicCompanionPlaceSearchStrategyFallback
{
    public CompanionPlaceSearchStrategy Plan(UserChatRequest request, CompanionSemanticIntent intent, string fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(intent);

        var baseStrategy = phraseFallbackBuilder.Build(request, intent, fallbackReason);
        var guarded = ambiguitySafetyClassifier.Apply(request, intent, baseStrategy);
        var strategy = guarded.Strategy;

        _ = telemetry.TrackAsync(
            "places.search_strategy.phrase_fallback_used",
            new Dictionary<string, object?>
            {
                ["originalMessage"] = request.UserMessage,
                ["canonicalQuery"] = strategy.CanonicalQuery,
                ["entity"] = strategy.Entity?.CanonicalName,
                ["variantCount"] = strategy.SearchVariants.Count,
                ["ambiguityGuardApplied"] = guarded.Applied,
                ["warnings"] = strategy.Warnings.ToArray()
            },
            CancellationToken.None);

        return strategy with
        {
            Warnings = strategy.Warnings
                .Concat(guarded.ReasonCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
