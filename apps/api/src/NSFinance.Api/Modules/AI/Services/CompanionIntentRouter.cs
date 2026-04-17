namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionIntentRouter
{
    CompanionIntentRoutingResult Route(string? userQuery);
}

public sealed class CompanionIntentRouter : ICompanionIntentRouter
{
    private readonly ILogger<CompanionIntentRouter> _logger;
    private readonly ICompanionIntentNormalizer _normalizer;
    private readonly ICompanionIntentSignalExtractor _signalExtractor;
    private readonly ICompanionIntentScorer _scorer;
    private readonly ICompanionIntentResolutionPolicy _resolutionPolicy;

    public CompanionIntentRouter(ILogger<CompanionIntentRouter> logger)
        : this(
            logger,
            new CompanionIntentNormalizer(),
            new CompanionIntentSignalExtractor(),
            new CompanionIntentScorer(),
            new CompanionIntentResolutionPolicy())
    {
    }

    public CompanionIntentRouter(
        ILogger<CompanionIntentRouter> logger,
        ICompanionIntentNormalizer normalizer,
        ICompanionIntentSignalExtractor signalExtractor,
        ICompanionIntentScorer scorer,
        ICompanionIntentResolutionPolicy resolutionPolicy)
    {
        _logger = logger;
        _normalizer = normalizer;
        _signalExtractor = signalExtractor;
        _scorer = scorer;
        _resolutionPolicy = resolutionPolicy;
    }

    public CompanionIntentRoutingResult Route(string? userQuery)
    {
        var normalized = _normalizer.Normalize(userQuery);
        var extraction = _signalExtractor.Extract(normalized);
        var scoring = _scorer.Score(extraction);
        var resolution = _resolutionPolicy.Resolve(extraction, scoring);

        LogRoutingDecision(resolution);
        return resolution.Routing;
    }

    private void LogRoutingDecision(CompanionIntentResolutionResult resolution)
    {
        var secondaryIntents = resolution.Routing.SecondaryIntents.Count == 0
            ? "none"
            : string.Join(",", resolution.Routing.SecondaryIntents);
        var reasonCodes = resolution.Routing.ReasonCodes.Count == 0
            ? "none"
            : string.Join(",", resolution.Routing.ReasonCodes);
        var signalGroups = resolution.SignalGroups.Count == 0
            ? "none"
            : string.Join(",", resolution.SignalGroups);
        var scoreSummary = string.IsNullOrWhiteSpace(resolution.ScoreSummary)
            ? "none"
            : resolution.ScoreSummary;
        var fallbackReason = string.IsNullOrWhiteSpace(resolution.FallbackReason)
            ? "none"
            : resolution.FallbackReason;

        _logger.LogInformation(
            "[AI_COMPANION_ROUTING] promptSummary={PromptSummary} normalizedLength={NormalizedLength} truncated={Truncated} noisy={Noisy} signalGroups={SignalGroups} scoreSummary={ScoreSummary} resolutionPath={ResolutionPath} fallbackReason={FallbackReason} intentFamily={IntentFamily} primaryIntent={PrimaryIntent} secondaryIntents={SecondaryIntents} confidence={Confidence} reasonCodes={ReasonCodes} ambiguous={Ambiguous} unsupported={Unsupported}",
            resolution.Normalized.PromptSummary,
            resolution.Normalized.NormalizedLength,
            resolution.Normalized.WasTruncated,
            resolution.Normalized.IsLikelyNoisy,
            signalGroups,
            scoreSummary,
            resolution.ResolutionPath,
            fallbackReason,
            resolution.Routing.IntentFamily,
            resolution.Routing.PrimaryIntent,
            secondaryIntents,
            resolution.Routing.Confidence,
            reasonCodes,
            resolution.Routing.IsAmbiguous,
            resolution.Routing.IsUnsupported);
    }
}
