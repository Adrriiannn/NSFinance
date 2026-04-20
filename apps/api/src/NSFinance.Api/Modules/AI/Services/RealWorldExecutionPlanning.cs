namespace NSFinance.Api.Modules.AI.Services;

public sealed class ExploratoryDomainSelectionPolicy : IExploratoryDomainSelectionPolicy
{
    private readonly IRealWorldDomainCapabilityCatalog domainCapabilityCatalog;

    public ExploratoryDomainSelectionPolicy(IRealWorldDomainCapabilityCatalog domainCapabilityCatalog)
    {
        this.domainCapabilityCatalog = domainCapabilityCatalog;
    }

    public ExploratoryDomainSelectionPolicy()
        : this(new RealWorldDomainCapabilityCatalog())
    {
    }

    public RealWorldDomainSelectionResult Select(
        RealWorldIntentInterpretation interpretation,
        string userQuery,
        int maxDomains)
    {
        var cap = Math.Clamp(maxDomains, 1, 4);
        var mode = interpretation.RecommendedExecutionMode;
        var normalizedQuery = userQuery?.Trim().ToLowerInvariant() ?? string.Empty;
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "real_world_catalog_selection_started",
            $"real_world_catalog_selection_mode:{mode.ToString().ToLowerInvariant()}"
        };
        var signals = QuerySignals.From(normalizedQuery);
        reasonCodes.Add(signals.HasAnySignal
            ? "real_world_planner_query_signal_used"
            : "real_world_planner_query_signal_missing");

