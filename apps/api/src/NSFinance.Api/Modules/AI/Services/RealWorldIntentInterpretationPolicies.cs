using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class RealWorldDeterministicFallbackBuilder : IRealWorldDeterministicFallbackBuilder
{
    public RealWorldIntentInterpretation BuildSeed(
        string userMessage,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var normalized = Normalize(userMessage);
        var financePlanning = IsFinancePlanningQuestion(normalized);
        var explicitVendorLookup = IsExplicitVendorLookup(normalized);
        var nearMe = localDiscovery.HasNearMeLanguage
                     || CompanionLocationGroundingParser.RequiresCurrentLocation(userMessage);
        var hasExplicitLocality = localDiscovery.HasExplicitLocality;
        var seedDomains = MapPlaceHintsToDomains(localDiscovery.PlaceTypeHints)
            .Distinct()
            .Take(4)
            .ToArray();
        var seedConcepts = localDiscovery.PlaceTypeHints
            .Where(static hint => !string.IsNullOrWhiteSpace(hint))
            .Select(NormalizeConceptToken)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "real_world_seed_built",
            $"real_world_seed_local_discovery_confidence:{localDiscovery.Confidence:0.##}"
        };
        if (nearMe)
        {
            reasonCodes.Add("real_world_seed_near_me_detected");
        }

        if (hasExplicitLocality)
        {
            reasonCodes.Add("real_world_seed_explicit_locality_detected");
        }

        if (financePlanning && !explicitVendorLookup)
        {
            reasonCodes.Add("real_world_seed_financial_guardrail_signal");
            return new RealWorldIntentInterpretation(
                IntentFamily: RealWorldIntentFamily.FinancialGuidance,
                RecommendedExecutionMode: RealWorldExecutionMode.FinancialGuidanceOnly,
                PlacesApplicable: false,
                FinancialRelated: true,
                RequiresLocation: false,
                Exploratory: false,
                ClarificationNeeded: false,
                HasNearMeLanguage: nearMe,
                HasExplicitLocality: hasExplicitLocality,
                Confidence: 0.72d,
                CandidateDomains: [],
                ClarificationPrompt: null,
                ReasonCodes: reasonCodes.ToArray(),
                Warnings: [])
            {
                CandidateConcepts = seedConcepts,
                InterpretationSource = RealWorldInterpretationSource.DeterministicFallback
            };
        }

        if (localDiscovery.IsLocalDiscoveryCandidate)
        {
            reasonCodes.Add("real_world_seed_places_candidate");
            return new RealWorldIntentInterpretation(
                IntentFamily: RealWorldIntentFamily.PlaceDiscovery,
                RecommendedExecutionMode: localDiscovery.Confidence >= 0.82d
                    && IsExploratoryPrompt(normalized)
                    ? RealWorldExecutionMode.ExploratoryMultiDomainSearch
                    : RealWorldExecutionMode.FocusedPlaceSearch,
                PlacesApplicable: true,
                FinancialRelated: false,
                RequiresLocation: nearMe || !hasExplicitLocality,
                Exploratory: IsExploratoryPrompt(normalized),
                ClarificationNeeded: false,
                HasNearMeLanguage: nearMe,
                HasExplicitLocality: hasExplicitLocality,
                Confidence: Math.Max(0.58d, Math.Min(0.82d, localDiscovery.Confidence)),
                CandidateDomains: seedDomains.Length == 0
                    ? [RealWorldDiscoveryDomain.EntertainmentGeneral]
                    : seedDomains,
                ClarificationPrompt: null,
                ReasonCodes: reasonCodes.ToArray(),
                Warnings: [])
            {
                CandidateConcepts = seedConcepts,
                InterpretationSource = RealWorldInterpretationSource.DeterministicFallback
            };
        }

        reasonCodes.Add("real_world_seed_ambiguous");
        return new RealWorldIntentInterpretation(
            IntentFamily: RealWorldIntentFamily.Ambiguous,
            RecommendedExecutionMode: RealWorldExecutionMode.ClarifyLight,
            PlacesApplicable: false,
            FinancialRelated: financePlanning,
            RequiresLocation: false,
            Exploratory: false,
            ClarificationNeeded: true,
            HasNearMeLanguage: nearMe,
            HasExplicitLocality: hasExplicitLocality,
            Confidence: 0.40d,
            CandidateDomains: seedDomains,
            ClarificationPrompt: "Do you want nearby places, financial guidance, or a specific area to search?",
            ReasonCodes: reasonCodes.ToArray(),
            Warnings: ["real_world_seed_low_confidence"])
        {
            CandidateConcepts = seedConcepts,
            InterpretationSource = RealWorldInterpretationSource.DeterministicFallback
        };
    }

    public RealWorldIntentInterpretation BuildFallback(
        string userMessage,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var normalized = Normalize(userMessage);
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "real_world_interpreter_deterministic_fallback_used"
        };
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var domains = ExtractFallbackDomains(normalized);
        var candidateConcepts = domains
            .Select(ToCanonicalConcept)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();

        var nearMe = localDiscovery.HasNearMeLanguage || CompanionLocationGroundingParser.RequiresCurrentLocation(userMessage);
        var explicitLocality = localDiscovery.HasExplicitLocality;
        var financePlanning = IsFinancePlanningQuestion(normalized);
        var vendorLookup = IsExplicitVendorLookup(normalized);
        var exploratory = IsExploratoryPrompt(normalized);
        var serviceFocused = IsServicePrompt(normalized);
        var themedFoodDrink = IsFoodDrinkTheme(normalized) || IsOutdoorTheme(normalized);

        RealWorldIntentFamily family;
        RealWorldExecutionMode mode;
        var placesApplicable = false;
        var financialRelated = financePlanning;
        var clarificationNeeded = false;
        double confidence;

        if (financePlanning && !vendorLookup)
        {
            family = RealWorldIntentFamily.FinancialGuidance;
            mode = RealWorldExecutionMode.FinancialGuidanceOnly;
            confidence = 0.88d;
            domains.Clear();
            candidateConcepts = [];
            reasonCodes.Add("real_world_financial_guardrail_triggered");
        }
        else if (exploratory)
        {
            family = RealWorldIntentFamily.ExploratoryAssistance;
            mode = RealWorldExecutionMode.ExploratoryMultiDomainSearch;
            placesApplicable = true;
            confidence = 0.82d;
            reasonCodes.Add("real_world_exploratory_prompt_detected");
            if (domains.Count == 0)
            {
                domains.Add(RealWorldDiscoveryDomain.ExploratoryEveningActivity);
            }
        }
        else if (vendorLookup)
        {
            var commerceDiscovery = IsCommerceDiscoveryPrompt(normalized, domains);
            family = commerceDiscovery
                ? RealWorldIntentFamily.CommerceDiscovery
                : RealWorldIntentFamily.PlaceDiscovery;
            mode = commerceDiscovery && themedFoodDrink
                ? RealWorldExecutionMode.FocusedThemeSearch
                : RealWorldExecutionMode.FocusedPlaceSearch;
            placesApplicable = true;
            confidence = 0.84d;
            reasonCodes.Add(
                commerceDiscovery
                    ? "real_world_commerce_lookup_detected"
                    : "real_world_vendor_lookup_mapped_to_place_discovery");
            if (domains.Count == 0)
            {
                domains.Add(
                    commerceDiscovery
                        ? RealWorldDiscoveryDomain.CommerceGeneral
                        : RealWorldDiscoveryDomain.EntertainmentGeneral);
            }
        }
        else if (serviceFocused)
        {
            family = RealWorldIntentFamily.ServiceDiscovery;
            mode = RealWorldExecutionMode.FocusedPlaceSearch;
            placesApplicable = true;
            confidence = 0.78d;
            reasonCodes.Add("real_world_service_discovery_detected");
            if (domains.Count == 0)
            {
                domains.Add(RealWorldDiscoveryDomain.ServiceGeneral);
            }
        }
        else if (localDiscovery.IsLocalDiscoveryCandidate || domains.Count > 0)
        {
            family = RealWorldIntentFamily.PlaceDiscovery;
            mode = themedFoodDrink
                ? RealWorldExecutionMode.FocusedThemeSearch
                : RealWorldExecutionMode.FocusedPlaceSearch;
            placesApplicable = true;
            confidence = Math.Max(0.68d, localDiscovery.Confidence);
            reasonCodes.Add("real_world_place_discovery_detected");
            if (domains.Count == 0)
            {
                domains.Add(RealWorldDiscoveryDomain.EntertainmentGeneral);
            }
        }
        else
        {
            family = RealWorldIntentFamily.Ambiguous;
            mode = RealWorldExecutionMode.ClarifyLight;
            placesApplicable = false;
            clarificationNeeded = true;
            confidence = 0.44d;
            reasonCodes.Add("real_world_ambiguous_prompt");
            warnings.Add("real_world_interpreter_low_confidence");
        }

        var requiresLocation = placesApplicable && (nearMe || !explicitLocality);
        if (nearMe)
        {
            reasonCodes.Add("real_world_near_me_detected");
        }

        if (explicitLocality)
        {
            reasonCodes.Add("real_world_explicit_locality_detected");
        }

        if (themedFoodDrink && mode == RealWorldExecutionMode.FocusedThemeSearch)
        {
            reasonCodes.Add("real_world_themed_mode_selected");
        }

        if (mode == RealWorldExecutionMode.ClarifyLight && string.IsNullOrWhiteSpace(userMessage))
        {
            warnings.Add("real_world_empty_prompt");
        }

        return new RealWorldIntentInterpretation(
            IntentFamily: family,
            RecommendedExecutionMode: mode,
            PlacesApplicable: placesApplicable,
            FinancialRelated: financialRelated,
            RequiresLocation: requiresLocation,
            Exploratory: exploratory,
            ClarificationNeeded: clarificationNeeded,
            HasNearMeLanguage: nearMe,
            HasExplicitLocality: explicitLocality,
            Confidence: Math.Round(Math.Clamp(confidence, 0d, 0.98d), 4),
            CandidateDomains: domains.Distinct().Take(6).ToArray(),
            ClarificationPrompt: clarificationNeeded
                ? "Do you want nearby places, financial guidance, or a specific area to search?"
                : null,
            ReasonCodes: reasonCodes.ToArray(),
            Warnings: warnings.ToArray())
        {
            CandidateConcepts = candidateConcepts,
            InterpretationSource = RealWorldInterpretationSource.DeterministicFallback
        };
    }

    private static List<RealWorldDiscoveryDomain> ExtractFallbackDomains(string normalized)
    {
        var domains = new List<RealWorldDiscoveryDomain>(6);

        void Add(RealWorldDiscoveryDomain domain)
        {
            if (!domains.Contains(domain))
            {
                domains.Add(domain);
            }
        }

        if (ContainsAny(normalized, "coffee", "cafe", "cafes"))
        {
            Add(RealWorldDiscoveryDomain.Cafe);
            Add(RealWorldDiscoveryDomain.FoodDrinkGeneral);
        }

        if (ContainsAny(normalized, "restaurant", "restaurants", "dine", "dining", "eat"))
        {
            Add(RealWorldDiscoveryDomain.Restaurant);
            Add(RealWorldDiscoveryDomain.FoodDrinkGeneral);
        }

        if (ContainsAny(normalized, "takeaway", "takeaways", "take out", "delivery"))
        {
            Add(RealWorldDiscoveryDomain.Takeaway);
            Add(RealWorldDiscoveryDomain.FoodDrinkGeneral);
        }

        if (ContainsAny(normalized, "pub", "pubs", "bar", "bars", "drinks", "drinking"))
        {
            Add(RealWorldDiscoveryDomain.PubBar);
            Add(RealWorldDiscoveryDomain.NightlifeGeneral);
        }

        if (ContainsAny(normalized, "cinema", "cinemas", "movie", "movies", "theater", "theatre", "film"))
        {
            Add(RealWorldDiscoveryDomain.MovieTheater);
            Add(RealWorldDiscoveryDomain.EntertainmentGeneral);
        }

        if (ContainsAny(normalized, "park", "parks", "walk", "walking", "hike", "long walks"))
        {
            Add(RealWorldDiscoveryDomain.ParkWalk);
            Add(RealWorldDiscoveryDomain.OutdoorActivity);
        }

        if (ContainsAny(normalized, "playground", "playgrounds"))
        {
            Add(RealWorldDiscoveryDomain.Playground);
            Add(RealWorldDiscoveryDomain.ExploratoryFamilyActivity);
        }

        if (ContainsAny(normalized, "pharmacy", "chemist"))
        {
            Add(RealWorldDiscoveryDomain.Pharmacy);
            Add(RealWorldDiscoveryDomain.ServiceGeneral);
        }

        if (ContainsAny(normalized, "petrol", "gas station", "fuel"))
        {
            Add(RealWorldDiscoveryDomain.PetrolStation);
            Add(RealWorldDiscoveryDomain.ServiceGeneral);
        }

        if (ContainsAny(normalized, "gym", "fitness"))
        {
            Add(RealWorldDiscoveryDomain.Gym);
            Add(RealWorldDiscoveryDomain.ServiceGeneral);
        }

        if (ContainsAny(normalized, "xbox", "ps5", "playstation", "console", "laptop", "electronics"))
        {
            Add(RealWorldDiscoveryDomain.ElectronicsRetail);
            Add(RealWorldDiscoveryDomain.CommerceGeneral);
        }

        if (ContainsAny(normalized, "red bull", "energy drink"))
        {
            Add(RealWorldDiscoveryDomain.ConvenienceStore);
            Add(RealWorldDiscoveryDomain.Grocery);
        }

        if (ContainsAny(normalized, "fish and chips", "chips"))
        {
            Add(RealWorldDiscoveryDomain.Takeaway);
            Add(RealWorldDiscoveryDomain.Restaurant);
        }

        if (ContainsAny(normalized, "shopping", "mall", "shop for", "buy ", "purchase "))
        {
            Add(RealWorldDiscoveryDomain.ShoppingGeneral);
            Add(RealWorldDiscoveryDomain.CommerceGeneral);
        }

        if (ContainsAny(normalized, "fun", "things to do", "visit", "where can i go"))
        {
            Add(RealWorldDiscoveryDomain.EntertainmentGeneral);
        }

        if (ContainsAny(normalized, "tonight", "this evening", "evening"))
        {
            Add(RealWorldDiscoveryDomain.ExploratoryEveningActivity);
        }

        if (ContainsAny(normalized, "family", "kids", "children"))
        {
            Add(RealWorldDiscoveryDomain.ExploratoryFamilyActivity);
        }

        return domains;
    }

    private static IReadOnlyList<RealWorldDiscoveryDomain> MapPlaceHintsToDomains(
        IReadOnlyList<string> placeTypeHints)
    {
        var mapped = new List<RealWorldDiscoveryDomain>(placeTypeHints.Count);
        foreach (var hint in placeTypeHints)
        {
            switch (hint.Trim().ToLowerInvariant())
            {
                case "cafe":
                    mapped.Add(RealWorldDiscoveryDomain.Cafe);
                    break;
                case "restaurant":
                case "brunch":
                    mapped.Add(RealWorldDiscoveryDomain.Restaurant);
                    break;
                case "museum":
                case "zoo":
                case "tourist_attraction":
                    mapped.Add(RealWorldDiscoveryDomain.EntertainmentGeneral);
                    break;
                case "movie_theater":
                case "performing_arts_theater":
                    mapped.Add(RealWorldDiscoveryDomain.MovieTheater);
                    break;
                case "playground":
                    mapped.Add(RealWorldDiscoveryDomain.Playground);
                    break;
                case "park":
                    mapped.Add(RealWorldDiscoveryDomain.ParkWalk);
                    break;
                case "bar":
                    mapped.Add(RealWorldDiscoveryDomain.PubBar);
                    break;
                case "gas_station":
                    mapped.Add(RealWorldDiscoveryDomain.PetrolStation);
                    break;
                case "pharmacy":
                    mapped.Add(RealWorldDiscoveryDomain.Pharmacy);
                    break;
                case "gym":
                    mapped.Add(RealWorldDiscoveryDomain.Gym);
                    break;
            }
        }

        return mapped.Distinct().ToArray();
    }

    private static string ToCanonicalConcept(RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.Cafe => "cafe",
            RealWorldDiscoveryDomain.Restaurant => "restaurant",
            RealWorldDiscoveryDomain.Takeaway => "takeaway",
            RealWorldDiscoveryDomain.PubBar => "pub_bar",
            RealWorldDiscoveryDomain.MovieTheater => "movie_theater",
            RealWorldDiscoveryDomain.ParkWalk => "park_walk",
            RealWorldDiscoveryDomain.Playground => "playground",
            RealWorldDiscoveryDomain.Pharmacy => "pharmacy",
            RealWorldDiscoveryDomain.PetrolStation => "petrol_station",
            RealWorldDiscoveryDomain.Gym => "gym",
            RealWorldDiscoveryDomain.ElectronicsRetail => "electronics_retail",
            RealWorldDiscoveryDomain.ConvenienceStore => "convenience_store",
            RealWorldDiscoveryDomain.Grocery => "grocery",
            RealWorldDiscoveryDomain.ShoppingGeneral => "shopping_general",
            RealWorldDiscoveryDomain.OutdoorActivity => "outdoor_activity",
            RealWorldDiscoveryDomain.EntertainmentGeneral => "entertainment_general",
            RealWorldDiscoveryDomain.NightlifeGeneral => "nightlife_general",
            RealWorldDiscoveryDomain.FoodDrinkGeneral => "food_drink_general",
            RealWorldDiscoveryDomain.ServiceGeneral => "service_general",
            RealWorldDiscoveryDomain.CommerceGeneral => "commerce_general",
            RealWorldDiscoveryDomain.ExploratoryEveningActivity => "exploratory_evening_activity",
            RealWorldDiscoveryDomain.ExploratoryFamilyActivity => "exploratory_family_activity",
            _ => "place"
        };
    }

    internal static bool IsServicePrompt(string normalized)
    {
        return ContainsAny(normalized, "pharmacy", "chemist", "petrol", "fuel", "gas station", "gym", "fitness");
    }

    internal static bool IsExploratoryPrompt(string normalized)
    {
        return ContainsAny(
            normalized,
            "what can i do",
            "what should i do",
            "something fun",
            "somewhere nice",
            "where should i go",
            "places to visit",
            "things to do");
    }

    internal static bool IsFoodDrinkTheme(string normalized)
    {
        return ContainsAny(normalized, "what should i eat", "something to eat", "something to drink", "food", "drink");
    }

    internal static bool IsOutdoorTheme(string normalized)
    {
        return ContainsAny(normalized, "long walk", "long walks", "walk in", "good places to walk");
    }

    internal static bool IsFinancePlanningQuestion(string value)
    {
        return ContainsAny(
            value,
            "save for",
            "save up",
            "can i afford",
            "how can i afford",
            "budget for",
            "cut my expenses",
            "reduce my spending",
            "what can i cut",
            "how much can i cut",
            "monthly budget",
            "spending plan",
            "afford to buy");
    }

    internal static bool IsExplicitVendorLookup(string value)
    {
        var hasStrongVendorPhrase = ContainsAny(
            value,
            "where can i buy",
            "where can i get",
            "places that sell",
            "shops that sell",
            "stores that sell");
        if (hasStrongVendorPhrase)
        {
            return true;
        }

        var hasDiscoveryVerb = ContainsAny(
            value,
            "find ",
            "show me",
            "suggest",
            "recommend",
            "where should i go",
            "places to go",
            "places to visit");
        var hasPlaceNounOrSignal = ContainsAny(
            value,
            "place",
            "places",
            "shop",
            "shops",
            "store",
            "stores",
            "restaurant",
            "cafe",
            "pub",
            "bar",
            "cinema",
            "museum",
            "park",
            "pharmacy",
            "petrol",
            "near me",
            "nearby",
            "around here");

        return hasDiscoveryVerb && hasPlaceNounOrSignal;
    }

    private static bool IsCommerceDiscoveryPrompt(
        string normalized,
        IReadOnlyCollection<RealWorldDiscoveryDomain> domains)
    {
        if (ContainsAny(
                normalized,
                "buy ",
                "purchase ",
                "places that sell",
                "shops that sell",
                "stores that sell",
                "shop for"))
        {
            return true;
        }

        if (ContainsAny(normalized, "xbox", "ps5", "playstation", "laptop", "electronics", "red bull"))
        {
            return true;
        }

        return domains.Contains(RealWorldDiscoveryDomain.ElectronicsRetail)
               || domains.Contains(RealWorldDiscoveryDomain.CommerceGeneral)
               || domains.Contains(RealWorldDiscoveryDomain.ShoppingGeneral);
    }

    internal static string NormalizeConceptToken(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        normalized = normalized.Replace(' ', '_');
        normalized = Regex.Replace(normalized, @"[^a-z0-9_]", string.Empty);
        return normalized;
    }

    internal static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim().ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"[^\p{L}\p{N}\s'\-]", " ");
        return Regex.Replace(cleaned, "\\s+", " ").Trim();
    }
}

