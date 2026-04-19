namespace NSFinance.Api.Modules.AI.Services;

public sealed class ExploratoryDomainSelectionPolicy : IExploratoryDomainSelectionPolicy
{
    private static readonly IReadOnlyList<RealWorldDiscoveryDomain> EveningDefaults =
    [
        RealWorldDiscoveryDomain.PubBar,
        RealWorldDiscoveryDomain.MovieTheater,
        RealWorldDiscoveryDomain.Restaurant,
        RealWorldDiscoveryDomain.ParkWalk
    ];

    private static readonly IReadOnlyList<RealWorldDiscoveryDomain> FamilyDefaults =
    [
        RealWorldDiscoveryDomain.Playground,
        RealWorldDiscoveryDomain.ParkWalk,
        RealWorldDiscoveryDomain.MovieTheater,
        RealWorldDiscoveryDomain.Restaurant
    ];

    private static readonly IReadOnlyList<RealWorldDiscoveryDomain> ChillDefaults =
    [
        RealWorldDiscoveryDomain.ParkWalk,
        RealWorldDiscoveryDomain.Cafe,
        RealWorldDiscoveryDomain.MovieTheater,
        RealWorldDiscoveryDomain.Restaurant
    ];

    public IReadOnlyList<RealWorldDiscoveryDomain> Select(
        RealWorldIntentInterpretation interpretation,
        string userQuery,
        int maxDomains)
    {
        var cap = Math.Clamp(maxDomains, 1, 4);
        var normalized = userQuery?.Trim().ToLowerInvariant() ?? string.Empty;
        var selected = new List<RealWorldDiscoveryDomain>(cap);
        var prefersFamily = normalized.Contains("family", StringComparison.Ordinal)
                            || normalized.Contains("kids", StringComparison.Ordinal)
                            || normalized.Contains("children", StringComparison.Ordinal);
        var prefersEvening = normalized.Contains("tonight", StringComparison.Ordinal)
                             || normalized.Contains("evening", StringComparison.Ordinal)
                             || normalized.Contains("night", StringComparison.Ordinal);
        var prefersChill = normalized.Contains("chill", StringComparison.Ordinal)
                           || normalized.Contains("quiet", StringComparison.Ordinal)
                           || normalized.Contains("calm", StringComparison.Ordinal)
                           || normalized.Contains("relax", StringComparison.Ordinal);
        var prefersOutdoor = normalized.Contains("walk", StringComparison.Ordinal)
                             || normalized.Contains("park", StringComparison.Ordinal)
                             || normalized.Contains("outdoor", StringComparison.Ordinal)
                             || normalized.Contains("nature", StringComparison.Ordinal);
        var candidateOrder = interpretation.CandidateDomains
            .Where(static domain => !IsMetaDomain(domain))
            .Distinct()
            .Select((domain, index) => new
            {
                Domain = domain,
                Index = index,
                Score = ScoreDomain(domain, prefersFamily, prefersEvening, prefersChill, prefersOutdoor)
            })
            .OrderByDescending(static x => x.Score)
            .ThenBy(static x => x.Index)
            .Select(static x => x.Domain)
            .ToArray();

        void AddCandidate(RealWorldDiscoveryDomain domain)
        {
            if (selected.Contains(domain))
            {
                return;
            }

            if (selected.Count >= cap)
            {
                return;
            }

            if (IsRedundantWithSelected(domain, selected))
            {
                return;
            }

            selected.Add(domain);
        }

        foreach (var domain in candidateOrder)
        {
            AddCandidate(domain);
        }

        if (selected.Count == 0)
        {
            var defaults = prefersFamily
                ? FamilyDefaults
                : prefersChill
                    ? ChillDefaults
                    : prefersEvening
                        ? EveningDefaults
                : EveningDefaults;
            foreach (var domain in defaults)
            {
                AddCandidate(domain);
            }
        }

        if (selected.Count == 0)
        {
            AddCandidate(RealWorldDiscoveryDomain.EntertainmentGeneral);
        }

        return selected;
    }

    private static bool IsMetaDomain(RealWorldDiscoveryDomain domain)
    {
        return domain is RealWorldDiscoveryDomain.ExploratoryEveningActivity
            or RealWorldDiscoveryDomain.ExploratoryFamilyActivity;
    }

