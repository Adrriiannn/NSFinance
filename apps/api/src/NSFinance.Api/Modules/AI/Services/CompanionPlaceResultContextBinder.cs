namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceResultContextBinder(IChatTelemetry telemetry) : ICompanionPlaceResultContextBinder
{
    public CompanionPlaceResultContextBinding Bind(
        UserChatRequest request,
        ResultContextReadResult readResult,
        ResultContextSnapshot? latestPlacesV2Context,
        CompanionSemanticIntent currentIntent)
    {
        var active = readResult.ActiveResultContext;
        var isFollowUp = currentIntent.ActionKind is "filter_previous_results" or "sort_previous_results";
        ResultContextSnapshot? chosen;
        string source;
        string reason;
        var clientWasStale = false;

        if (isFollowUp && latestPlacesV2Context is not null)
        {
            chosen = latestPlacesV2Context;
            source = "latest_v2";
            reason = "follow_up_prefers_latest_places_v2_context";
            clientWasStale = active is not null && active.ResultSetId != latestPlacesV2Context.ResultSetId;
        }
        else if (active is not null
                 && active.NormalizedConstraints.TryGetValue("pipeline", out var pipeline)
                 && string.Equals(pipeline, "places_intelligence_v2", StringComparison.OrdinalIgnoreCase))
        {
            chosen = active;
            source = readResult.UsedClientResultSetId ? "client_active" : "latest_v2";
            reason = "active_context_is_places_v2";
        }
        else if (active is not null)
        {
            chosen = active;
            source = "legacy_active";
            reason = "legacy_active_context_fallback";
        }
        else
        {
            chosen = null;
            source = "none";
            reason = "no_result_context_available";
        }

        _ = telemetry.TrackAsync(
            "places.result_context.binding_decision",
            new Dictionary<string, object?>
            {
                ["correlationId"] = request.CorrelationId,
                ["currentActionKind"] = currentIntent.ActionKind,
                ["activeResultSetId"] = active?.ResultSetId,
                ["latestPlacesV2ResultSetId"] = latestPlacesV2Context?.ResultSetId,
                ["chosenResultSetId"] = chosen?.ResultSetId,
                ["source"] = source,
                ["clientContextWasStale"] = clientWasStale,
                ["reason"] = reason
            },
            CancellationToken.None);

        return new CompanionPlaceResultContextBinding(chosen, source, reason, clientWasStale);
    }
}