public sealed class RealWorldFinancialGuardrailPolicy : IRealWorldFinancialGuardrailPolicy
{
    public bool ShouldForceFinancialGuidance(string userMessage, out string reasonCode)
    {
        var normalized = RealWorldDeterministicFallbackBuilder.Normalize(userMessage);
        var financialPlanning = RealWorldDeterministicFallbackBuilder.IsFinancePlanningQuestion(normalized);
        var explicitVendorLookup = RealWorldDeterministicFallbackBuilder.IsExplicitVendorLookup(normalized);
        if (financialPlanning && !explicitVendorLookup)
        {
            reasonCode = "real_world_financial_guardrail_enforced";
            return true;
        }

        reasonCode = "real_world_financial_guardrail_not_triggered";
        return false;
    }
}

public sealed class RealWorldConceptNormalizationPolicy : IRealWorldConceptNormalizationPolicy
{
    private static readonly IReadOnlyDictionary<string, string> AliasToCanonical =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["movie_theatre"] = "movie_theater",
            ["cinema"] = "movie_theater",
            ["cinemas"] = "movie_theater",
            ["film"] = "movie_theater",
            ["films"] = "movie_theater",
            ["watch_movie_place"] = "movie_theater",
            ["watch_film_place"] = "movie_theater",
            ["coffee_shop"] = "cafe",
            ["coffee_shops"] = "cafe",
            ["cafes"] = "cafe",
            ["pub"] = "pub_bar",
            ["bar"] = "pub_bar",
            ["bars"] = "pub_bar",
            ["night_out"] = "nightlife_general",
            ["petrol"] = "petrol_station",
            ["gas_station"] = "petrol_station",
            ["fuel_station"] = "petrol_station",
            ["electronics_shop"] = "electronics_retail",
            ["electronics_store"] = "electronics_retail",
            ["stores"] = "commerce_general",
            ["shops"] = "commerce_general",
            ["service"] = "service_general",
            ["services"] = "service_general"
        };

    private static readonly HashSet<string> CanonicalConcepts =
    [
        "cafe",
        "restaurant",
        "takeaway",
        "pub_bar",
        "movie_theater",
        "park_walk",
        "playground",
        "pharmacy",
        "petrol_station",
        "gym",
        "electronics_retail",
        "convenience_store",
        "grocery",
        "shopping_general",
        "outdoor_activity",
        "entertainment_general",
        "nightlife_general",
        "food_drink_general",
        "service_general",
        "commerce_general",
        "exploratory_evening_activity",
        "exploratory_family_activity"
    ];

    public RealWorldConceptNormalizationResult Normalize(
        IReadOnlyList<string> candidateConcepts,
        IReadOnlyList<RealWorldDiscoveryDomain> candidateDomains)
    {
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal);
        var normalizedConcepts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var domain in candidateDomains)
        {
            normalizedConcepts.Add(ToCanonicalConcept(domain));
        }

        foreach (var rawConcept in candidateConcepts)
        {
            if (string.IsNullOrWhiteSpace(rawConcept))
            {
                continue;
            }

            var normalizedToken = RealWorldDeterministicFallbackBuilder.NormalizeConceptToken(rawConcept);
            if (TryResolveCanonical(normalizedToken, out var canonical, out var normalizationReasonCode))
            {
                normalizedConcepts.Add(canonical!);
                if (!string.IsNullOrWhiteSpace(normalizationReasonCode))
                {
                    reasonCodes.Add(normalizationReasonCode!);
                }
            }
            else
            {
                reasonCodes.Add("real_world_interpreter_concept_flattened_to_generic");
                reasonCodes.Add(RealWorldInterpreterFallbackReasonCodes.UnknownVocabulary);
                if (normalizedToken.Contains("service", StringComparison.Ordinal))
                {
                    normalizedConcepts.Add("service_general");
                }
                else if (normalizedToken.Contains("shop", StringComparison.Ordinal)
                         || normalizedToken.Contains("buy", StringComparison.Ordinal))
                {
                    normalizedConcepts.Add("commerce_general");
                }
                else if (normalizedToken.Contains("fun", StringComparison.Ordinal)
                         || normalizedToken.Contains("activity", StringComparison.Ordinal))
                {
                    normalizedConcepts.Add("entertainment_general");
                }
            }
        }

        var mappedDomains = normalizedConcepts
            .Select(ToDomain)
            .Where(static domain => domain.HasValue)
            .Select(static domain => domain!.Value)
            .Concat(candidateDomains)
            .Distinct()
            .Take(8)
            .ToArray();
        return new RealWorldConceptNormalizationResult(
            NormalizedConcepts: normalizedConcepts.Take(10).ToArray(),
            MappedDomains: mappedDomains,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static bool TryResolveCanonical(
        string normalizedToken,
        out string? canonical,
        out string? normalizationReasonCode)
    {
        if (CanonicalConcepts.Contains(normalizedToken))
        {
            canonical = normalizedToken;
            normalizationReasonCode = null;
            return true;
        }

        if (AliasToCanonical.TryGetValue(normalizedToken, out var aliasMapped))
        {
            canonical = aliasMapped;
            normalizationReasonCode = "real_world_interpreter_concept_normalized";
            return true;
        }

        var compact = normalizedToken.Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var concept in CanonicalConcepts)
        {
            var conceptCompact = concept.Replace("_", string.Empty, StringComparison.Ordinal);
            if (compact.Equals(conceptCompact, StringComparison.Ordinal))
            {
                canonical = concept;
                normalizationReasonCode = "real_world_interpreter_concept_normalized";
                return true;
            }
        }

        if (compact.Contains("cinema", StringComparison.Ordinal)
            || compact.Contains("movie", StringComparison.Ordinal)
            || compact.Contains("film", StringComparison.Ordinal))
        {
            canonical = "movie_theater";
            normalizationReasonCode = "real_world_interpreter_concept_normalized";
            return true;
        }

        if (compact.Contains("cafe", StringComparison.Ordinal)
            || compact.Contains("coffee", StringComparison.Ordinal))
        {
            canonical = "cafe";
            normalizationReasonCode = "real_world_interpreter_concept_normalized";
            return true;
        }

        if (compact.Contains("pub", StringComparison.Ordinal)
            || compact.Contains("bar", StringComparison.Ordinal))
        {
            canonical = "pub_bar";
            normalizationReasonCode = "real_world_interpreter_concept_normalized";
            return true;
        }

        canonical = null;
        normalizationReasonCode = null;
        return false;
    }

    private static string ToCanonicalConcept(RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.Cafe => "cafe",
            RealWorldDiscoveryDomain.Restaurant => "restaurant",
            RealWorldDiscoveryDomain.Takeaway => "takeaway",
            RealWorldDiscoveryDomain.PubBar => "pub_bar",
            RealWorldDiscoveryDomain.MovieTheater => "movie_theater",
            RealWorldDiscoveryDomain.ParkWalk => "park_walk",
            RealWorldDiscoveryDomain.Playground => "playground",
            RealWorldDiscoveryDomain.Pharmacy => "pharmacy",
            RealWorldDiscoveryDomain.PetrolStation => "petrol_station",
            RealWorldDiscoveryDomain.Gym => "gym",
            RealWorldDiscoveryDomain.ElectronicsRetail => "electronics_retail",
            RealWorldDiscoveryDomain.ConvenienceStore => "convenience_store",
            RealWorldDiscoveryDomain.Grocery => "grocery",
            RealWorldDiscoveryDomain.ShoppingGeneral => "shopping_general",
            RealWorldDiscoveryDomain.OutdoorActivity => "outdoor_activity",
            RealWorldDiscoveryDomain.EntertainmentGeneral => "entertainment_general",
            RealWorldDiscoveryDomain.NightlifeGeneral => "nightlife_general",
            RealWorldDiscoveryDomain.FoodDrinkGeneral => "food_drink_general",
            RealWorldDiscoveryDomain.ServiceGeneral => "service_general",
            RealWorldDiscoveryDomain.CommerceGeneral => "commerce_general",
            RealWorldDiscoveryDomain.ExploratoryEveningActivity => "exploratory_evening_activity",
            RealWorldDiscoveryDomain.ExploratoryFamilyActivity => "exploratory_family_activity",
            _ => "entertainment_general"
        };
    }

    private static RealWorldDiscoveryDomain? ToDomain(string canonicalConcept)
    {
        return canonicalConcept switch
        {
            "cafe" => RealWorldDiscoveryDomain.Cafe,
            "restaurant" => RealWorldDiscoveryDomain.Restaurant,
            "takeaway" => RealWorldDiscoveryDomain.Takeaway,
            "pub_bar" => RealWorldDiscoveryDomain.PubBar,
            "movie_theater" => RealWorldDiscoveryDomain.MovieTheater,
            "park_walk" => RealWorldDiscoveryDomain.ParkWalk,
            "playground" => RealWorldDiscoveryDomain.Playground,
            "pharmacy" => RealWorldDiscoveryDomain.Pharmacy,
            "petrol_station" => RealWorldDiscoveryDomain.PetrolStation,
            "gym" => RealWorldDiscoveryDomain.Gym,
            "electronics_retail" => RealWorldDiscoveryDomain.ElectronicsRetail,
            "convenience_store" => RealWorldDiscoveryDomain.ConvenienceStore,
            "grocery" => RealWorldDiscoveryDomain.Grocery,
            "shopping_general" => RealWorldDiscoveryDomain.ShoppingGeneral,
            "outdoor_activity" => RealWorldDiscoveryDomain.OutdoorActivity,
            "entertainment_general" => RealWorldDiscoveryDomain.EntertainmentGeneral,
            "nightlife_general" => RealWorldDiscoveryDomain.NightlifeGeneral,
            "food_drink_general" => RealWorldDiscoveryDomain.FoodDrinkGeneral,
            "service_general" => RealWorldDiscoveryDomain.ServiceGeneral,
            "commerce_general" => RealWorldDiscoveryDomain.CommerceGeneral,
            "exploratory_evening_activity" => RealWorldDiscoveryDomain.ExploratoryEveningActivity,
            "exploratory_family_activity" => RealWorldDiscoveryDomain.ExploratoryFamilyActivity,
            _ => null
        };
    }
}