    private static int ScoreDomain(
        RealWorldDiscoveryDomain domain,
        bool prefersFamily,
        bool prefersEvening,
        bool prefersChill,
        bool prefersOutdoor)
    {
        var score = 0;
        if (prefersFamily)
        {
            score += domain switch
            {
                RealWorldDiscoveryDomain.Playground => 6,
                RealWorldDiscoveryDomain.ParkWalk => 4,
                RealWorldDiscoveryDomain.MovieTheater => 3,
                RealWorldDiscoveryDomain.Restaurant => 2,
                RealWorldDiscoveryDomain.PubBar => -4,
                RealWorldDiscoveryDomain.NightlifeGeneral => -5,
                _ => 0
            };
        }

        if (prefersEvening)
        {
            score += domain switch
            {
                RealWorldDiscoveryDomain.PubBar => 3,
                RealWorldDiscoveryDomain.MovieTheater => 3,
                RealWorldDiscoveryDomain.Restaurant => 2,
                RealWorldDiscoveryDomain.NightlifeGeneral => 2,
                _ => 0
            };
        }

        if (prefersChill)
        {
            score += domain switch
            {
                RealWorldDiscoveryDomain.ParkWalk => 4,
                RealWorldDiscoveryDomain.Cafe => 3,
                RealWorldDiscoveryDomain.MovieTheater => 2,
                RealWorldDiscoveryDomain.PubBar => -2,
                RealWorldDiscoveryDomain.NightlifeGeneral => -3,
                _ => 0
            };
        }

        if (prefersOutdoor)
        {
            score += domain switch
            {
                RealWorldDiscoveryDomain.ParkWalk => 5,
                RealWorldDiscoveryDomain.OutdoorActivity => 4,
                _ => 0
            };
        }

        return score;
    }

    private static bool IsRedundantWithSelected(
        RealWorldDiscoveryDomain candidate,
        IReadOnlyCollection<RealWorldDiscoveryDomain> selected)
    {
        if (candidate == RealWorldDiscoveryDomain.NightlifeGeneral
            && selected.Contains(RealWorldDiscoveryDomain.PubBar))
        {
            return true;
        }

        return candidate == RealWorldDiscoveryDomain.FoodDrinkGeneral
               && (selected.Contains(RealWorldDiscoveryDomain.Restaurant)
                   || selected.Contains(RealWorldDiscoveryDomain.Cafe)
                   || selected.Contains(RealWorldDiscoveryDomain.Takeaway));
    }
}