        var candidateRanks = interpretation.CandidateDomains
            .Distinct()
            .Select((domain, index) => new { domain, index })
            .ToDictionary(static x => x.domain, static x => x.index);
        var candidateConcepts = interpretation.CandidateConcepts
            .Where(static concept => !string.IsNullOrWhiteSpace(concept))
            .Select(static concept => concept.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var scored = domainCapabilityCatalog.GetDomains()
            .Where(capability => IsEligibleForMode(capability, mode))
            .Select(capability =>
            {
                reasonCodes.Add($"real_world_catalog_domain_considered:{ToReasonToken(capability.Domain)}");
                var score = ScoreDomain(
                    interpretation,
                    capability,
                    signals,
                    candidateRanks,
                    candidateConcepts,
                    out var aiCandidateMatch,
                    out var querySignalMatch);
                return new ScoredDomain(
                    Capability: capability,
                    Score: score,
                    AiCandidateMatch: aiCandidateMatch,
                    QuerySignalMatch: querySignalMatch);
            })
            .OrderByDescending(static x => x.Score)
            .ThenBy(x => candidateRanks.TryGetValue(x.Capability.Domain, out var rank) ? rank : int.MaxValue)
            .ThenBy(static x => x.Capability.Domain)
            .ToArray();
        var selected = new List<RealWorldDiscoveryDomain>(cap);
        var selectedFamilies = new HashSet<RealWorldDomainFamily>();
        var deferredForDiversity = new List<ScoredDomain>(cap);

        void SelectDomain(ScoredDomain scoredDomain, string source)
        {
            selected.Add(scoredDomain.Capability.Domain);
            selectedFamilies.Add(scoredDomain.Capability.Family);
            reasonCodes.Add($"real_world_catalog_domain_selected:{ToReasonToken(scoredDomain.Capability.Domain)}");
            reasonCodes.Add(
                $"real_world_catalog_selection_reason:{ToReasonToken(scoredDomain.Capability.Domain)}:{source}");
            if (scoredDomain.AiCandidateMatch)
            {
                reasonCodes.Add(
                    $"real_world_catalog_selection_reason:{ToReasonToken(scoredDomain.Capability.Domain)}:ai_candidate");
            }

            if (scoredDomain.QuerySignalMatch)
            {
                reasonCodes.Add(
                    $"real_world_catalog_selection_reason:{ToReasonToken(scoredDomain.Capability.Domain)}:query_signal");
            }
        }

        foreach (var scoredDomain in scored)
        {
            if (selected.Count >= cap)
            {
                break;
            }

            if (selected.Contains(scoredDomain.Capability.Domain))
            {
                continue;
            }

            if (IsRedundantWithSelected(scoredDomain.Capability, selected))
            {
                reasonCodes.Add("real_world_catalog_candidate_family_conflict_resolved");
                continue;
            }

            if (mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch
                && selectedFamilies.Contains(scoredDomain.Capability.Family))
            {
                var allowFamilyPriority = signals.PrefersFamily
                                          && scoredDomain.Capability.SuitableFamily
                                          && !scoredDomain.Capability.SuitableNightlife;
                if (allowFamilyPriority)
                {
                    reasonCodes.Add("real_world_catalog_family_signal_priority_applied");
                    SelectDomain(scoredDomain, "catalog_family_priority");
                    continue;
                }

                deferredForDiversity.Add(scoredDomain);
                reasonCodes.Add("real_world_catalog_diversity_suppression_applied");
                continue;
            }

            SelectDomain(scoredDomain, "catalog_ranked");
        }

        if (mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch)
        {
            foreach (var scoredDomain in deferredForDiversity)
            {
                if (selected.Count >= cap)
                {
                    break;
                }

                if (selected.Contains(scoredDomain.Capability.Domain)
                    || IsRedundantWithSelected(scoredDomain.Capability, selected))
                {
                    continue;
                }

                SelectDomain(scoredDomain, "catalog_ranked_diversity_fill");
            }
        }

        if (selected.Count == 0)
        {
            reasonCodes.Add("real_world_catalog_default_backfill_used");
            foreach (var scoredDomain in scored.Where(static domain => domain.Capability.ExploratoryFallbackEligible))
            {
                if (selected.Count >= cap)
                {
                    break;
                }

                if (selected.Contains(scoredDomain.Capability.Domain)
                    || IsRedundantWithSelected(scoredDomain.Capability, selected))
                {
                    continue;
                }

                SelectDomain(scoredDomain, "catalog_fallback");
            }
        }

        return new RealWorldDomainSelectionResult(
            SelectedDomains: selected,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static bool IsEligibleForMode(
        RealWorldDomainCapability capability,
        RealWorldExecutionMode mode)
    {
        if (capability.Family == RealWorldDomainFamily.Meta)
        {
            return false;
        }

        return mode switch
        {
            RealWorldExecutionMode.ExploratoryMultiDomainSearch => capability.SupportsExploratorySearch,
            RealWorldExecutionMode.FocusedThemeSearch => capability.SupportsFocusedThemeSearch,
            _ => capability.SupportsFocusedPlaceSearch
        };
    }

    private static int ScoreDomain(
        RealWorldIntentInterpretation interpretation,
        RealWorldDomainCapability capability,
        QuerySignals signals,
        IReadOnlyDictionary<RealWorldDiscoveryDomain, int> candidateRanks,
        IReadOnlySet<string> candidateConcepts,
        out bool aiCandidateMatch,
        out bool querySignalMatch)
    {
        aiCandidateMatch = candidateRanks.TryGetValue(capability.Domain, out var candidateRank);
        querySignalMatch = false;

        var score = 0;
        if (aiCandidateMatch)
        {
            score += 42 - Math.Min(candidateRank, 8) * 2;
        }

        var conceptMatches = capability.CanonicalConcepts
            .Count(candidateConcepts.Contains);
        if (conceptMatches > 0)
        {
            score += 14 + ((conceptMatches - 1) * 4);
        }

        if (interpretation.IntentFamily == RealWorldIntentFamily.CommerceDiscovery)
        {
            score += capability.SupportsCommerceDiscovery ? 18 : -8;
        }

        if (interpretation.IntentFamily == RealWorldIntentFamily.ServiceDiscovery)
        {
            score += capability.SupportsServiceDiscovery || capability.SupportsEssentialService ? 18 : -8;
        }

        if (signals.PrefersFamily)
        {
            score += capability.SuitableFamily ? 14 : 0;
            score += capability.SuitableNightlife ? -8 : 0;
            querySignalMatch |= capability.SuitableFamily || capability.SuitableNightlife;
        }

        if (signals.PrefersEvening)
        {
            score += capability.SuitableEvening ? 9 : 0;
            querySignalMatch |= capability.SuitableEvening;
        }

        if (signals.PrefersChill)
        {
            score += capability.SuitableChill ? 11 : 0;
            score += capability.SuitableNightlife ? -4 : 0;
            querySignalMatch |= capability.SuitableChill || capability.SuitableNightlife;
        }

        if (signals.PrefersOutdoor)
        {
            score += capability.SuitableOutdoor ? 10 : 0;
            querySignalMatch |= capability.SuitableOutdoor;
        }

        if (signals.PrefersActive)
        {
            score += capability.SuitableActive ? 8 : 0;
            querySignalMatch |= capability.SuitableActive;
        }

        if (signals.PrefersFoodDrink)
        {
            score += capability.Family == RealWorldDomainFamily.FoodDrink ? 11 : 0;
            querySignalMatch |= capability.Family == RealWorldDomainFamily.FoodDrink;
        }

        if (signals.PrefersEntertainment)
        {
            var entertainmentMatch = capability.Family is RealWorldDomainFamily.Entertainment
                or RealWorldDomainFamily.Outdoor;
            score += entertainmentMatch ? 9 : 0;
            querySignalMatch |= entertainmentMatch;
        }

        if (signals.PrefersCommerce)
        {
            score += capability.SupportsCommerceDiscovery ? 12 : 0;
            querySignalMatch |= capability.SupportsCommerceDiscovery;
        }

        if (signals.PrefersService || signals.PrefersErrand)
        {
            var serviceMatch = capability.SupportsServiceDiscovery
                               || capability.SupportsEssentialService
                               || capability.SuitableQuickErrand;
            score += serviceMatch ? 11 : 0;
            querySignalMatch |= serviceMatch;
        }

        if (signals.PrefersNightlife)
        {
            score += capability.SuitableNightlife ? 12 : 0;
            querySignalMatch |= capability.SuitableNightlife;
        }

        if (signals.PrefersBudget)
        {
            score += capability.SuitableBudgetFriendly ? 7 : 0;
            querySignalMatch |= capability.SuitableBudgetFriendly;
        }

        if (signals.PrefersSolo)
        {
            score += capability.SuitableSolo ? 4 : 0;
            querySignalMatch |= capability.SuitableSolo;
        }

        if (signals.PrefersSocial)
        {
            score += capability.SuitableSocial ? 4 : 0;
            querySignalMatch |= capability.SuitableSocial;
        }

        if (interpretation.HasNearMeLanguage && !capability.NearMeAppropriate)
        {
            score -= 6;
        }

        if (interpretation.HasExplicitLocality && !capability.ExplicitLocalityAppropriate)
        {
            score -= 4;
        }

        if (capability.IsGeneric)
        {
            score -= 2;
        }

        if (capability.ExploratoryFallbackEligible)
        {
            score += 1;
        }

        return score;
    }

    private bool IsRedundantWithSelected(
        RealWorldDomainCapability candidate,
        IReadOnlyCollection<RealWorldDiscoveryDomain> selectedDomains)
    {
        if (!candidate.IsGeneric)
        {
            return false;
        }

        foreach (var selectedDomain in selectedDomains)
        {
            if (!domainCapabilityCatalog.TryGetDomain(selectedDomain, out var selectedCapability))
            {
                continue;
            }

            if (selectedCapability.Family == candidate.Family && !selectedCapability.IsGeneric)
            {
                return true;
            }
        }

        return false;
    }

    private static string ToReasonToken(RealWorldDiscoveryDomain domain)
    {
        return domain.ToString().ToLowerInvariant();
    }

    private sealed record ScoredDomain(
        RealWorldDomainCapability Capability,
        int Score,
        bool AiCandidateMatch,
        bool QuerySignalMatch);

    private sealed record QuerySignals(
        bool PrefersFamily,
        bool PrefersEvening,
        bool PrefersChill,
        bool PrefersOutdoor,
        bool PrefersActive,
        bool PrefersFoodDrink,
        bool PrefersEntertainment,
        bool PrefersCommerce,
        bool PrefersService,
        bool PrefersNightlife,
        bool PrefersBudget,
        bool PrefersSocial,
        bool PrefersSolo,
        bool PrefersErrand)
    {
        public bool HasAnySignal =>
            PrefersFamily
            || PrefersEvening
            || PrefersChill
            || PrefersOutdoor
            || PrefersActive
            || PrefersFoodDrink
            || PrefersEntertainment
            || PrefersCommerce
            || PrefersService
            || PrefersNightlife
            || PrefersBudget
            || PrefersSocial
            || PrefersSolo
            || PrefersErrand;

        public static QuerySignals From(string normalizedQuery)
        {
            return new QuerySignals(
                PrefersFamily: ContainsAny(normalizedQuery, "family", "kids", "children"),
                PrefersEvening: ContainsAny(normalizedQuery, "tonight", "evening", "night", "weekend"),
                PrefersChill: ContainsAny(normalizedQuery, "chill", "quiet", "calm", "relax"),
                PrefersOutdoor: ContainsAny(normalizedQuery, "walk", "park", "outdoor", "nature"),
                PrefersActive: ContainsAny(normalizedQuery, "active", "adventure", "fitness", "sport", "hike"),
                PrefersFoodDrink: ContainsAny(
                    normalizedQuery,
                    "eat",
                    "food",
                    "drink",
                    "coffee",
                    "cafe",
                    "restaurant",
                    "dine",
                    "brunch"),
                PrefersEntertainment: ContainsAny(
                    normalizedQuery,
                    "fun",
                    "entertainment",
                    "movie",
                    "cinema",
                    "theater",
                    "theatre",
                    "film"),
                PrefersCommerce: ContainsAny(normalizedQuery, "buy", "shop", "purchase", "sell"),
                PrefersService: ContainsAny(
                    normalizedQuery,
                    "service",
                    "pharmacy",
                    "chemist",
                    "petrol",
                    "fuel",
                    "gas station"),
                PrefersNightlife: ContainsAny(normalizedQuery, "nightlife", "pub", "bar", "drinks", "drinking"),
                PrefersBudget: ContainsAny(normalizedQuery, "budget", "cheap", "affordable", "value"),
                PrefersSocial: ContainsAny(normalizedQuery, "friends", "group", "together", "social"),
                PrefersSolo: ContainsAny(normalizedQuery, "alone", "solo", "myself"),
                PrefersErrand: ContainsAny(normalizedQuery, "quick", "errand", "pick up", "pick-up"));
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            return terms.Any(term => text.Contains(term, StringComparison.Ordinal));
        }
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

        var maxDomains = interpretation.RecommendedExecutionMode switch
        {
            RealWorldExecutionMode.ExploratoryMultiDomainSearch => 4,
            RealWorldExecutionMode.FocusedThemeSearch => 2,
            _ => 1
        };
        var domainSelection = exploratoryDomainSelectionPolicy.Select(
            interpretation,
            normalizedQuery,
            maxDomains);
        reasonCodes.UnionWith(domainSelection.ReasonCodes);
        var selectedDomains = domainSelection.SelectedDomains;

        if (selectedDomains.Count == 0)
        {
            selectedDomains = interpretation.CandidateDomains
                .Where(domain => domain is not RealWorldDiscoveryDomain.ExploratoryEveningActivity
                                  and not RealWorldDiscoveryDomain.ExploratoryFamilyActivity)
                .Distinct()
                .Take(Math.Max(1, maxDomains))
                .ToArray();
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

        var useDirectPlacesExecution = interpretation.PlacesApplicable
                                       && interpretation.RecommendedExecutionMode is
                                           RealWorldExecutionMode.FocusedPlaceSearch
                                           or RealWorldExecutionMode.FocusedThemeSearch
                                           or RealWorldExecutionMode.ExploratoryMultiDomainSearch;

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
            RealWorldFailureScenario.InternalRoutingConflict => new RealWorldFailureMessage(
                ReplyText:
                    "I understood this as a place search, but hit an internal routing issue. "
                    + "Please try again now.",
                Warnings: ["fallback_internal_routing_conflict"],
                FollowUpIntentHints: ["retry_local_search", "provide_typed_location"]),
            _ => new RealWorldFailureMessage(
                ReplyText: "I couldn’t complete that request right now.",
                Warnings: ["fallback_generic"],
                FollowUpIntentHints: ["retry_local_search"])
        };
    }
}