public sealed class RealWorldInterpretationValidationPolicy(
    IRealWorldFinancialGuardrailPolicy financialGuardrailPolicy,
    IRealWorldConceptNormalizationPolicy conceptNormalizationPolicy) : IRealWorldInterpretationValidationPolicy
{
    private const double LowConfidenceThreshold = 0.46d;

    public RealWorldInterpretationValidationPolicy(
        IRealWorldFinancialGuardrailPolicy financialGuardrailPolicy)
        : this(financialGuardrailPolicy, new RealWorldConceptNormalizationPolicy())
    {
    }

    public RealWorldIntentInterpretation ValidateAndNormalize(
        string userMessage,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        RealWorldIntentInterpretation aiInterpretation,
        RealWorldIntentInterpretation deterministicFallback)
    {
        ArgumentNullException.ThrowIfNull(aiInterpretation);
        ArgumentNullException.ThrowIfNull(deterministicFallback);

        var reasonCodes = new HashSet<string>(aiInterpretation.ReasonCodes, StringComparer.Ordinal)
        {
            "real_world_interpreter_ai_primary_used"
        };
        var warnings = new HashSet<string>(aiInterpretation.Warnings, StringComparer.Ordinal);
        var overrideApplied = false;
        var hasNearMeLanguage = aiInterpretation.HasNearMeLanguage || localDiscovery.HasNearMeLanguage;
        var hasExplicitLocality = aiInterpretation.HasExplicitLocality
                                  || localDiscovery.HasExplicitLocality
                                  || grounding.HasTypedArea
                                  || !string.IsNullOrWhiteSpace(grounding.LocalityLabel);
        var normalization = conceptNormalizationPolicy.Normalize(
            aiInterpretation.CandidateConcepts ?? [],
            aiInterpretation.CandidateDomains);
        reasonCodes.UnionWith(normalization.ReasonCodes);
        var candidateConcepts = normalization.NormalizedConcepts.ToList();
        var domains = normalization.MappedDomains.ToList();
        if (domains.Count == 0 && aiInterpretation.PlacesApplicable)
        {
            domains = deterministicFallback.CandidateDomains
                .Distinct()
                .Take(4)
                .ToList();
            if (domains.Count == 0)
            {
                domains.Add(RealWorldDiscoveryDomain.EntertainmentGeneral);
            }

            reasonCodes.Add("real_world_interpreter_domains_backfilled_from_fallback");
            reasonCodes.Add(RealWorldInterpreterFallbackReasonCodes.ValidationInconsistent);
            overrideApplied = true;
        }

        if (candidateConcepts.Count == 0 && domains.Count > 0)
        {
            candidateConcepts = domains
                .Select(ToCanonicalConcept)
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToList();
        }

        if (financialGuardrailPolicy.ShouldForceFinancialGuidance(userMessage, out var guardrailReason))
        {
            reasonCodes.Add(guardrailReason);
            reasonCodes.Add("real_world_interpreter_validation_override_applied");
            warnings.Add("real_world_interpreter_financial_override");
            return deterministicFallback with
            {
                IntentFamily = RealWorldIntentFamily.FinancialGuidance,
                RecommendedExecutionMode = RealWorldExecutionMode.FinancialGuidanceOnly,
                PlacesApplicable = false,
                FinancialRelated = true,
                RequiresLocation = false,
                Exploratory = false,
                ClarificationNeeded = false,
                CandidateDomains = [],
                CandidateConcepts = [],
                ClarificationPrompt = null,
                Confidence = Math.Max(deterministicFallback.Confidence, aiInterpretation.Confidence),
                ReasonCodes = reasonCodes.ToArray(),
                Warnings = warnings.ToArray(),
                InterpretationSource = RealWorldInterpretationSource.AiPrimary
            };
        }

        var normalizedIntentFamily = aiInterpretation.IntentFamily;
        var normalizedMode = aiInterpretation.RecommendedExecutionMode;
        var placesApplicable = aiInterpretation.PlacesApplicable;
        var financialRelated = aiInterpretation.FinancialRelated;
        var exploratory = aiInterpretation.Exploratory
                          || normalizedMode == RealWorldExecutionMode.ExploratoryMultiDomainSearch
                          || normalizedIntentFamily == RealWorldIntentFamily.ExploratoryAssistance;
        var clarificationNeeded = aiInterpretation.ClarificationNeeded;
        var clarificationPrompt = aiInterpretation.ClarificationPrompt;
        var confidence = Math.Round(Math.Clamp(aiInterpretation.Confidence, 0d, 0.98d), 4);

        if (normalizedIntentFamily == RealWorldIntentFamily.FinancialGuidance
            || normalizedMode == RealWorldExecutionMode.FinancialGuidanceOnly)
        {
            normalizedIntentFamily = RealWorldIntentFamily.FinancialGuidance;
            normalizedMode = RealWorldExecutionMode.FinancialGuidanceOnly;
            placesApplicable = false;
            financialRelated = true;
            exploratory = false;
            clarificationNeeded = false;
            clarificationPrompt = null;
            domains.Clear();
            candidateConcepts.Clear();
            reasonCodes.Add("real_world_interpreter_financial_normalized");
            overrideApplied = true;
        }
        else
        {
            if (normalizedMode is RealWorldExecutionMode.FocusedPlaceSearch
                or RealWorldExecutionMode.FocusedThemeSearch
                or RealWorldExecutionMode.ExploratoryMultiDomainSearch)
            {
                placesApplicable = true;
            }

            if (exploratory
                && normalizedMode != RealWorldExecutionMode.ExploratoryMultiDomainSearch
                && confidence >= 0.60d)
            {
                normalizedMode = RealWorldExecutionMode.ExploratoryMultiDomainSearch;
                reasonCodes.Add("real_world_exploratory_mode_selected");
                overrideApplied = true;
            }

            if (clarificationNeeded && normalizedMode != RealWorldExecutionMode.ClarifyLight)
            {
                normalizedMode = RealWorldExecutionMode.ClarifyLight;
                reasonCodes.Add("real_world_interpreter_clarify_mode_enforced");
                overrideApplied = true;
            }

            if (normalizedMode == RealWorldExecutionMode.ClarifyLight
                && string.IsNullOrWhiteSpace(clarificationPrompt))
            {
                clarificationPrompt = "Do you want nearby places, financial guidance, or a specific area to search?";
                reasonCodes.Add("real_world_interpreter_clarification_prompt_defaulted");
                overrideApplied = true;
            }

            if (confidence < LowConfidenceThreshold)
            {
                normalizedMode = RealWorldExecutionMode.ClarifyLight;
                clarificationNeeded = true;
                placesApplicable = false;
                financialRelated = false;
                clarificationPrompt ??=
                    "I can help with nearby places or financial guidance. Tell me which one you want.";
                reasonCodes.Add("real_world_interpreter_low_confidence_clarify");
                reasonCodes.Add(RealWorldInterpreterFallbackReasonCodes.LowConfidence);
                warnings.Add("real_world_interpreter_low_confidence");
                overrideApplied = true;
            }
        }

        var requiresLocation = placesApplicable && (hasNearMeLanguage || !hasExplicitLocality);
        if (overrideApplied)
        {
            reasonCodes.Add("real_world_interpreter_validation_override_applied");
            warnings.Add("real_world_interpreter_validation_override_applied");
        }

        return aiInterpretation with
        {
            IntentFamily = normalizedIntentFamily,
            RecommendedExecutionMode = normalizedMode,
            PlacesApplicable = placesApplicable,
            FinancialRelated = financialRelated,
            RequiresLocation = requiresLocation,
            Exploratory = exploratory,
            ClarificationNeeded = clarificationNeeded,
            HasNearMeLanguage = hasNearMeLanguage,
            HasExplicitLocality = hasExplicitLocality,
            Confidence = confidence,
            CandidateDomains = domains,
            CandidateConcepts = candidateConcepts,
            ClarificationPrompt = clarificationPrompt,
            ReasonCodes = reasonCodes.ToArray(),
            Warnings = warnings.ToArray(),
            InterpretationSource = RealWorldInterpretationSource.AiPrimary
        };
    }

    private static string ToCanonicalConcept(RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.Cafe => "cafe",
            RealWorldDiscoveryDomain.Restaurant => "restaurant",
            RealWorldDiscoveryDomain.Takeaway => "takeaway",
            RealWorldDiscoveryDomain.PubBar => "pub_bar",
            RealWorldDiscoveryDomain.MovieTheater => "movie_theater",
            RealWorldDiscoveryDomain.ParkWalk => "park_walk",
            RealWorldDiscoveryDomain.Playground => "playground",
            RealWorldDiscoveryDomain.Pharmacy => "pharmacy",
            RealWorldDiscoveryDomain.PetrolStation => "petrol_station",
            RealWorldDiscoveryDomain.Gym => "gym",
            RealWorldDiscoveryDomain.ElectronicsRetail => "electronics_retail",
            RealWorldDiscoveryDomain.ConvenienceStore => "convenience_store",
            RealWorldDiscoveryDomain.Grocery => "grocery",
            RealWorldDiscoveryDomain.ShoppingGeneral => "shopping_general",
            RealWorldDiscoveryDomain.OutdoorActivity => "outdoor_activity",
            RealWorldDiscoveryDomain.EntertainmentGeneral => "entertainment_general",
            RealWorldDiscoveryDomain.NightlifeGeneral => "nightlife_general",
            RealWorldDiscoveryDomain.FoodDrinkGeneral => "food_drink_general",
            RealWorldDiscoveryDomain.ServiceGeneral => "service_general",
            RealWorldDiscoveryDomain.CommerceGeneral => "commerce_general",
            RealWorldDiscoveryDomain.ExploratoryEveningActivity => "exploratory_evening_activity",
            RealWorldDiscoveryDomain.ExploratoryFamilyActivity => "exploratory_family_activity",
            _ => "place"
        };
    }
}