public sealed class RealWorldExecutionModePlanner(
    IExploratoryDomainSelectionPolicy exploratoryDomainSelectionPolicy) : IRealWorldExecutionModePlanner
{
    public RealWorldExecutionPlan Plan(
        string userQuery,
        RealWorldIntentInterpretation interpretation,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        var normalizedQuery = userQuery?.Trim() ?? string.Empty;

        var reasonCodes = new HashSet<string>(interpretation.ReasonCodes, StringComparer.Ordinal)
        {
            $"execution_mode:{interpretation.RecommendedExecutionMode.ToString().ToLowerInvariant()}"
        };
        reasonCodes.Add(
            string.IsNullOrWhiteSpace(normalizedQuery)
                ? "real_world_planner_query_signal_missing"
                : "real_world_planner_query_signal_used");

        if (interpretation.IntentFamily == RealWorldIntentFamily.FinancialGuidance
            || interpretation.RecommendedExecutionMode == RealWorldExecutionMode.FinancialGuidanceOnly)
        {
            return new RealWorldExecutionPlan(
                Mode: RealWorldExecutionMode.FinancialGuidanceOnly,
                IntentFamily: RealWorldIntentFamily.FinancialGuidance,
                ShouldHandoffToCompanion: false,
                ShouldUsePlaces: false,
                UseDirectPlacesExecution: false,
                RequiresLocationGrounding: false,
                SelectedDomains: [],
                ClarificationPrompt: null,
                ReasonCodes: reasonCodes.ToArray());
        }

        if (interpretation.InterpretationSource == RealWorldInterpretationSource.AiPrimary
            && interpretation.Confidence < 0.45d)
        {
            reasonCodes.Add("execution_mode:low_confidence_clarify");
            reasonCodes.Add(RealWorldInterpreterFallbackReasonCodes.PlannerDowngrade);
            return new RealWorldExecutionPlan(
                Mode: RealWorldExecutionMode.ClarifyLight,
                IntentFamily: interpretation.IntentFamily,
                ShouldHandoffToCompanion: true,
                ShouldUsePlaces: false,
                UseDirectPlacesExecution: false,
                RequiresLocationGrounding: false,
                SelectedDomains: [],
                ClarificationPrompt: interpretation.ClarificationPrompt
                                     ?? "I can help with nearby places or financial guidance. Tell me which one you want.",
                ReasonCodes: reasonCodes.ToArray());
        }

        if (interpretation.ClarificationNeeded
            || interpretation.RecommendedExecutionMode == RealWorldExecutionMode.ClarifyLight)
        {
            return new RealWorldExecutionPlan(
                Mode: RealWorldExecutionMode.ClarifyLight,
                IntentFamily: interpretation.IntentFamily,
                ShouldHandoffToCompanion: true,
                ShouldUsePlaces: false,
                UseDirectPlacesExecution: false,
                RequiresLocationGrounding: false,
                SelectedDomains: [],
                ClarificationPrompt: interpretation.ClarificationPrompt,
                ReasonCodes: reasonCodes.ToArray());
        }

        var requiresGrounding = interpretation.RequiresLocation
                                && (interpretation.HasNearMeLanguage
                                    || !interpretation.HasExplicitLocality);
        var hasAnyGrounding = grounding.HasCoordinates
                              || grounding.HasTypedArea
                              || localDiscovery.HasExplicitLocality
                              || interpretation.HasExplicitLocality;
        if (requiresGrounding && !hasAnyGrounding)
        {
            reasonCodes.Add("execution_mode:missing_location_guard");
            return new RealWorldExecutionPlan(
                Mode: RealWorldExecutionMode.MissingLocationGuard,
                IntentFamily: interpretation.IntentFamily,
                ShouldHandoffToCompanion: true,
                ShouldUsePlaces: false,
                UseDirectPlacesExecution: false,
                RequiresLocationGrounding: true,
                SelectedDomains: [],
                ClarificationPrompt: null,
                ReasonCodes: reasonCodes.ToArray());
        }

        var selectedDomains = interpretation.RecommendedExecutionMode switch
        {
            RealWorldExecutionMode.ExploratoryMultiDomainSearch => exploratoryDomainSelectionPolicy
                .Select(interpretation, normalizedQuery, 4),
            RealWorldExecutionMode.FocusedThemeSearch => exploratoryDomainSelectionPolicy
                .Select(interpretation, normalizedQuery, 2),
            _ => interpretation.CandidateDomains
                .Where(domain => domain is not RealWorldDiscoveryDomain.ExploratoryEveningActivity
                                  and not RealWorldDiscoveryDomain.ExploratoryFamilyActivity)
                .Distinct()
                .Take(1)
                .ToArray()
        };

        if (selectedDomains.Count == 0)
        {
            selectedDomains = interpretation.RecommendedExecutionMode == RealWorldExecutionMode.FocusedThemeSearch
                ? [RealWorldDiscoveryDomain.FoodDrinkGeneral, RealWorldDiscoveryDomain.EntertainmentGeneral]
                : [RealWorldDiscoveryDomain.EntertainmentGeneral];
            reasonCodes.Add("execution_mode:default_domain_selection_applied");
            reasonCodes.Add("real_world_planner_default_domain_selection_used");
        }
        else if (selectedDomains.Any(interpretation.CandidateDomains.Contains))
        {
            reasonCodes.Add("real_world_planner_ai_domains_preserved");
        }
        else
        {
            reasonCodes.Add("real_world_planner_domains_backfilled");
        }

        if (interpretation.ReasonCodes.Any(code =>
                code.Contains("domains_backfilled_from_fallback", StringComparison.OrdinalIgnoreCase)))
        {
            reasonCodes.Add("real_world_planner_domains_backfilled");
        }

        var useDirectPlacesExecution = interpretation.IntentFamily is RealWorldIntentFamily.CommerceDiscovery
            or RealWorldIntentFamily.ServiceDiscovery
            || interpretation.RecommendedExecutionMode is RealWorldExecutionMode.ExploratoryMultiDomainSearch
                or RealWorldExecutionMode.FocusedThemeSearch;

        return new RealWorldExecutionPlan(
            Mode: interpretation.RecommendedExecutionMode,
            IntentFamily: interpretation.IntentFamily,
            ShouldHandoffToCompanion: true,
            ShouldUsePlaces: true,
            UseDirectPlacesExecution: useDirectPlacesExecution,
            RequiresLocationGrounding: requiresGrounding,
            SelectedDomains: selectedDomains,
            ClarificationPrompt: interpretation.ClarificationPrompt,
            ReasonCodes: reasonCodes.ToArray());
    }
}

