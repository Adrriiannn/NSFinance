using System.Text.Json;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed record RealWorldInterpreterPromptInput(
    string UserMessage,
    CompanionLocationGrounding Grounding,
    LocalDiscoveryConstraintExtractionResult LocalDiscovery,
    RealWorldIntentInterpretation DeterministicInterpretation);

public interface IRealWorldIntentInterpreterPromptBuilder
{
    string BuildSystemInstructions();

    string BuildUserPrompt(RealWorldInterpreterPromptInput input);
}

public sealed class RealWorldIntentInterpreterPromptBuilder : IRealWorldIntentInterpreterPromptBuilder
{
    public string BuildSystemInstructions()
    {
        return
            """
            You are an intent interpreter for a bounded real-world assistance planner.
            Return STRICT JSON only.
            Do not include prose outside JSON.
            You must choose only from allowed enums.
            You must not invent external facts.
            If user goal is financial planning/advice, do not force place discovery.
            """;
    }

    public string BuildUserPrompt(RealWorldInterpreterPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var payload = new
        {
            userMessage = Sanitize(input.UserMessage, 420),
            grounding = new
            {
                source = input.Grounding.Source,
                hasCoordinates = input.Grounding.HasCoordinates,
                hasTypedArea = input.Grounding.HasTypedArea,
                typedArea = Sanitize(input.Grounding.TypedArea, 80),
                localityLabel = Sanitize(input.Grounding.LocalityLabel, 80)
            },
            localDiscovery = new
            {
                input.LocalDiscovery.IsLocalDiscoveryCandidate,
                input.LocalDiscovery.Confidence,
                input.LocalDiscovery.HasNearMeLanguage,
                input.LocalDiscovery.HasExplicitLocality,
                localityHint = Sanitize(input.LocalDiscovery.LocalityHint, 80),
                placeTypeHints = input.LocalDiscovery.PlaceTypeHints,
                audienceHints = input.LocalDiscovery.AudienceHints,
                timeHints = input.LocalDiscovery.TimeHints,
                preferenceHints = input.LocalDiscovery.PreferenceHints,
                reasonCodes = input.LocalDiscovery.ReasonCodes
            },
            deterministicSeed = new
            {
                intentFamily = input.DeterministicInterpretation.IntentFamily.ToString(),
                executionMode = input.DeterministicInterpretation.RecommendedExecutionMode.ToString(),
                input.DeterministicInterpretation.PlacesApplicable,
                input.DeterministicInterpretation.FinancialRelated,
                input.DeterministicInterpretation.RequiresLocation,
                input.DeterministicInterpretation.Exploratory,
                input.DeterministicInterpretation.ClarificationNeeded,
                input.DeterministicInterpretation.Confidence,
                candidateDomains = input.DeterministicInterpretation.CandidateDomains
                    .Select(x => x.ToString())
                    .ToArray(),
                reasonCodes = input.DeterministicInterpretation.ReasonCodes
            }
        };

        return
            $$"""
              Interpret this request for execution planning.

              Input JSON:
              {{JsonSerializer.Serialize(payload)}}

              Allowed intentFamily values:
              - FinancialGuidance
              - PlaceDiscovery
              - CommerceDiscovery
              - ServiceDiscovery
              - ExploratoryAssistance
              - MixedAssistance
              - Ambiguous

              Allowed executionMode values:
              - FocusedPlaceSearch
              - FocusedThemeSearch
              - ExploratoryMultiDomainSearch
              - FinancialGuidanceOnly
              - ClarifyLight
              - MissingLocationGuard
              - ProviderFailureFallback

              Allowed candidateDomains values:
              - Cafe
              - Restaurant
              - Takeaway
              - PubBar
              - MovieTheater
              - ParkWalk
              - Playground
              - Pharmacy
              - PetrolStation
              - Gym
              - ElectronicsRetail
              - ConvenienceStore
              - Grocery
              - ShoppingGeneral
              - OutdoorActivity
              - EntertainmentGeneral
              - NightlifeGeneral
              - FoodDrinkGeneral
              - ServiceGeneral
              - CommerceGeneral
              - ExploratoryEveningActivity
              - ExploratoryFamilyActivity

              Return strict JSON with exactly:
              {
                "intentFamily": "...",
                "executionMode": "...",
                "placesApplicable": true,
                "financialRelated": false,
                "requiresLocation": true,
                "exploratory": false,
                "clarificationNeeded": false,
                "confidence": 0.0,
                "candidateDomains": ["..."],
                "clarificationPrompt": "string|null",
                "reasonCodes": ["..."]
              }
              """;
    }

