using System.Collections.Concurrent;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record RealWorldConversationSearchContext(
    DateTimeOffset UpdatedAtUtc,
    double? DeviceLatitude,
    double? DeviceLongitude,
    int? DeviceRadiusMeters,
    string? DeviceAccuracyBucket,
    DateTimeOffset? DeviceCapturedAtUtc,
    string? DeviceLocalityLabel,
    string? DeviceSource,
    string? ExplicitArea,
    RealWorldExecutionMode? LastExecutionMode,
    RealWorldIntentFamily? LastIntentFamily,
    IReadOnlyList<RealWorldDiscoveryDomain> LastDomains,
    IReadOnlyList<string> AudienceHints,
    IReadOnlyList<string> TimeHints,
    IReadOnlyList<string> PreferenceHints,
    bool NearbyOutingActive)
{
    public bool HasDeviceCoordinates => DeviceLatitude.HasValue && DeviceLongitude.HasValue;
}

public sealed record RealWorldConversationSearchContextReadResult(
    RealWorldConversationSearchContext? Context,
    bool ContextReused,
    bool ContextExpired,
    bool DeviceLocationExpired,
    IReadOnlyList<string> ReasonCodes);

public sealed record RealWorldConversationSearchContextWriteInput(
    CompanionLocationGrounding Grounding,
    LocalDiscoveryConstraintExtractionResult LocalDiscovery,
    RealWorldIntentInterpretation Interpretation,
    RealWorldExecutionPlan Plan);

public interface IRealWorldConversationSearchContextService
{
    RealWorldConversationSearchContextReadResult Read(
        string? sessionKey,
        DateTimeOffset? nowUtc = null);

    void Write(
        string? sessionKey,
        RealWorldConversationSearchContextWriteInput input,
        DateTimeOffset? nowUtc = null);
}

public sealed class RealWorldConversationSearchContextService : IRealWorldConversationSearchContextService
{
    private static readonly TimeSpan ContextFreshnessWindow = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan DeviceLocationFreshnessWindow = TimeSpan.FromMinutes(12);
    private readonly ConcurrentDictionary<string, RealWorldConversationSearchContext> store =
        new(StringComparer.Ordinal);

    public RealWorldConversationSearchContextReadResult Read(
        string? sessionKey,
        DateTimeOffset? nowUtc = null)
    {
        var normalizedKey = NormalizeSessionKey(sessionKey);
        if (normalizedKey is null)
        {
            return new RealWorldConversationSearchContextReadResult(
                Context: null,
                ContextReused: false,
                ContextExpired: false,
                DeviceLocationExpired: false,
                ReasonCodes: []);
        }

        if (!store.TryGetValue(normalizedKey, out var context))
        {
            return new RealWorldConversationSearchContextReadResult(
                Context: null,
                ContextReused: false,
                ContextExpired: false,
                DeviceLocationExpired: false,
                ReasonCodes: []);
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        if (now - context.UpdatedAtUtc > ContextFreshnessWindow)
        {
            store.TryRemove(normalizedKey, out _);
            return new RealWorldConversationSearchContextReadResult(
                Context: null,
                ContextReused: false,
                ContextExpired: true,
                DeviceLocationExpired: false,
                ReasonCodes: ["real_world_search_context_expired"]);
        }

        var reasonCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "real_world_search_context_reused"
        };
        var effectiveContext = context;
        var deviceExpired = false;
        if (context.HasDeviceCoordinates
            && context.DeviceCapturedAtUtc.HasValue
            && now - context.DeviceCapturedAtUtc.Value > DeviceLocationFreshnessWindow)
        {
            deviceExpired = true;
            reasonCodes.Add("real_world_search_context_location_expired");
            effectiveContext = context with
            {
                DeviceLatitude = null,
                DeviceLongitude = null,
                DeviceRadiusMeters = null,
                DeviceAccuracyBucket = null,
                DeviceCapturedAtUtc = null,
                DeviceSource = null
            };
            store[normalizedKey] = effectiveContext with
            {
                UpdatedAtUtc = context.UpdatedAtUtc
            };
        }

        return new RealWorldConversationSearchContextReadResult(
            Context: effectiveContext,
            ContextReused: true,
            ContextExpired: false,
            DeviceLocationExpired: deviceExpired,
            ReasonCodes: reasonCodes.ToArray());
    }