public sealed class RealWorldPlacesExecutionService(
    IPlacesSearchService placesSearchService,
    ILogger<RealWorldPlacesExecutionService> logger) : IRealWorldPlacesExecutionService
{
    public async Task<RealWorldPlacesExecutionResult> ExecuteAsync(
        RealWorldPlacesExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var maxDomains = Math.Clamp(request.MaxDomains, 1, 4);
        var maxPerDomain = Math.Clamp(request.MaxItemsPerDomain, 1, 3);
        var maxTotal = Math.Clamp(request.MaxTotalItems, 1, 8);
        var selectedDomains = request.Domains
            .Distinct()
            .Take(maxDomains)
            .ToArray();
        var groups = new List<RealWorldDomainPlacesGroup>(selectedDomains.Length);
        var warnings = new HashSet<string>(StringComparer.Ordinal)
        {
            "real_world_places_execution_started"
        };
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            $"real_world_places_mode:{request.Mode.ToString().ToLowerInvariant()}"
        };

        var totalItems = 0;
        var providerFailureCount = 0;
        var requestFailureCount = 0;

        foreach (var domain in selectedDomains)
        {
            if (totalItems >= maxTotal)
            {
                break;
            }

            var query = domain.ToQueryPhrase();
            var result = await placesSearchService.SearchAsync(
                query,
                request.CountryCode,
                request.LocationContext,
                cancellationToken);
            warnings.UnionWith(result.Warnings ?? []);

            if (!result.Items.Any())
            {
                if (!string.IsNullOrWhiteSpace(result.Metadata?.ProviderErrorCode))
                {
                    var code = result.Metadata!.ProviderErrorCode!;
                    if (code.Equals("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase)
                        || code.Contains("request", StringComparison.OrdinalIgnoreCase))
                    {
                        requestFailureCount += 1;
                    }
                    else
                    {
                        providerFailureCount += 1;
                    }
                }

                reasonCodes.Add($"real_world_places_domain_no_results:{domain}");
                continue;
            }

            var availableSlots = Math.Max(0, maxTotal - totalItems);
            if (availableSlots == 0)
            {
                break;
            }

            var items = result.Items
                .Take(Math.Min(maxPerDomain, availableSlots))
                .ToArray();
            totalItems += items.Length;
            groups.Add(new RealWorldDomainPlacesGroup(
                Domain: domain,
                Label: domain.ToLabel(),
                Items: items,
                Warnings: result.Warnings ?? []));
            reasonCodes.Add($"real_world_places_domain_results:{domain}:{items.Length}");
        }

        if (groups.Count == 0)
        {
            var scenario = requestFailureCount > 0
                ? RealWorldFailureScenario.ProviderRequestFailure
                : providerFailureCount > 0
                    ? RealWorldFailureScenario.ProviderUnavailable
                    : RealWorldFailureScenario.NoMatchesFound;
            reasonCodes.Add($"real_world_places_failure:{scenario}");
            return new RealWorldPlacesExecutionResult(
                Succeeded: false,
                HasAnyResults: false,
                IsPartial: false,
                Groups: [],
                FailureScenario: scenario,
                ReasonCodes: reasonCodes.ToArray(),
                Warnings: warnings.ToArray());
        }

        var expectedGroups = Math.Min(selectedDomains.Length, maxDomains);
        var isPartial = groups.Count < expectedGroups;
        if (isPartial)
        {
            reasonCodes.Add("real_world_places_partial_results");
        }

        logger.LogInformation(
            "Real-world places execution complete mode={Mode} groups={GroupCount} selectedDomains={SelectedDomainCount} totalItems={TotalItems} partial={Partial}",
            request.Mode,
            groups.Count,
            selectedDomains.Length,
            totalItems,
            isPartial);

        return new RealWorldPlacesExecutionResult(
            Succeeded: true,
            HasAnyResults: true,
            IsPartial: isPartial,
            Groups: groups,
            FailureScenario: isPartial ? RealWorldFailureScenario.ExploratoryPartialResults : null,
            ReasonCodes: reasonCodes.ToArray(),
            Warnings: warnings.ToArray());
    }
}

