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

            if (mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch
                && ShouldSuppressForExploratoryFit(scoredDomain, selected.Count, signals))
            {
                reasonCodes.Add("real_world_exploratory_domain_fit_gate_applied");
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

                if (ShouldSuppressForExploratoryFit(scoredDomain, selected.Count, signals))
                {
                    reasonCodes.Add("real_world_exploratory_domain_fit_gate_applied");
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

    private static bool ShouldSuppressForExploratoryFit(
        ScoredDomain scoredDomain,
        int selectedCount,
        QuerySignals signals)
    {
        if (selectedCount == 0)
        {
            return false;
        }

        var minimumScore = signals.PrefersNightlife || signals.PrefersFamily ? 18 : 16;
        if (selectedCount >= 2)
        {
            minimumScore += 4;
        }

        if (signals.PrefersNightlife
            && !scoredDomain.Capability.SuitableNightlife
            && scoredDomain.Capability.Domain is not RealWorldDiscoveryDomain.PubBar
            && scoredDomain.Capability.Domain is not RealWorldDiscoveryDomain.NightlifeGeneral
            && scoredDomain.Capability.Family != RealWorldDomainFamily.FoodDrink)
        {
            return true;
        }

        return scoredDomain.Score < minimumScore;
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
    IExploratoryDomainSelectionPolicy exploratoryDomainSelectionPolicy,
    IRealWorldProductDomainEligibilityPolicy? productDomainEligibilityPolicy = null) : IRealWorldExecutionModePlanner
{
    private readonly IRealWorldProductDomainEligibilityPolicy commerceDomainEligibilityPolicy =
        productDomainEligibilityPolicy ?? new RealWorldProductDomainEligibilityPolicy();

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

        var effectiveInterpretation = interpretation;
        var hasAnyGrounding = grounding.HasCoordinates
                              || grounding.HasTypedArea
                              || localDiscovery.HasExplicitLocality
                              || interpretation.HasExplicitLocality;
        var shouldEnableExploratoryExecutionByContext = ShouldEnableExploratoryExecutionByContext(
            normalizedQuery,
            interpretation,
            localDiscovery,
            hasAnyGrounding);
        if (shouldEnableExploratoryExecutionByContext)
        {
            effectiveInterpretation = PromoteToExploratoryExecution(interpretation, localDiscovery);
            reasonCodes.Add("real_world_exploratory_execution_enabled_by_context");
            reasonCodes.Add("execution_mode:exploratory_context_override");
        }

        if (effectiveInterpretation.InterpretationSource == RealWorldInterpretationSource.AiPrimary
            && effectiveInterpretation.Confidence < 0.45d)
        {
            if (!hasAnyGrounding)
            {
                reasonCodes.Add("real_world_clarify_preserved_due_to_missing_scope");
            }

            reasonCodes.Add("execution_mode:low_confidence_clarify");
            reasonCodes.Add(RealWorldInterpreterFallbackReasonCodes.PlannerDowngrade);
            return new RealWorldExecutionPlan(
                Mode: RealWorldExecutionMode.ClarifyLight,
                IntentFamily: effectiveInterpretation.IntentFamily,
                ShouldHandoffToCompanion: true,
                ShouldUsePlaces: false,
                UseDirectPlacesExecution: false,
                RequiresLocationGrounding: false,
                SelectedDomains: [],
                ClarificationPrompt: effectiveInterpretation.ClarificationPrompt
                                     ?? "I can help with nearby places or financial guidance. Tell me which one you want.",
                ReasonCodes: reasonCodes.ToArray());
        }

        if (effectiveInterpretation.ClarificationNeeded
            || effectiveInterpretation.RecommendedExecutionMode == RealWorldExecutionMode.ClarifyLight)
        {
            if (!hasAnyGrounding)
            {
                reasonCodes.Add("real_world_clarify_preserved_due_to_missing_scope");
            }

            return new RealWorldExecutionPlan(
                Mode: RealWorldExecutionMode.ClarifyLight,
                IntentFamily: effectiveInterpretation.IntentFamily,
                ShouldHandoffToCompanion: true,
                ShouldUsePlaces: false,
                UseDirectPlacesExecution: false,
                RequiresLocationGrounding: false,
                SelectedDomains: [],
                ClarificationPrompt: effectiveInterpretation.ClarificationPrompt,
                ReasonCodes: reasonCodes.ToArray());
        }

        var requiresGrounding = effectiveInterpretation.RequiresLocation
                                && (effectiveInterpretation.HasNearMeLanguage
                                    || !effectiveInterpretation.HasExplicitLocality);
        if (requiresGrounding && !hasAnyGrounding)
        {
            reasonCodes.Add("execution_mode:missing_location_guard");
            reasonCodes.Add("real_world_clarify_preserved_due_to_missing_scope");
            return new RealWorldExecutionPlan(
                Mode: RealWorldExecutionMode.MissingLocationGuard,
                IntentFamily: effectiveInterpretation.IntentFamily,
                ShouldHandoffToCompanion: true,
                ShouldUsePlaces: false,
                UseDirectPlacesExecution: false,
                RequiresLocationGrounding: true,
                SelectedDomains: [],
                ClarificationPrompt: null,
                ReasonCodes: reasonCodes.ToArray());
        }

        var maxDomains = effectiveInterpretation.RecommendedExecutionMode switch
        {
            RealWorldExecutionMode.ExploratoryMultiDomainSearch => 4,
            RealWorldExecutionMode.FocusedThemeSearch => 2,
            _ => 1
        };
        var domainSelection = exploratoryDomainSelectionPolicy.Select(
            effectiveInterpretation,
            normalizedQuery,
            maxDomains);
        reasonCodes.UnionWith(domainSelection.ReasonCodes);
        var selectedDomains = domainSelection.SelectedDomains;

        if (selectedDomains.Count == 0)
        {
            selectedDomains = effectiveInterpretation.CandidateDomains
                .Where(domain => domain is not RealWorldDiscoveryDomain.ExploratoryEveningActivity
                                  and not RealWorldDiscoveryDomain.ExploratoryFamilyActivity)
                .Distinct()
                .Take(Math.Max(1, maxDomains))
                .ToArray();
            reasonCodes.Add("execution_mode:default_domain_selection_applied");
            reasonCodes.Add("real_world_planner_default_domain_selection_used");
        }
        else if (selectedDomains.Any(effectiveInterpretation.CandidateDomains.Contains))
        {
            reasonCodes.Add("real_world_planner_ai_domains_preserved");
        }
        else
        {
            reasonCodes.Add("real_world_planner_domains_backfilled");
        }

        if (effectiveInterpretation.ReasonCodes.Any(code =>
                code.Contains("domains_backfilled_from_fallback", StringComparison.OrdinalIgnoreCase)))
        {
            reasonCodes.Add("real_world_planner_domains_backfilled");
        }

        var commerceEligibility = commerceDomainEligibilityPolicy.Evaluate(
            normalizedQuery,
            effectiveInterpretation,
            selectedDomains);
        if (commerceEligibility.IsCommerceVendorRequest)
        {
            reasonCodes.UnionWith(commerceEligibility.ReasonCodes);
            selectedDomains = ApplyCommerceDomainEligibility(
                selectedDomains,
                commerceEligibility,
                maxDomains,
                reasonCodes);
        }

        if (effectiveInterpretation.RecommendedExecutionMode == RealWorldExecutionMode.ExploratoryMultiDomainSearch
            && selectedDomains.Count < maxDomains)
        {
            reasonCodes.Add("real_world_exploratory_domain_count_reduced_for_fit");
        }

        var useDirectPlacesExecution = effectiveInterpretation.PlacesApplicable
                                       && effectiveInterpretation.RecommendedExecutionMode is
                                           RealWorldExecutionMode.FocusedPlaceSearch
                                           or RealWorldExecutionMode.FocusedThemeSearch
                                           or RealWorldExecutionMode.ExploratoryMultiDomainSearch;

        return new RealWorldExecutionPlan(
            Mode: effectiveInterpretation.RecommendedExecutionMode,
            IntentFamily: effectiveInterpretation.IntentFamily,
            ShouldHandoffToCompanion: true,
            ShouldUsePlaces: true,
            UseDirectPlacesExecution: useDirectPlacesExecution,
            RequiresLocationGrounding: requiresGrounding,
            SelectedDomains: selectedDomains,
            ClarificationPrompt: effectiveInterpretation.ClarificationPrompt,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static IReadOnlyList<RealWorldDiscoveryDomain> ApplyCommerceDomainEligibility(
        IReadOnlyList<RealWorldDiscoveryDomain> selectedDomains,
        RealWorldCommerceEligibilityResult eligibility,
        int maxDomains,
        ISet<string> reasonCodes)
    {
        var allowedSet = eligibility.AllowedDomains.ToHashSet();
        var preferredOrder = eligibility.PreferredDomains
            .Select((domain, index) => new { domain, index })
            .ToDictionary(static x => x.domain, static x => x.index);

        var filtered = selectedDomains
            .Where(allowedSet.Contains)
            .Distinct()
            .ToList();
        foreach (var domain in filtered)
        {
            reasonCodes.Add($"real_world_commerce_domain_allowed:{domain.ToString().ToLowerInvariant()}");
        }

        foreach (var excluded in selectedDomains.Where(domain => !filtered.Contains(domain)))
        {
            reasonCodes.Add($"real_world_commerce_domain_excluded:{excluded.ToString().ToLowerInvariant()}");
        }

        if (filtered.Count == 0)
        {
            filtered = eligibility.PreferredDomains
                .Distinct()
                .Take(Math.Max(1, maxDomains))
                .ToList();
            reasonCodes.Add("real_world_commerce_domain_fallback_applied");
        }

        return filtered
            .OrderBy(domain => preferredOrder.TryGetValue(domain, out var rank) ? rank : int.MaxValue)
            .ThenBy(static domain => domain)
            .Take(Math.Max(1, maxDomains))
            .ToArray();
    }

    private static bool ShouldEnableExploratoryExecutionByContext(
        string normalizedQuery,
        RealWorldIntentInterpretation interpretation,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        bool hasAnyGrounding)
    {
        if (!hasAnyGrounding)
        {
            return false;
        }

        if (interpretation.IntentFamily == RealWorldIntentFamily.FinancialGuidance
            || interpretation.FinancialRelated)
        {
            return false;
        }

        var hasExploratorySignals = interpretation.Exploratory
                                    || interpretation.IntentFamily == RealWorldIntentFamily.ExploratoryAssistance
                                    || localDiscovery.IsLocalDiscoveryCandidate
                                    || ContainsExploratorySignal(normalizedQuery);
        if (!hasExploratorySignals)
        {
            return false;
        }

        return HasTemporalOutingSignal(localDiscovery, normalizedQuery);
    }

    private static RealWorldIntentInterpretation PromoteToExploratoryExecution(
        RealWorldIntentInterpretation interpretation,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        IReadOnlyList<RealWorldDiscoveryDomain> promotedDomains = interpretation.CandidateDomains
            .Where(domain => domain is not RealWorldDiscoveryDomain.ExploratoryEveningActivity
                              and not RealWorldDiscoveryDomain.ExploratoryFamilyActivity)
            .Distinct()
            .Take(8)
            .ToArray();
        if (promotedDomains.Count == 0)
        {
            promotedDomains = BuildFallbackExploratoryDomains(localDiscovery);
        }

        var promotedReasonCodes = new HashSet<string>(interpretation.ReasonCodes, StringComparer.Ordinal)
        {
            "real_world_exploratory_execution_enabled_by_context",
            "real_world_exploratory_mode_selected"
        };

        return interpretation with
        {
            IntentFamily = interpretation.IntentFamily == RealWorldIntentFamily.Ambiguous
                ? RealWorldIntentFamily.ExploratoryAssistance
                : interpretation.IntentFamily,
            RecommendedExecutionMode = RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            PlacesApplicable = true,
            FinancialRelated = false,
            RequiresLocation = true,
            Exploratory = true,
            ClarificationNeeded = false,
            Confidence = Math.Max(interpretation.Confidence, 0.52d),
            CandidateDomains = promotedDomains,
            ClarificationPrompt = null,
            ReasonCodes = promotedReasonCodes.ToArray()
        };
    }

    private static IReadOnlyList<RealWorldDiscoveryDomain> BuildFallbackExploratoryDomains(
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var fallback = new List<RealWorldDiscoveryDomain>(4)
        {
            RealWorldDiscoveryDomain.EntertainmentGeneral
        };
        if (localDiscovery.AudienceHints.Any(value =>
                string.Equals(value, "kids", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "family", StringComparison.OrdinalIgnoreCase)))
        {
            fallback.Add(RealWorldDiscoveryDomain.Playground);
            fallback.Add(RealWorldDiscoveryDomain.ExploratoryFamilyActivity);
        }
        else
        {
            fallback.Add(RealWorldDiscoveryDomain.FoodDrinkGeneral);
            fallback.Add(RealWorldDiscoveryDomain.NightlifeGeneral);
        }

        if (localDiscovery.PreferenceHints.Any(value => string.Equals(value, "quiet", StringComparison.OrdinalIgnoreCase)))
        {
            fallback.Add(RealWorldDiscoveryDomain.ParkWalk);
        }

        return fallback
            .Distinct()
            .Take(4)
            .ToArray();
    }

    private static bool HasTemporalOutingSignal(
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        string normalizedQuery)
    {
        if (localDiscovery.TimeHints.Any(value =>
                string.Equals(value, "tonight", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "weekend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "today", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return normalizedQuery.Contains("tonight", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("later tonight", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("this evening", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("evening", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("later", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("this weekend", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsExploratorySignal(string normalizedQuery)
    {
        return normalizedQuery.Contains("what can i do", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("what should i do", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("where should i go", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("something fun", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("things to do", StringComparison.OrdinalIgnoreCase)
               || normalizedQuery.Contains("find something to do", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RealWorldPlacesExecutionService(
    IPlacesSearchService placesSearchService,
    ILogger<RealWorldPlacesExecutionService> logger,
    IRealWorldProductDomainEligibilityPolicy? productDomainEligibilityPolicy = null) : IRealWorldPlacesExecutionService
{
    private readonly IRealWorldProductDomainEligibilityPolicy commerceDomainEligibilityPolicy =
        productDomainEligibilityPolicy ?? new RealWorldProductDomainEligibilityPolicy();

    public async Task<RealWorldPlacesExecutionResult> ExecuteAsync(
        RealWorldPlacesExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var maxDomains = Math.Clamp(request.MaxDomains, 1, 4);
        var maxPerDomain = Math.Clamp(request.MaxItemsPerDomain, 1, 8);
        var maxTotal = Math.Clamp(request.MaxTotalItems, 1, 8);
        var selectedDomains = request.Domains
            .Distinct()
            .Take(maxDomains)
            .ToArray();
        var warnings = new HashSet<string>(StringComparer.Ordinal)
        {
            "real_world_places_execution_started"
        };
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            $"real_world_places_mode:{request.Mode.ToString().ToLowerInvariant()}"
        };
        if (request.RetrievalPlan is not null)
        {
            reasonCodes.Add($"real_world_retrieval_plan_authoritative:{request.RetrievalPlan.Authoritative.ToString().ToLowerInvariant()}");
            reasonCodes.Add($"real_world_retrieval_plan_near_me:{request.RetrievalPlan.HasNearMeSemantic.ToString().ToLowerInvariant()}");
            reasonCodes.Add($"real_world_retrieval_plan_requested_shortlist:{request.RetrievalPlan.RequestedShortlistSize}");
            foreach (var selectedDomain in request.RetrievalPlan.SelectedDomains)
            {
                reasonCodes.Add($"real_world_retrieval_plan_domain:{selectedDomain.ToString().ToLowerInvariant()}");
            }

            foreach (var concept in request.RetrievalPlan.CanonicalConcepts.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                reasonCodes.Add($"real_world_retrieval_plan_concept:{concept.Trim().ToLowerInvariant()}");
            }
        }

        var eligibilityInterpretation = BuildEligibilityInterpretation(request, selectedDomains);
        var commerceEligibility = commerceDomainEligibilityPolicy.Evaluate(
            request.UserQuery,
            eligibilityInterpretation,
            selectedDomains);
        if (commerceEligibility.IsCommerceVendorRequest)
        {
            reasonCodes.UnionWith(commerceEligibility.ReasonCodes);
            var allowed = selectedDomains
                .Where(commerceEligibility.AllowedDomains.Contains)
                .Distinct()
                .ToArray();
            foreach (var domain in allowed)
            {
                reasonCodes.Add($"real_world_commerce_domain_allowed:{ToReasonToken(domain)}");
            }

            foreach (var excluded in selectedDomains.Where(domain => !allowed.Contains(domain)))
            {
                reasonCodes.Add($"real_world_commerce_domain_excluded:{ToReasonToken(excluded)}");
            }

            if (allowed.Length > 0)
            {
                selectedDomains = allowed;
            }

            if ((request.LocationContext?.Latitude.HasValue == true && request.LocationContext.Longitude.HasValue)
                && !(request.RetrievalPlan?.HasNearMeSemantic ?? false))
            {
                reasonCodes.Add("real_world_commerce_local_bias_enabled");
            }
        }

        if (selectedDomains.Length > 1)
        {
            reasonCodes.Add("real_world_domain_retrieval_isolated:true");
        }

        var providerFailureCount = 0;
        var requestFailureCount = 0;
        var buckets = new List<DomainExecutionBucket>(selectedDomains.Length);
        foreach (var domain in selectedDomains)
        {
            var query = BuildDomainAlignedQuery(
                request.UserQuery,
                domain,
                request.RetrievalPlan,
                commerceEligibility,
                reasonCodes);
            var domainLocationContext = BuildDomainLocationContext(
                request.LocationContext,
                request,
                domain,
                maxTotal);
            var result = await placesSearchService.SearchAsync(
                query,
                request.CountryCode,
                domainLocationContext,
                cancellationToken);
            warnings.UnionWith(result.Warnings ?? []);
            reasonCodes.Add($"real_world_places_provider_candidates:{ToReasonToken(domain)}:{result.Items.Count}");
            reasonCodes.Add($"real_world_places_results_returned:{ToReasonToken(domain)}:{result.Items.Count}");

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

            var filteredItems = ApplyDomainConsistencyFilter(
                result.Items,
                domain,
                request.Mode,
                request.RetrievalPlan,
                reasonCodes);
            if (filteredItems.Count < result.Items.Count)
            {
                reasonCodes.Add("real_world_places_surface_quality_trim_applied");
            }

            if (filteredItems.Count == 0)
            {
                reasonCodes.Add($"real_world_places_domain_no_results:{domain}");
                continue;
            }

            buckets.Add(new DomainExecutionBucket(
                Domain: domain,
                Label: domain.ToLabel(),
                Items: filteredItems,
                Warnings: result.Warnings ?? []));
        }

        if (buckets.Count == 0)
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

        if (request.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch
            && selectedDomains.Length <= 2
            && maxPerDomain >= 4)
        {
            reasonCodes.Add("real_world_exploratory_per_domain_item_boost_applied");
        }

        var groups = ApplyCrossDomainDedupeAndPack(
            buckets,
            maxPerDomain,
            maxTotal,
            reasonCodes,
            out var totalItems);
        if (groups.Count == 0)
        {
            reasonCodes.Add($"real_world_places_failure:{RealWorldFailureScenario.NoMatchesFound}");
            return new RealWorldPlacesExecutionResult(
                Succeeded: false,
                HasAnyResults: false,
                IsPartial: false,
                Groups: [],
                FailureScenario: RealWorldFailureScenario.NoMatchesFound,
                ReasonCodes: reasonCodes.ToArray(),
                Warnings: warnings.ToArray());
        }

        foreach (var domain in selectedDomains)
        {
            var surfacedCount = groups
                .Where(group => group.Domain == domain)
                .SelectMany(group => group.Items)
                .Count();
            reasonCodes.Add($"real_world_places_domain_results:{domain}:{surfacedCount}");
            reasonCodes.Add($"real_world_places_results_surfaced:{ToReasonToken(domain)}:{surfacedCount}");
            if (surfacedCount > 0)
            {
                reasonCodes.Add($"real_world_places_surface_cap_reason:max_per_domain_{maxPerDomain}");
            }

            var sourceBucket = buckets.FirstOrDefault(bucket => bucket.Domain == domain);
            if (sourceBucket is not null && surfacedCount < sourceBucket.Items.Count)
            {
                reasonCodes.Add("real_world_places_surface_quality_trim_applied");
            }
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

    private static RealWorldIntentInterpretation BuildEligibilityInterpretation(
        RealWorldPlacesExecutionRequest request,
        IReadOnlyList<RealWorldDiscoveryDomain> selectedDomains)
    {
        return new RealWorldIntentInterpretation(
            IntentFamily: request.RetrievalPlan?.IntentFamily ?? RealWorldIntentFamily.Ambiguous,
            RecommendedExecutionMode: request.Mode,
            PlacesApplicable: true,
            FinancialRelated: false,
            RequiresLocation: false,
            Exploratory: request.Mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            ClarificationNeeded: false,
            HasNearMeLanguage: request.RetrievalPlan?.HasNearMeSemantic ?? false,
            HasExplicitLocality: !string.IsNullOrWhiteSpace(request.LocationContext?.TypedArea),
            Confidence: 0.8d,
            CandidateDomains: selectedDomains,
            ClarificationPrompt: null,
            ReasonCodes: [],
            Warnings: [])
        {
            CandidateConcepts = request.RetrievalPlan?.CanonicalConcepts ?? []
        };
    }

    private static string BuildDomainAlignedQuery(
        string userQuery,
        RealWorldDiscoveryDomain domain,
        RealWorldPlaceRetrievalPlan? retrievalPlan,
        RealWorldCommerceEligibilityResult commerceEligibility,
        ISet<string> reasonCodes)
    {
        if (commerceEligibility.IsCommerceVendorRequest
            || retrievalPlan?.IntentFamily == RealWorldIntentFamily.CommerceDiscovery)
        {
            var queryFamilyToken = ToCommerceQueryFamilyToken(domain);
            reasonCodes.Add($"real_world_retrieval_query_family:{queryFamilyToken}");
            var productHint = ResolveCommerceProductHint(retrievalPlan, commerceEligibility, userQuery);
            if (!string.IsNullOrWhiteSpace(productHint))
            {
                return queryFamilyToken switch
                {
                    "electronics_retail" => $"{productHint} electronics store video game store",
                    "convenience_store" => $"{productHint} convenience store",
                    "grocery_store" => $"{productHint} grocery store supermarket",
                    "petrol_station" => $"{productHint} petrol station convenience store",
                    "shopping_retail" => $"{productHint} shopping stores",
                    _ => $"{productHint} stores"
                };
            }

            return queryFamilyToken switch
            {
                "electronics_retail" => "electronics store video game store",
                "convenience_store" => "convenience store",
                "grocery_store" => "grocery store supermarket",
                "petrol_station" => "petrol station convenience store",
                "shopping_retail" => "shopping stores",
                _ => "stores"
            };
        }

        var domainPhrase = domain.ToQueryPhrase();
        reasonCodes.Add($"real_world_retrieval_query_family:{ToQueryFamilyToken(domain)}");
        if (retrievalPlan?.Authoritative == true)
        {
            reasonCodes.Add("real_world_retrieval_domain_aligned_query_used");
            return domainPhrase;
        }

        if (string.IsNullOrWhiteSpace(userQuery))
        {
            return domainPhrase;
        }

        if (userQuery.Contains(domainPhrase, StringComparison.OrdinalIgnoreCase))
        {
            return userQuery.Trim();
        }

        return $"{userQuery.Trim()} {domainPhrase}".Trim();
    }

    private static string ResolveCommerceProductHint(
        RealWorldPlaceRetrievalPlan? retrievalPlan,
        RealWorldCommerceEligibilityResult commerceEligibility,
        string userQuery)
    {
        var candidate = retrievalPlan?.CommerceProductHints?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                        ?? commerceEligibility.ProductHints.FirstOrDefault()
                        ?? retrievalPlan?.CanonicalConcepts.FirstOrDefault(value =>
                            value.Contains("ps5", StringComparison.OrdinalIgnoreCase)
                            || value.Contains("xbox", StringComparison.OrdinalIgnoreCase)
                            || value.Contains("playstation", StringComparison.OrdinalIgnoreCase)
                            || value.Contains("red", StringComparison.OrdinalIgnoreCase)
                            || value.Contains("drink", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = GuessProductHintFromQuery(userQuery);
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var normalized = candidate
            .Trim()
            .Replace('_', ' ')
            .ToLowerInvariant();
        return normalized.Length > 40 ? normalized[..40].TrimEnd() : normalized;
    }

    private static string GuessProductHintFromQuery(string userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            return string.Empty;
        }

        var normalized = userQuery.Trim().ToLowerInvariant();
        if (normalized.Contains("ps5", StringComparison.Ordinal))
        {
            return "ps5";
        }

        if (normalized.Contains("xbox", StringComparison.Ordinal))
        {
            return "xbox";
        }

        if (normalized.Contains("red bull", StringComparison.Ordinal)
            || normalized.Contains("redbull", StringComparison.Ordinal))
        {
            return "red bull";
        }

        if (normalized.Contains("controller", StringComparison.Ordinal))
        {
            return "controller";
        }

        if (normalized.Contains("laptop", StringComparison.Ordinal))
        {
            return "laptop";
        }

        return string.Empty;
    }

    private static string ToCommerceQueryFamilyToken(RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.ElectronicsRetail => "electronics_retail",
            RealWorldDiscoveryDomain.ConvenienceStore => "convenience_store",
            RealWorldDiscoveryDomain.Grocery => "grocery_store",
            RealWorldDiscoveryDomain.PetrolStation => "petrol_station",
            RealWorldDiscoveryDomain.ShoppingGeneral => "shopping_retail",
            _ => "general_retail"
        };
    }

    private static string ToQueryFamilyToken(RealWorldDiscoveryDomain domain)
    {
        return domain.ToString().ToLowerInvariant();
    }

    private static string ToReasonToken(RealWorldDiscoveryDomain domain)
    {
        return domain.ToString().ToLowerInvariant();
    }

    private static PlaceSearchLocationContext? BuildDomainLocationContext(
        PlaceSearchLocationContext? locationContext,
        RealWorldPlacesExecutionRequest request,
        RealWorldDiscoveryDomain domain,
        int maxTotal)
    {
        if (locationContext is null)
        {
            return null;
        }

        var concept = request.RetrievalPlan?.CanonicalConcepts.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value));
        return locationContext with
        {
            PlannerSelectedDomain = domain,
            PlannerSelectedConcept = concept,
            PlannerIntentFamily = request.RetrievalPlan?.IntentFamily ?? locationContext.PlannerIntentFamily,
            PlannerAuthoritative = request.RetrievalPlan?.Authoritative ?? locationContext.PlannerAuthoritative,
            HasNearMeSemantic = request.RetrievalPlan?.HasNearMeSemantic ?? locationContext.HasNearMeSemantic,
            ImplicitLocalBias = request.RetrievalPlan?.EnableImplicitLocalBias ?? locationContext.ImplicitLocalBias,
            PlannerExecutionMode = request.Mode,
            PlannerMaxShortlist = maxTotal
        };
    }

    private static IReadOnlyList<RealWorldDomainPlacesGroup> ApplyCrossDomainDedupeAndPack(
        IReadOnlyList<DomainExecutionBucket> buckets,
        int maxPerDomain,
        int maxTotal,
        ISet<string> reasonCodes,
        out int totalItems)
    {
        totalItems = 0;
        var winnerByPlaceId = new Dictionary<string, DomainWinner>(StringComparer.OrdinalIgnoreCase);
        var contestedPlaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in buckets)
        {
            for (var index = 0; index < bucket.Items.Count; index += 1)
            {
                var item = bucket.Items[index];
                if (string.IsNullOrWhiteSpace(item.PlaceId))
                {
                    continue;
                }

                var score = ScoreDomainWinner(bucket.Domain, item, index);
                if (winnerByPlaceId.TryGetValue(item.PlaceId, out var existing))
                {
                    contestedPlaceIds.Add(item.PlaceId);
                    if (score > existing.Score)
                    {
                        winnerByPlaceId[item.PlaceId] = new DomainWinner(bucket.Domain, score);
                    }
                }
                else
                {
                    winnerByPlaceId[item.PlaceId] = new DomainWinner(bucket.Domain, score);
                }
            }
        }

        if (contestedPlaceIds.Count > 0)
        {
            reasonCodes.Add("real_world_cross_domain_dedupe_applied");
            foreach (var winnerDomain in contestedPlaceIds
                         .Select(id => winnerByPlaceId[id].Domain)
                         .Distinct())
            {
                reasonCodes.Add($"real_world_cross_domain_dedupe_winner:{ToReasonToken(winnerDomain)}");
            }
        }

        var groups = new List<RealWorldDomainPlacesGroup>(buckets.Count);
        foreach (var bucket in buckets)
        {
            if (totalItems >= maxTotal)
            {
                break;
            }

            var items = new List<PlaceSearchItem>(maxPerDomain);
            foreach (var item in bucket.Items)
            {
                if (items.Count >= maxPerDomain || totalItems >= maxTotal)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(item.PlaceId))
                {
                    continue;
                }

                if (winnerByPlaceId.TryGetValue(item.PlaceId, out var winner)
                    && winner.Domain != bucket.Domain)
                {
                    continue;
                }

                if (items.Any(existing =>
                        existing.PlaceId.Equals(item.PlaceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                items.Add(item);
                totalItems += 1;
            }

            if (items.Count == 0)
            {
                continue;
            }

            groups.Add(new RealWorldDomainPlacesGroup(
                Domain: bucket.Domain,
                Label: bucket.Label,
                Items: items,
                Warnings: bucket.Warnings));
        }

        return groups;
    }

    private static int ScoreDomainWinner(
        RealWorldDiscoveryDomain domain,
        PlaceSearchItem item,
        int rankIndex)
    {
        var profile = GetDomainConsistencyProfile(domain, strictCommerce: false);
        var compatibilityScore = ScoreDomainCompatibility(item, profile);
        return (GetDomainPriority(domain) * 100) + (compatibilityScore * 10) - rankIndex;
    }

    private static int GetDomainPriority(RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.PubBar => 10,
            RealWorldDiscoveryDomain.ElectronicsRetail => 10,
            RealWorldDiscoveryDomain.ConvenienceStore => 10,
            RealWorldDiscoveryDomain.Grocery => 10,
            RealWorldDiscoveryDomain.Cafe => 9,
            RealWorldDiscoveryDomain.Restaurant => 9,
            RealWorldDiscoveryDomain.MovieTheater => 9,
            RealWorldDiscoveryDomain.PetrolStation => 8,
            RealWorldDiscoveryDomain.ShoppingGeneral => 7,
            RealWorldDiscoveryDomain.NightlifeGeneral => 6,
            RealWorldDiscoveryDomain.FoodDrinkGeneral => 6,
            RealWorldDiscoveryDomain.EntertainmentGeneral => 6,
            RealWorldDiscoveryDomain.CommerceGeneral => 5,
            _ => 4
        };
    }

    private static int ScoreDomainCompatibility(
        PlaceSearchItem item,
        DomainConsistencyProfile profile)
    {
        if (profile.PositiveHints.Count == 0 && profile.NegativeHints.Count == 0)
        {
            return 0;
        }

        var typeHaystacks = GetTypeHaystacks(item);
        var textHaystacks = GetTextHaystacks(item);
        var hasNegative = typeHaystacks.Any(value =>
                              profile.NegativeHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                          || textHaystacks.Any(value =>
                              profile.NegativeHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        if (hasNegative)
        {
            return -4;
        }

        var typeMatch = typeHaystacks.Any(value =>
            profile.PositiveHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        var textMatch = textHaystacks.Any(value =>
            profile.PositiveHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        if (typeMatch && textMatch)
        {
            return 5;
        }

        if (typeMatch)
        {
            return 4;
        }

        if (textMatch)
        {
            return 2;
        }

        return 0;
    }

    private static IReadOnlyList<PlaceSearchItem> ApplyDomainConsistencyFilter(
        IReadOnlyList<PlaceSearchItem> items,
        RealWorldDiscoveryDomain domain,
        RealWorldExecutionMode mode,
        RealWorldPlaceRetrievalPlan? retrievalPlan,
        ISet<string> reasonCodes)
    {
        var strictCommerce = retrievalPlan?.IntentFamily == RealWorldIntentFamily.CommerceDiscovery;
        if (items.Count == 0
            || (mode != RealWorldExecutionMode.FocusedPlaceSearch && !strictCommerce))
        {
            return items;
        }

        var profile = GetDomainConsistencyProfile(domain, strictCommerce);
        if (profile.PositiveHints.Count == 0 && profile.NegativeHints.Count == 0)
        {
            return items;
        }

        var filtered = items
            .Where(item => IsDomainCompatible(item, profile))
            .ToArray();
        if (filtered.Length == 0)
        {
            if (strictCommerce)
            {
                reasonCodes.Add($"real_world_places_domain_filter_zero_matches:{ToReasonToken(domain)}");
                return [];
            }

            reasonCodes.Add("real_world_places_domain_filter_no_matches_fallback_unfiltered");
            return items;
        }

        if (filtered.Length < items.Count)
        {
            reasonCodes.Add($"real_world_places_domain_filter_applied:{domain.ToString().ToLowerInvariant()}");
        }

        return filtered;
    }

    private static DomainConsistencyProfile GetDomainConsistencyProfile(
        RealWorldDiscoveryDomain domain,
        bool strictCommerce)
    {
        var tourismExclusions = strictCommerce
            ? new[]
            {
                "tourist_attraction",
                "museum",
                "park",
                "zoo",
                "cemetery",
                "castle",
                "historical",
                "church"
            }
            : Array.Empty<string>();
        return domain switch
        {
            RealWorldDiscoveryDomain.Cafe => new DomainConsistencyProfile(
                PositiveHints: ["cafe", "coffee_shop", "coffee"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.Restaurant => new DomainConsistencyProfile(
                PositiveHints: ["restaurant", "food"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.Takeaway => new DomainConsistencyProfile(
                PositiveHints: ["takeaway", "meal_takeaway", "fast_food"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.PubBar => new DomainConsistencyProfile(
                PositiveHints: ["pub", "bar", "night_club"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.NightlifeGeneral => new DomainConsistencyProfile(
                PositiveHints: ["bar", "night_club", "pub"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.MovieTheater => new DomainConsistencyProfile(
                PositiveHints: ["movie_theater", "cinema", "theater", "theatre"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.ParkWalk => new DomainConsistencyProfile(
                PositiveHints: ["park", "hiking_area", "trail", "outdoor"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.Playground => new DomainConsistencyProfile(
                PositiveHints: ["playground"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.Pharmacy => new DomainConsistencyProfile(
                PositiveHints: ["pharmacy", "drugstore", "chemist"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.PetrolStation => new DomainConsistencyProfile(
                PositiveHints: ["gas_station", "petrol", "fuel"],
                NegativeHints: strictCommerce ? tourismExclusions : []),
            RealWorldDiscoveryDomain.Gym => new DomainConsistencyProfile(
                PositiveHints: ["gym", "fitness"],
                NegativeHints: []),
            RealWorldDiscoveryDomain.ElectronicsRetail => new DomainConsistencyProfile(
                PositiveHints: ["electronics_store", "computer_store", "mobile_phone_store", "video_game_store", "electronics"],
                NegativeHints: strictCommerce ? tourismExclusions : [],
                RequireTypeMatch: strictCommerce),
            RealWorldDiscoveryDomain.ConvenienceStore => new DomainConsistencyProfile(
                PositiveHints: ["convenience_store", "grocery_store", "supermarket", "food_store"],
                NegativeHints: strictCommerce ? tourismExclusions : [],
                RequireTypeMatch: strictCommerce),
            RealWorldDiscoveryDomain.Grocery => new DomainConsistencyProfile(
                PositiveHints: ["supermarket", "grocery_store", "market", "food_store"],
                NegativeHints: strictCommerce ? tourismExclusions : [],
                RequireTypeMatch: strictCommerce),
            RealWorldDiscoveryDomain.ShoppingGeneral => new DomainConsistencyProfile(
                PositiveHints: ["store", "shopping_mall", "department_store"],
                NegativeHints: strictCommerce ? tourismExclusions : []),
            RealWorldDiscoveryDomain.CommerceGeneral => new DomainConsistencyProfile(
                PositiveHints: ["store", "shopping_mall", "department_store", "market"],
                NegativeHints: strictCommerce ? tourismExclusions : []),
            _ => new DomainConsistencyProfile(PositiveHints: [], NegativeHints: [])
        };
    }

    private static bool IsDomainCompatible(PlaceSearchItem item, DomainConsistencyProfile profile)
    {
        if (profile.PositiveHints.Count == 0 && profile.NegativeHints.Count == 0)
        {
            return true;
        }

        var typeHaystacks = GetTypeHaystacks(item);
        var textHaystacks = GetTextHaystacks(item);
        var hasNegative = typeHaystacks.Any(value =>
                              profile.NegativeHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                          || textHaystacks.Any(value =>
                              profile.NegativeHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        if (hasNegative)
        {
            return false;
        }

        if (profile.PositiveHints.Count == 0)
        {
            return true;
        }

        var typeMatch = typeHaystacks.Any(value =>
            profile.PositiveHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        var textMatch = textHaystacks.Any(value =>
            profile.PositiveHints.Any(hint => value.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        return profile.RequireTypeMatch
            ? typeMatch
            : typeMatch || textMatch;
    }

    private static IReadOnlyList<string> GetTypeHaystacks(PlaceSearchItem item)
    {
        var values = new List<string>(8);
        if (!string.IsNullOrWhiteSpace(item.PrimaryType))
        {
            values.Add(item.PrimaryType!);
        }

        if (!string.IsNullOrWhiteSpace(item.PrimaryTypeDisplayName))
        {
            values.Add(item.PrimaryTypeDisplayName!);
        }

        if (item.Types is not null)
        {
            values.AddRange(item.Types.Where(static value => !string.IsNullOrWhiteSpace(value)));
        }

        return values;
    }

    private static IReadOnlyList<string> GetTextHaystacks(PlaceSearchItem item)
    {
        var values = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            values.Add(item.Category!);
        }

        if (!string.IsNullOrWhiteSpace(item.DisplayName))
        {
            values.Add(item.DisplayName!);
        }

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            values.Add(item.Name);
        }

        return values;
    }

    private sealed record DomainExecutionBucket(
        RealWorldDiscoveryDomain Domain,
        string Label,
        IReadOnlyList<PlaceSearchItem> Items,
        IReadOnlyList<string> Warnings);

    private sealed record DomainWinner(
        RealWorldDiscoveryDomain Domain,
        int Score);

    private sealed record DomainConsistencyProfile(
        IReadOnlyList<string> PositiveHints,
        IReadOnlyList<string> NegativeHints,
        bool RequireTypeMatch = false);
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