    public void Write(
        string? sessionKey,
        RealWorldConversationSearchContextWriteInput input,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var normalizedKey = NormalizeSessionKey(sessionKey);
        if (normalizedKey is null)
        {
            return;
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        store.AddOrUpdate(
            normalizedKey,
            _ => BuildContext(
                input,
                existing: null,
                nowUtc: now),
            (_, existing) => BuildContext(
                input,
                existing,
                now));
    }

    private static RealWorldConversationSearchContext BuildContext(
        RealWorldConversationSearchContextWriteInput input,
        RealWorldConversationSearchContext? existing,
        DateTimeOffset nowUtc)
    {
        var explicitArea = ResolveExplicitArea(input.Grounding, input.LocalDiscovery)
                           ?? existing?.ExplicitArea;

        var hasCurrentDeviceCoordinates = input.Grounding.HasCoordinates;
        var mergedAudienceHints = MergeHints(existing?.AudienceHints, input.LocalDiscovery.AudienceHints);
        var mergedTimeHints = MergeHints(existing?.TimeHints, input.LocalDiscovery.TimeHints);
        var mergedPreferenceHints = MergeHints(existing?.PreferenceHints, input.LocalDiscovery.PreferenceHints);
        var domains = ResolveDomains(input, existing);
        var nearbyOutingActive = input.Plan.ShouldUsePlaces
                                || input.Interpretation.Exploratory
                                || input.LocalDiscovery.IsLocalDiscoveryCandidate
                                || existing?.NearbyOutingActive == true;

        return new RealWorldConversationSearchContext(
            UpdatedAtUtc: nowUtc,
            DeviceLatitude: hasCurrentDeviceCoordinates
                ? input.Grounding.Latitude
                : existing?.DeviceLatitude,
            DeviceLongitude: hasCurrentDeviceCoordinates
                ? input.Grounding.Longitude
                : existing?.DeviceLongitude,
            DeviceRadiusMeters: hasCurrentDeviceCoordinates
                ? input.Grounding.RadiusMeters
                : existing?.DeviceRadiusMeters,
            DeviceAccuracyBucket: hasCurrentDeviceCoordinates
                ? input.Grounding.AccuracyBucket
                : existing?.DeviceAccuracyBucket,
            DeviceCapturedAtUtc: hasCurrentDeviceCoordinates
                ? input.Grounding.CapturedAtUtc ?? nowUtc
                : existing?.DeviceCapturedAtUtc,
            DeviceLocalityLabel: NormalizeOptional(input.Grounding.LocalityLabel)
                                 ?? existing?.DeviceLocalityLabel,
            DeviceSource: hasCurrentDeviceCoordinates
                ? NormalizeOptional(input.Grounding.Source) ?? "gps"
                : existing?.DeviceSource,
            ExplicitArea: explicitArea,
            LastExecutionMode: input.Plan.Mode,
            LastIntentFamily: input.Interpretation.IntentFamily,
            LastDomains: domains,
            AudienceHints: mergedAudienceHints,
            TimeHints: mergedTimeHints,
            PreferenceHints: mergedPreferenceHints,
            NearbyOutingActive: nearbyOutingActive);
    }

    private static IReadOnlyList<RealWorldDiscoveryDomain> ResolveDomains(
        RealWorldConversationSearchContextWriteInput input,
        RealWorldConversationSearchContext? existing)
    {
        var selected = input.Plan.SelectedDomains
            .Distinct()
            .Take(4)
            .ToArray();
        if (selected.Length > 0)
        {
            return selected;
        }

        var interpreted = input.Interpretation.CandidateDomains
            .Distinct()
            .Take(4)
            .ToArray();
        if (interpreted.Length > 0)
        {
            return interpreted;
        }

        return existing?.LastDomains ?? [];
    }

    private static IReadOnlyList<string> MergeHints(
        IReadOnlyList<string>? existing,
        IReadOnlyList<string> current)
    {
        return (existing ?? [])
            .Concat(current)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
    }

    private static string? ResolveExplicitArea(
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var fromQuery = NormalizeArea(localDiscovery.LocalityHint);
        if (!string.IsNullOrWhiteSpace(fromQuery))
        {
            return fromQuery;
        }

        return NormalizeArea(grounding.TypedArea);
    }

    private static string? NormalizeArea(string? value)
    {
        var normalized = NormalizeOptional(value);
        return CompanionLocationGroundingParser.IsValidAreaHint(normalized)
            ? normalized
            : null;
    }

    private static string? NormalizeSessionKey(string? sessionKey)
    {
        var normalized = NormalizeOptional(sessionKey);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= 120
            ? normalized
            : normalized[..120];
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
