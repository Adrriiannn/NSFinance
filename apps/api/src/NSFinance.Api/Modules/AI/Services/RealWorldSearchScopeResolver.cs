namespace NSFinance.Api.Modules.AI.Services;

public enum RealWorldSearchScopeKind
{
    None = 0,
    DeviceLocation = 1,
    ExplicitArea = 2
}

public sealed record RealWorldSearchScopeResolution(
    CompanionLocationGrounding EffectiveGrounding,
    CompanionLocationGrounding SecondaryDeviceGrounding,
    LocalDiscoveryConstraintExtractionResult EffectiveLocalDiscovery,
    RealWorldSearchScopeKind SearchScope,
    string? ExplicitArea,
    bool HasUsableScope,
    bool ExplicitAreaOverrodeDeviceLocation,
    IReadOnlyList<string> ReasonCodes);

public interface IRealWorldSearchScopeResolver
{
    RealWorldSearchScopeResolution Resolve(
        string userQuery,
        CompanionLocationGrounding requestGrounding,
        LocalDiscoveryConstraintExtractionResult requestLocalDiscovery,
        RealWorldConversationSearchContextReadResult contextReadResult);
}

public sealed class RealWorldSearchScopeResolver : IRealWorldSearchScopeResolver
{
    public RealWorldSearchScopeResolution Resolve(
        string userQuery,
        CompanionLocationGrounding requestGrounding,
        LocalDiscoveryConstraintExtractionResult requestLocalDiscovery,
        RealWorldConversationSearchContextReadResult contextReadResult)
    {
        var normalizedQuery = Normalize(userQuery) ?? string.Empty;
        var reasonCodes = new HashSet<string>(contextReadResult.ReasonCodes, StringComparer.Ordinal);
        var context = contextReadResult.Context;

        var queryExplicitArea = NormalizeAreaValue(requestLocalDiscovery.LocalityHint);
        var groundingExplicitArea = NormalizeAreaValue(requestGrounding.TypedArea);
        var requestExplicitArea = queryExplicitArea ?? groundingExplicitArea;
        var contextExplicitArea = NormalizeAreaValue(context?.ExplicitArea);
        var explicitArea = requestExplicitArea ?? contextExplicitArea;

        var secondaryDeviceGrounding = ResolveSecondaryDeviceGrounding(requestGrounding, context);
        if (!requestGrounding.HasCoordinates && secondaryDeviceGrounding.HasCoordinates)
        {
            reasonCodes.Add("real_world_search_context_device_location_reused");
        }

        if (requestExplicitArea is null && !string.IsNullOrWhiteSpace(explicitArea))
        {
            reasonCodes.Add("real_world_search_context_explicit_area_reused");
        }

        var explicitAreaOverrodeDevice = false;
        RealWorldSearchScopeKind scope;
        CompanionLocationGrounding effectiveGrounding;
        if (!string.IsNullOrWhiteSpace(explicitArea))
        {
            scope = RealWorldSearchScopeKind.ExplicitArea;
            reasonCodes.Add("real_world_search_scope:explicit_area");
            explicitAreaOverrodeDevice = secondaryDeviceGrounding.HasCoordinates;
            if (explicitAreaOverrodeDevice)
            {
                reasonCodes.Add("real_world_search_scope:explicit_area_overrode_device_location");
            }

            effectiveGrounding = new CompanionLocationGrounding(
                Source: requestExplicitArea is null
                    ? "conversation_explicit_area"
                    : queryExplicitArea is not null
                        ? "query_locality"
                        : ResolveExplicitAreaSource(requestGrounding),
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                TypedArea: explicitArea,
                LocalityLabel: explicitArea,
                AccuracyBucket: null,
                CapturedAtUtc: null);
        }
        else if (secondaryDeviceGrounding.HasCoordinates)
        {
            scope = RealWorldSearchScopeKind.DeviceLocation;
            reasonCodes.Add("real_world_search_scope:device_location");
            effectiveGrounding = secondaryDeviceGrounding with
            {
                TypedArea = null
            };
        }
        else
        {
            scope = RealWorldSearchScopeKind.None;
            reasonCodes.Add("real_world_search_scope:missing");
            effectiveGrounding = requestGrounding;
        }

        var effectiveLocalDiscovery = BuildEffectiveLocalDiscovery(
            normalizedQuery,
            requestLocalDiscovery,
            explicitArea,
            context,
            reasonCodes);
        var hasUsableScope = scope is RealWorldSearchScopeKind.DeviceLocation or RealWorldSearchScopeKind.ExplicitArea;

        return new RealWorldSearchScopeResolution(
            EffectiveGrounding: effectiveGrounding,
            SecondaryDeviceGrounding: secondaryDeviceGrounding,
            EffectiveLocalDiscovery: effectiveLocalDiscovery,
            SearchScope: scope,
            ExplicitArea: explicitArea,
            HasUsableScope: hasUsableScope,
            ExplicitAreaOverrodeDeviceLocation: explicitAreaOverrodeDevice,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static LocalDiscoveryConstraintExtractionResult BuildEffectiveLocalDiscovery(
        string normalizedQuery,
        LocalDiscoveryConstraintExtractionResult requestLocalDiscovery,
        string? explicitArea,
        RealWorldConversationSearchContext? context,
        ISet<string> reasonCodes)
    {
        var audienceHints = MergeHints(requestLocalDiscovery.AudienceHints, context?.AudienceHints);
        var timeHints = MergeHints(requestLocalDiscovery.TimeHints, context?.TimeHints);
        var preferenceHints = MergeHints(requestLocalDiscovery.PreferenceHints, context?.PreferenceHints);
        var mergedHintsApplied = !SequenceEqualOrdinal(requestLocalDiscovery.AudienceHints, audienceHints)
                                 || !SequenceEqualOrdinal(requestLocalDiscovery.TimeHints, timeHints)
                                 || !SequenceEqualOrdinal(requestLocalDiscovery.PreferenceHints, preferenceHints);
        if (mergedHintsApplied)
        {
            reasonCodes.Add("real_world_search_context_hints_reused");
        }

        var hasExplicitLocality = requestLocalDiscovery.HasExplicitLocality || !string.IsNullOrWhiteSpace(explicitArea);
        var localityHint = hasExplicitLocality
            ? explicitArea ?? requestLocalDiscovery.LocalityHint
            : requestLocalDiscovery.LocalityHint;
        if (!requestLocalDiscovery.HasExplicitLocality && !string.IsNullOrWhiteSpace(explicitArea))
        {
            reasonCodes.Add("real_world_search_scope_explicit_area_applied_to_constraints");
        }

        var contextNearbyOutingActive = context?.NearbyOutingActive == true;
        var contextualFollowUp = contextNearbyOutingActive
                                 && IsContextualFollowUpRefinement(
                                     normalizedQuery,
                                     requestLocalDiscovery);
        if (contextualFollowUp)
        {
            reasonCodes.Add("real_world_search_context_refinement_applied");
        }

        var shouldPromoteCandidate = contextualFollowUp
                                     || (contextNearbyOutingActive
                                         && !ContainsFinancialSignal(normalizedQuery)
                                         && (requestLocalDiscovery.HasNearMeLanguage
                                             || hasExplicitLocality
                                             || requestLocalDiscovery.AudienceHints.Count > 0
                                             || requestLocalDiscovery.PreferenceHints.Count > 0
                                             || requestLocalDiscovery.TimeHints.Count > 0));
        var isLocalDiscoveryCandidate = requestLocalDiscovery.IsLocalDiscoveryCandidate || shouldPromoteCandidate;
        var confidence = requestLocalDiscovery.Confidence;
        if (contextualFollowUp)
        {
            confidence = Math.Max(confidence, 0.66d);
        }
        else if (shouldPromoteCandidate)
        {
            confidence = Math.Max(confidence, 0.58d);
        }

        var localReasonCodes = new HashSet<string>(requestLocalDiscovery.ReasonCodes, StringComparer.Ordinal);
        if (mergedHintsApplied)
        {
            localReasonCodes.Add("real_world_search_context_hints_reused");
        }

        if (contextualFollowUp)
        {
            localReasonCodes.Add("real_world_search_context_refinement_applied");
        }

        if (!requestLocalDiscovery.HasExplicitLocality && !string.IsNullOrWhiteSpace(explicitArea))
        {
            localReasonCodes.Add("real_world_search_scope_explicit_area_applied_to_constraints");
        }

        return requestLocalDiscovery with
        {
            IsLocalDiscoveryCandidate = isLocalDiscoveryCandidate,
            Confidence = Math.Round(Math.Clamp(confidence, 0d, 0.98d), 4, MidpointRounding.AwayFromZero),
            HasExplicitLocality = hasExplicitLocality,
            LocalityHint = localityHint,
            AudienceHints = audienceHints,
            TimeHints = timeHints,
            PreferenceHints = preferenceHints,
            ReasonCodes = localReasonCodes.ToArray()
        };
    }

    private static CompanionLocationGrounding ResolveSecondaryDeviceGrounding(
        CompanionLocationGrounding requestGrounding,
        RealWorldConversationSearchContext? context)
    {
        if (requestGrounding.HasCoordinates)
        {
            return requestGrounding;
        }

        if (context?.HasDeviceCoordinates == true)
        {
            return new CompanionLocationGrounding(
                Source: context.DeviceSource ?? "conversation_gps_context",
                Latitude: context.DeviceLatitude,
                Longitude: context.DeviceLongitude,
                RadiusMeters: context.DeviceRadiusMeters,
                TypedArea: null,
                LocalityLabel: context.DeviceLocalityLabel,
                AccuracyBucket: context.DeviceAccuracyBucket,
                CapturedAtUtc: context.DeviceCapturedAtUtc);
        }

        return requestGrounding with
        {
            Latitude = null,
            Longitude = null
        };
    }

    private static string ResolveExplicitAreaSource(CompanionLocationGrounding requestGrounding)
    {
        return requestGrounding.Source?.Trim().ToLowerInvariant() switch
        {
            "query_locality" => "query_locality",
            "typed_area" => "typed_area",
            _ => "typed_area"
        };
    }

    private static string? NormalizeAreaValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return CompanionLocationGroundingParser.IsValidAreaHint(trimmed)
            ? trimmed
            : null;
    }

    private static string[] MergeHints(
        IReadOnlyList<string> current,
        IReadOnlyList<string>? fromContext)
    {
        return current
            .Concat(fromContext ?? [])
            .Select(Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
    }

    private static bool SequenceEqualOrdinal(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index += 1)
        {
            if (!string.Equals(
                    Normalize(first[index]),
                    Normalize(second[index]),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsContextualFollowUpRefinement(
        string normalizedQuery,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return false;
        }

        if (ContainsFinancialSignal(normalizedQuery))
        {
            return false;
        }

        if (localDiscovery.HasNearMeLanguage || localDiscovery.HasExplicitLocality)
        {
            return true;
        }

        if (localDiscovery.AudienceHints.Count > 0
            || localDiscovery.PreferenceHints.Count > 0
            || localDiscovery.TimeHints.Count > 0)
        {
            return true;
        }

        var tokenCount = normalizedQuery.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        if (tokenCount > 8)
        {
            return false;
        }

        return normalizedQuery.Contains("what about", StringComparison.Ordinal)
               || normalizedQuery.Contains("something fun", StringComparison.Ordinal)
               || normalizedQuery.Contains("something quieter", StringComparison.Ordinal)
               || normalizedQuery.Contains("somewhere fun", StringComparison.Ordinal)
               || normalizedQuery.Contains("somewhere quiet", StringComparison.Ordinal)
               || normalizedQuery.Contains("with kids", StringComparison.Ordinal)
               || normalizedQuery.Contains("with family", StringComparison.Ordinal)
               || normalizedQuery.Contains("near me please", StringComparison.Ordinal)
               || normalizedQuery.Contains("instead", StringComparison.Ordinal);
    }

    private static bool ContainsFinancialSignal(string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return false;
        }

        return normalizedQuery.Contains("save", StringComparison.Ordinal)
               || normalizedQuery.Contains("saving", StringComparison.Ordinal)
               || normalizedQuery.Contains("afford", StringComparison.Ordinal)
               || normalizedQuery.Contains("budget", StringComparison.Ordinal)
               || normalizedQuery.Contains("income", StringComparison.Ordinal)
               || normalizedQuery.Contains("salary", StringComparison.Ordinal)
               || normalizedQuery.Contains("payday", StringComparison.Ordinal)
               || normalizedQuery.Contains("debt", StringComparison.Ordinal)
               || normalizedQuery.Contains("mortgage", StringComparison.Ordinal)
               || normalizedQuery.Contains("loan", StringComparison.Ordinal)
               || normalizedQuery.Contains("expense", StringComparison.Ordinal)
               || normalizedQuery.Contains("spend", StringComparison.Ordinal)
               || normalizedQuery.Contains("spending", StringComparison.Ordinal);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
    }
}