    private static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value.Trim(), "\\s+", " ");
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd();
    }
}

public sealed class RealWorldIntentInterpreter(
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IRealWorldIntentInterpreterPromptBuilder promptBuilder,
    ILogger<RealWorldIntentInterpreter> logger) : IRealWorldIntentInterpreter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<RealWorldIntentInterpretation> InterpretAsync(
        UserChatRequest request,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var deterministic = BuildDeterministicInterpretation(request.UserMessage, localDiscovery);
        var warnings = new HashSet<string>(deterministic.Warnings, StringComparer.Ordinal);
        var aiInterpretation = await TryInterpretWithAiAsync(
            request,
            grounding,
            localDiscovery,
            deterministic,
            cancellationToken);

        if (aiInterpretation is null)
        {
            warnings.Add("real_world_interpreter_ai_unavailable_or_invalid");
            return deterministic with
            {
                Warnings = warnings.ToArray()
            };
        }

        var merged = MergeDeterministicAndAi(
            request.UserMessage,
            deterministic,
            aiInterpretation,
            localDiscovery);
        warnings.UnionWith(merged.Warnings);
        return merged with
        {
            Warnings = warnings.ToArray()
        };
    }

    private async Task<RealWorldIntentInterpretation?> TryInterpretWithAiAsync(
        UserChatRequest request,
        CompanionLocationGrounding grounding,
        LocalDiscoveryConstraintExtractionResult localDiscovery,
        RealWorldIntentInterpretation deterministic,
        CancellationToken cancellationToken)
    {
        try
        {
            var route = modelRouter.Resolve(
                AITaskType.UserChatSimple,
                AIModelClass.Fast,
                complexityHint: "real_world_intent_interpretation");
            var promptInput = new RealWorldInterpreterPromptInput(
                request.UserMessage,
                grounding,
                localDiscovery,
                deterministic);
            var aiRequest = AIRequest.Create(
                taskType: AITaskType.UserChatSimple,
                preferredModelClass: route.ModelClass,
                messages:
                [
                    AIMessage.User(promptBuilder.BuildUserPrompt(promptInput))
                ],
                correlationId: request.CorrelationId,
                systemInstructions: promptBuilder.BuildSystemInstructions(),
                structuredOutputSchemaName: "real_world_intent_interpretation_v1",
                temperature: 0.1d,
                maxOutputTokens: 280,
                metadata: request.Metadata);

            var response = await aiClient.SendAsync(aiRequest, route, cancellationToken);
            if (!response.Succeeded)
            {
                logger.LogInformation(
                    "Real-world interpreter AI call failed correlationId={CorrelationId} reason={Reason}",
                    request.CorrelationId,
                    response.FailureReason ?? "unknown");
                return null;
            }

            var payload = response.StructuredPayloadJson ?? response.Content;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var parsed = JsonSerializer.Deserialize<RealWorldInterpreterAiResponse>(payload, JsonOptions);
            return NormalizeAiResponse(parsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Real-world interpreter AI parsing failed correlationId={CorrelationId}",
                request.CorrelationId);
            return null;
        }
    }

    private static RealWorldIntentInterpretation MergeDeterministicAndAi(
        string userMessage,
        RealWorldIntentInterpretation deterministic,
        RealWorldIntentInterpretation ai,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var reasonCodes = new HashSet<string>(deterministic.ReasonCodes, StringComparer.Ordinal);
        reasonCodes.UnionWith(ai.ReasonCodes);
        reasonCodes.Add("real_world_interpreter_ai_merge_applied");

        var warnings = new HashSet<string>(deterministic.Warnings, StringComparer.Ordinal);
        warnings.UnionWith(ai.Warnings);

        var financeGuardrail = IsFinancePlanningQuestion(userMessage) && !IsExplicitVendorLookup(userMessage);
        if (financeGuardrail)
        {
            reasonCodes.Add("real_world_financial_guardrail_enforced");
            return deterministic with
            {
                IntentFamily = RealWorldIntentFamily.FinancialGuidance,
                RecommendedExecutionMode = RealWorldExecutionMode.FinancialGuidanceOnly,
                PlacesApplicable = false,
                FinancialRelated = true,
                CandidateDomains = [],
                ReasonCodes = reasonCodes.ToArray(),
                Warnings = warnings.ToArray()
            };
        }

        var shouldUseAiInterpretation = ai.Confidence >= Math.Max(0.55d, deterministic.Confidence + 0.1d);
        var mergedDomains = deterministic.CandidateDomains
            .Concat(ai.CandidateDomains)
            .Distinct()
            .Take(6)
            .ToArray();

        if (!shouldUseAiInterpretation)
        {
            return deterministic with
            {
                CandidateDomains = mergedDomains,
                ReasonCodes = reasonCodes.ToArray(),
                Warnings = warnings.ToArray()
            };
        }

        var resolved = ai with
        {
            CandidateDomains = mergedDomains,
            HasNearMeLanguage = deterministic.HasNearMeLanguage || ai.HasNearMeLanguage,
            HasExplicitLocality = deterministic.HasExplicitLocality || localDiscovery.HasExplicitLocality,
            Confidence = Math.Round(Math.Clamp(Math.Max(ai.Confidence, deterministic.Confidence), 0d, 0.98d), 4),
            ReasonCodes = reasonCodes.ToArray(),
            Warnings = warnings.ToArray()
        };

        if (resolved.ClarificationNeeded && resolved.RecommendedExecutionMode != RealWorldExecutionMode.ClarifyLight)
        {
            resolved = resolved with
            {
                RecommendedExecutionMode = RealWorldExecutionMode.ClarifyLight,
                ReasonCodes = resolved.ReasonCodes
                    .Concat(["real_world_interpreter_clarify_mode_enforced"])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        return resolved;
    }

    private static RealWorldIntentInterpretation BuildDeterministicInterpretation(
        string userMessage,
        LocalDiscoveryConstraintExtractionResult localDiscovery)
    {
        var normalized = Normalize(userMessage);
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var domains = ExtractDomains(normalized);

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
            reasonCodes.Add("real_world_financial_guardrail_triggered");
            domains.Clear();
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
            Warnings: warnings.ToArray());
    }

    private static RealWorldIntentInterpretation? NormalizeAiResponse(RealWorldInterpreterAiResponse? ai)
    {
        if (ai is null)
        {
            return null;
        }

        if (!TryParseIntentFamily(ai.IntentFamily, out var family)
            || !TryParseExecutionMode(ai.ExecutionMode, out var mode))
        {
            return null;
        }

        var domains = (ai.CandidateDomains ?? [])
            .Select(TryParseDomain)
            .Where(domain => domain.HasValue)
            .Select(domain => domain!.Value)
            .Distinct()
            .Take(6)
            .ToArray();
        var confidence = Math.Round(Math.Clamp(ai.Confidence ?? 0.5d, 0d, 0.98d), 4);

        return new RealWorldIntentInterpretation(
            IntentFamily: family,
            RecommendedExecutionMode: mode,
            PlacesApplicable: ai.PlacesApplicable ?? family is not RealWorldIntentFamily.FinancialGuidance,
            FinancialRelated: ai.FinancialRelated ?? family == RealWorldIntentFamily.FinancialGuidance,
            RequiresLocation: ai.RequiresLocation ?? false,
            Exploratory: ai.Exploratory ?? mode == RealWorldExecutionMode.ExploratoryMultiDomainSearch,
            ClarificationNeeded: ai.ClarificationNeeded ?? mode == RealWorldExecutionMode.ClarifyLight,
            HasNearMeLanguage: false,
            HasExplicitLocality: false,
            Confidence: confidence,
            CandidateDomains: domains,
            ClarificationPrompt: ai.ClarificationPrompt,
            ReasonCodes: (ai.ReasonCodes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray(),
            Warnings: []);
    }

    private static bool TryParseIntentFamily(string? value, out RealWorldIntentFamily family)
    {
        return Enum.TryParse(value, true, out family);
    }

    private static bool TryParseExecutionMode(string? value, out RealWorldExecutionMode mode)
    {
        return Enum.TryParse(value, true, out mode);
    }

    private static RealWorldDiscoveryDomain? TryParseDomain(string? value)
    {
        if (Enum.TryParse<RealWorldDiscoveryDomain>(value, true, out var parsed))
        {
            return parsed;
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "movie_theater" => RealWorldDiscoveryDomain.MovieTheater,
            "pub_bar" => RealWorldDiscoveryDomain.PubBar,
            "petrol_station" => RealWorldDiscoveryDomain.PetrolStation,
            "food_drink_general" => RealWorldDiscoveryDomain.FoodDrinkGeneral,
            "service_general" => RealWorldDiscoveryDomain.ServiceGeneral,
            "commerce_general" => RealWorldDiscoveryDomain.CommerceGeneral,
            _ => null
        };
    }

    private static List<RealWorldDiscoveryDomain> ExtractDomains(string normalized)
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
        }

        if (ContainsAny(normalized, "restaurant", "restaurants", "dine", "dining", "eat"))
        {
            Add(RealWorldDiscoveryDomain.Restaurant);
        }

        if (ContainsAny(normalized, "takeaway", "takeaways", "take out", "delivery"))
        {
            Add(RealWorldDiscoveryDomain.Takeaway);
        }

        if (ContainsAny(normalized, "pub", "pubs", "bar", "bars", "drinks", "drinking"))
        {
            Add(RealWorldDiscoveryDomain.PubBar);
        }

        if (ContainsAny(normalized, "cinema", "cinemas", "movie", "movies", "theater", "theatre"))
        {
            Add(RealWorldDiscoveryDomain.MovieTheater);
        }

        if (ContainsAny(normalized, "park", "parks", "walk", "walking", "hike", "long walks"))
        {
            Add(RealWorldDiscoveryDomain.ParkWalk);
            Add(RealWorldDiscoveryDomain.OutdoorActivity);
        }

        if (ContainsAny(normalized, "playground", "playgrounds"))
        {
            Add(RealWorldDiscoveryDomain.Playground);
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

    private static bool IsServicePrompt(string normalized)
    {
        return ContainsAny(normalized, "pharmacy", "chemist", "petrol", "fuel", "gas station", "gym", "fitness");
    }

    private static bool IsExploratoryPrompt(string normalized)
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

    private static bool IsFoodDrinkTheme(string normalized)
    {
        return ContainsAny(normalized, "what should i eat", "something to eat", "something to drink", "food", "drink");
    }

    private static bool IsOutdoorTheme(string normalized)
    {
        return ContainsAny(normalized, "long walk", "long walks", "walk in", "good places to walk");
    }

    private static bool IsFinancePlanningQuestion(string value)
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

    private static bool IsExplicitVendorLookup(string value)
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

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim().ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"[^\p{L}\p{N}\s'\-]", " ");
        return Regex.Replace(cleaned, "\\s+", " ").Trim();
    }

    private sealed record RealWorldInterpreterAiResponse(
        string? IntentFamily,
        string? ExecutionMode,
        bool? PlacesApplicable,
        bool? FinancialRelated,
        bool? RequiresLocation,
        bool? Exploratory,
        bool? ClarificationNeeded,
        double? Confidence,
        IReadOnlyList<string>? CandidateDomains,
        string? ClarificationPrompt,
        IReadOnlyList<string>? ReasonCodes);
}