public sealed class RealWorldFailureMessageBuilder : IRealWorldFailureMessageBuilder
{
    public RealWorldFailureMessage Build(
        RealWorldFailureScenario scenario,
        bool exploratory,
        string? clarificationPrompt = null)
    {
        return scenario switch
        {
            RealWorldFailureScenario.MissingLocation => new RealWorldFailureMessage(
                ReplyText:
                    "I can help with nearby options, but I need either location permission "
                    + "or a typed area like a suburb, city centre, postcode, or landmark.",
                Warnings: ["fallback_missing_location", "nearby_location_missing"],
                FollowUpIntentHints: ["allow_location_permission", "provide_typed_location"]),
            RealWorldFailureScenario.LocationDeniedOpenSettings => new RealWorldFailureMessage(
                ReplyText:
                    "Location access is off at OS level. You can enable it in Settings, "
                    + "or type an area and I can search there instead.",
                Warnings:
                [
                    "fallback_location_denied_open_settings",
                    "nearby_location_open_settings_required"
                ],
                FollowUpIntentHints: ["open_location_settings", "provide_typed_location"]),
            RealWorldFailureScenario.ProviderRequestFailure => new RealWorldFailureMessage(
                ReplyText:
                    "I couldn’t run that place search safely just now. Please try again, "
                    + "or give me a simpler place type like cafes, pubs, or parks.",
                Warnings: ["fallback_provider_request_failure"],
                FollowUpIntentHints: ["retry_local_search", "refine_place_type"]),
            RealWorldFailureScenario.ProviderUnavailable => new RealWorldFailureMessage(
                ReplyText:
                    "I can’t reach live place data right now. Please try again shortly.",
                Warnings: ["fallback_provider_unavailable"],
                FollowUpIntentHints: ["retry_local_search", "provide_typed_location"]),
            RealWorldFailureScenario.NoMatchesFound => new RealWorldFailureMessage(
                ReplyText:
                    exploratory
                        ? "I couldn’t find enough strong options for those categories. Try a nearby area or a more specific preference."
                        : "I couldn’t find matching places for that request. Try a nearby area or a more specific place type.",
                Warnings: ["fallback_no_matches"],
                FollowUpIntentHints: ["provide_typed_location", "refine_place_preferences"]),
            RealWorldFailureScenario.ClarificationNeeded => new RealWorldFailureMessage(
                ReplyText: string.IsNullOrWhiteSpace(clarificationPrompt)
                    ? "I can help with nearby places or financial guidance. Tell me which one you want."
                    : clarificationPrompt,
                Warnings: ["fallback_clarify_light"],
                FollowUpIntentHints: ["clarify_intent", "provide_typed_location"]),
            RealWorldFailureScenario.DomainNotActionable => new RealWorldFailureMessage(
                ReplyText:
                    "I understand what you’re aiming for, but I need one more detail to search reliably "
                    + "(for example, place type, area, or budget style).",
                Warnings: ["fallback_domain_not_actionable"],
                FollowUpIntentHints: ["clarify_intent", "refine_place_preferences"]),
            RealWorldFailureScenario.ExploratoryPartialResults => new RealWorldFailureMessage(
                ReplyText:
                    "I found some good options, but a few categories didn’t return strong matches yet.",
                Warnings: ["fallback_exploratory_partial"],
                FollowUpIntentHints: ["refine_place_preferences", "retry_local_search"]),
            _ => new RealWorldFailureMessage(
                ReplyText: "I couldn’t complete that request right now.",
                Warnings: ["fallback_generic"],
                FollowUpIntentHints: ["retry_local_search"])
        };
    }
}

