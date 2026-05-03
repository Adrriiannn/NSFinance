using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceGuardEvidenceService(
    IPlaceDetailsService placeDetailsService,
    ICompanionPlaceParkingEvidenceService parkingEvidenceService,
    ICompanionPlaceTypeFamilyClassifier typeFamilyClassifier,
    IOptions<AIIntegrationOptions> options,
    IChatTelemetry telemetry) : ICompanionPlaceGuardEvidenceService
{
    private static readonly IReadOnlyList<CompanionAmbiguityGuardDefinition> Catalogue =
    [
        new("bank_branch_vs_atm", ["bank_branch", "bank"], ["atm", "cash_machine"], ["bank", "financial_institution"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        new("atm_vs_bank_branch", ["atm"], ["bank_branch", "bank"], ["atm"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        new("post_office_vs_mailbox", ["post_office"], ["mailbox", "post_box", "parcel_locker"], ["post_office"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        new("hotel_vs_hotel_restaurant", ["hotel", "lodging"], ["restaurant", "bar"], ["hotel", "lodging"], ["types", "primaryType"], "soft_penalty_only"),
        new("car_park_vs_public_park", ["parking", "car_park"], ["park", "tourist_attraction"], ["parking", "parking_lot", "parking_garage"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        new("fine_dining_vs_fast_food", ["fine_dining", "upscale"], ["fast_food_restaurant", "meal_takeaway", "cafe"], ["restaurant", "fine_dining_restaurant"], ["types", "primaryType", "priceLevel"], "ranking_guard"),
        new("restaurant_delivery_vs_dine_in_only", ["delivery", "delivery_restaurant", "food_delivery"], ["dine_in_only", "takeaway_not_available"], ["restaurant", "meal_delivery", "meal_takeaway"], ["delivery", "takeout", "dineIn"], "enrich_before_reject"),
        new("takeaway_vs_dine_in_only", ["takeaway", "takeout"], ["dine_in_only", "takeout_false"], ["meal_takeaway", "restaurant"], ["takeout", "delivery", "dineIn"], "enrich_before_reject"),
        new("dog_friendly_policy", ["dog_friendly", "dogs_allowed"], ["dogs_not_allowed"], ["allows_dogs"], ["allowsDogs"], "enrich_before_reject"),
        new("wheelchair_accessibility", ["wheelchair", "wheelchair_accessible", "accessible"], ["not_accessible"], ["wheelchair_accessible"], ["accessibilityOptions"], "enrich_before_reject"),
        new("outdoor_seating", ["outdoor_seating", "outside_seating"], ["no_outdoor_seating"], ["outdoor_seating"], ["outdoorSeating"], "enrich_before_reject"),
        new("card_payments", ["card", "cards", "card_payments"], ["cash_only"], ["credit_cards", "debit_cards", "nfc"], ["paymentOptions"], "enrich_before_reject"),
        new("parking_availability", ["parking", "free_parking", "parking_available"], ["no_parking"], ["parking", "nearby_parking"], ["parkingOptions", "accessibilityOptions.wheelchairAccessibleParking", "nearbyParking"], "enrich_before_reject")
    ];

    public async Task<CompanionGuardEvaluationResult> EvaluateAsync(
        CompanionPlaceSearchStrategy strategy,
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(candidates);

        var guards = ResolveApplicableGuards(strategy, intent);
        if (guards.Count == 0 || candidates.Count == 0 || !options.Value.Architecture.PlacesGuardEvidenceEnabled)
        {
            return new CompanionGuardEvaluationResult(new Dictionary<string, IReadOnlyList<CompanionGuardEvidence>>(StringComparer.OrdinalIgnoreCase), [], []);
        }

        var maxCandidates = Math.Clamp(options.Value.Architecture.PlacesGuardEvidenceMaxCandidates <= 0 ? 20 : options.Value.Architecture.PlacesGuardEvidenceMaxCandidates, 1, 25);
        var candidatesToEvaluate = candidates.Take(maxCandidates).ToArray();
        await telemetry.TrackAsync(
            "places.guard_evidence.started",
            new Dictionary<string, object?>
            {
                ["appliedGuardIds"] = guards.Select(static guard => guard.GuardId).ToArray(),
                ["candidateCount"] = candidatesToEvaluate.Length,
                ["requestedCandidateCount"] = candidates.Count
            },
            cancellationToken);

        var requiresDetails = guards.Any(static guard => guard.DefaultAction == "enrich_before_reject" && guard.GuardId != "parking_availability");
        var detailsById = new Dictionary<string, PlaceDetailsResult?>(StringComparer.OrdinalIgnoreCase);
        if (requiresDetails)
        {
            await telemetry.TrackAsync(
                "places.guard_evidence.details_enrichment_requested",
                new Dictionary<string, object?>
                {
                    ["candidateCount"] = candidatesToEvaluate.Length,
                    ["guardIds"] = guards.Where(static guard => guard.DefaultAction == "enrich_before_reject").Select(static guard => guard.GuardId).ToArray()
                },
                cancellationToken);
            foreach (var candidate in candidatesToEvaluate)
            {
                detailsById[candidate.PlaceId] = await TryGetDetailsAsync(candidate.PlaceId, cancellationToken);
            }
        }

        CompanionParkingEvidenceResult? parking = null;
        if (guards.Any(static guard => guard.GuardId == "parking_availability"))
        {
            parking = await parkingEvidenceService.EvaluateAsync(intent, candidatesToEvaluate, cancellationToken);
        }

        var evidence = new Dictionary<string, IReadOnlyList<CompanionGuardEvidence>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidatesToEvaluate)
        {
            detailsById.TryGetValue(candidate.PlaceId, out var details);
            var items = guards
                .Select(guard => EvaluateGuard(guard, candidate, details, parking))
                .ToArray();
            evidence[candidate.PlaceId] = items;
        }

        var flat = evidence.Values.SelectMany(static item => item).ToArray();
        await telemetry.TrackAsync(
            "places.guard_evidence.status_counts",
            new Dictionary<string, object?>
            {
                ["confirmedMatchCount"] = flat.Count(static item => item.Status == CompanionGuardEvidenceStatus.ConfirmedMatch),
                ["likelyMatchCount"] = flat.Count(static item => item.Status == CompanionGuardEvidenceStatus.LikelyMatch),
                ["unknownCount"] = flat.Count(static item => item.Status == CompanionGuardEvidenceStatus.Unknown),
                ["likelyConflictCount"] = flat.Count(static item => item.Status == CompanionGuardEvidenceStatus.LikelyConflict),
                ["confirmedConflictCount"] = flat.Count(static item => item.Status == CompanionGuardEvidenceStatus.ConfirmedConflict)
            },
            cancellationToken);

        await telemetry.TrackAsync(
            "places.guard_evidence.completed",
            new Dictionary<string, object?>
            {
                ["appliedGuardIds"] = guards.Select(static guard => guard.GuardId).ToArray(),
                ["candidateCount"] = candidatesToEvaluate.Length,
                ["detailsEnrichedCount"] = detailsById.Count,
                ["evidenceCount"] = flat.Length
            },
            cancellationToken);

        return new CompanionGuardEvaluationResult(
            evidence,
            guards.Select(static guard => guard.GuardId).ToArray(),
            guards.Count == 0 ? [] : ["places_guard_evidence_evaluated"]);
    }

    private async Task<PlaceDetailsResult?> TryGetDetailsAsync(string placeId, CancellationToken cancellationToken)
    {
        try
        {
            var details = await placeDetailsService.GetDetailsAsync(placeId, cancellationToken);
            return string.IsNullOrWhiteSpace(details.PlaceId) ? null : details;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private CompanionGuardEvidence EvaluateGuard(
        CompanionAmbiguityGuardDefinition guard,
        CompanionPlacePoolCandidate candidate,
        PlaceDetailsResult? details,
        CompanionParkingEvidenceResult? parking)
    {
        return guard.GuardId switch
        {
            "bank_branch_vs_atm" => EvaluateTypeGuard(guard, candidate, required: ["bank", "financial_institution"], conflict: ["atm"]),
            "atm_vs_bank_branch" => EvaluateTypeGuard(guard, candidate, required: ["atm"], conflict: ["bank", "financial_institution"]),
            "post_office_vs_mailbox" => EvaluateTypeGuard(guard, candidate, required: ["post_office"], conflict: ["mailbox", "post_box", "parcel_locker"]),
            "hotel_vs_hotel_restaurant" => EvaluateTypeGuard(guard, candidate, required: ["hotel", "lodging"], conflict: ["restaurant", "bar"]),
            "car_park_vs_public_park" => EvaluateTypeGuard(guard, candidate, required: ["parking"], conflict: ["park", "tourist_attraction"]),
            "fine_dining_vs_fast_food" => EvaluateTypeGuard(guard, candidate, required: ["restaurant"], conflict: ["fast_food_restaurant", "meal_takeaway", "cafe"]),
            "restaurant_delivery_vs_dine_in_only" => EvaluateBoolGuard(guard, candidate.PlaceId, details, details?.Delivery, conflictWhen: details?.Delivery == false && details.DineIn == true && details.Takeout == false, "delivery", "delivery_false_dine_in_only"),
            "takeaway_vs_dine_in_only" => EvaluateBoolGuard(guard, candidate.PlaceId, details, details?.Takeout, conflictWhen: details?.Takeout == false && details.DineIn == true, "takeout", "takeout_false_dine_in_only"),
            "dog_friendly_policy" => EvaluateBoolGuard(guard, candidate.PlaceId, details, details?.AllowsDogs, conflictWhen: details?.AllowsDogs == false, "allowsDogs", "dogs_not_allowed"),
            "wheelchair_accessibility" => EvaluateAccessibilityGuard(guard, candidate.PlaceId, details),
            "outdoor_seating" => EvaluateBoolGuard(guard, candidate.PlaceId, details, details?.OutdoorSeating, conflictWhen: details?.OutdoorSeating == false, "outdoorSeating", "outdoor_seating_false"),
            "card_payments" => EvaluatePaymentGuard(guard, candidate.PlaceId, details),
            "parking_availability" => EvaluateParkingGuard(guard, candidate.PlaceId, parking),
            _ => Unknown(guard, candidate.PlaceId, requiresDetails: false, "guard_not_implemented")
        };
    }

    private CompanionGuardEvidence EvaluateTypeGuard(
        CompanionAmbiguityGuardDefinition guard,
        CompanionPlacePoolCandidate candidate,
        IReadOnlyList<string> required,
        IReadOnlyList<string> conflict)
    {
        var families = typeFamilyClassifier.ClassifyFamilies(candidate);
        var hasRequired = required.Any(families.Contains);
        var hasConflict = conflict.Any(families.Contains);
        if (hasRequired)
        {
            return new CompanionGuardEvidence(guard.GuardId, candidate.PlaceId, CompanionGuardEvidenceStatus.ConfirmedMatch, 0.9d, ["types"], ["required_family_present"], false);
        }

        if (hasConflict)
        {
            return new CompanionGuardEvidence(guard.GuardId, candidate.PlaceId, CompanionGuardEvidenceStatus.ConfirmedConflict, 0.9d, ["types"], ["dangerous_sibling_family_present"], false);
        }

        return Unknown(guard, candidate.PlaceId, requiresDetails: false, "type_family_unknown");
    }

    private static CompanionGuardEvidence EvaluateBoolGuard(
        CompanionAmbiguityGuardDefinition guard,
        string placeId,
        PlaceDetailsResult? details,
        bool? value,
        bool conflictWhen,
        string field,
        string conflictReason)
    {
        if (value == true)
        {
            return new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedMatch, 0.95d, [field], [$"{field}_true"], false);
        }

        if (conflictWhen)
        {
            return new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedConflict, 0.92d, [field], [conflictReason], false);
        }

        return Unknown(guard, placeId, requiresDetails: details is null, $"{field}_unknown");
    }

    private static CompanionGuardEvidence EvaluateAccessibilityGuard(
        CompanionAmbiguityGuardDefinition guard,
        string placeId,
        PlaceDetailsResult? details)
    {
        var options = details?.AccessibilityOptions;
        if (options?.WheelchairAccessibleEntrance == true
            || options?.WheelchairAccessibleParking == true
            || options?.WheelchairAccessibleRestroom == true
            || options?.WheelchairAccessibleSeating == true)
        {
            return new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedMatch, 0.95d, ["accessibilityOptions"], ["wheelchair_accessibility_true"], false);
        }

        if (options is not null
            && (options.WheelchairAccessibleEntrance == false
                || options.WheelchairAccessibleParking == false
                || options.WheelchairAccessibleRestroom == false
                || options.WheelchairAccessibleSeating == false))
        {
            return new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedConflict, 0.88d, ["accessibilityOptions"], ["wheelchair_accessibility_false"], false);
        }

        return Unknown(guard, placeId, requiresDetails: details is null, "wheelchair_accessibility_unknown");
    }

    private static CompanionGuardEvidence EvaluatePaymentGuard(
        CompanionAmbiguityGuardDefinition guard,
        string placeId,
        PlaceDetailsResult? details)
    {
        var options = details?.PaymentOptions;
        if (options?.AcceptsCreditCards == true || options?.AcceptsDebitCards == true || options?.AcceptsNfc == true)
        {
            return new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedMatch, 0.95d, ["paymentOptions"], ["card_payment_true"], false);
        }

        if (options?.AcceptsCashOnly == true
            && options.AcceptsCreditCards != true
            && options.AcceptsDebitCards != true
            && options.AcceptsNfc != true)
        {
            return new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedConflict, 0.9d, ["paymentOptions"], ["cash_only"], false);
        }

        return Unknown(guard, placeId, requiresDetails: details is null, "payment_options_unknown");
    }

    private static CompanionGuardEvidence EvaluateParkingGuard(
        CompanionAmbiguityGuardDefinition guard,
        string placeId,
        CompanionParkingEvidenceResult? parking)
    {
        if (parking?.EvidenceByPlaceId.TryGetValue(placeId, out var evidence) == true)
        {
            return evidence.EvidenceLevel switch
            {
                "confirmed_on_site" => new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedMatch, evidence.Confidence, ["parkingEvidence"], evidence.Reasons, false),
                "likely_on_site" or "nearby_parking" => new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.LikelyMatch, evidence.Confidence, ["parkingEvidence"], evidence.Reasons, false),
                "confirmed_no_parking" => new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.ConfirmedConflict, evidence.Confidence, ["parkingEvidence"], evidence.Reasons, false),
                _ => Unknown(guard, placeId, requiresDetails: false, "parking_evidence_unknown")
            };
        }

        return Unknown(guard, placeId, requiresDetails: false, "parking_evidence_unknown");
    }

    private static CompanionGuardEvidence Unknown(
        CompanionAmbiguityGuardDefinition guard,
        string placeId,
        bool requiresDetails,
        string reason)
    {
        return new CompanionGuardEvidence(guard.GuardId, placeId, CompanionGuardEvidenceStatus.Unknown, 0.35d, guard.EvidenceFields, [reason], requiresDetails);
    }

    private static IReadOnlyList<CompanionAmbiguityGuardDefinition> ResolveApplicableGuards(
        CompanionPlaceSearchStrategy strategy,
        CompanionSemanticIntent intent)
    {
        var haystack = Normalize(string.Join(' ',
            intent.PlaceQuery,
            strategy.CanonicalQuery,
            intent.Role.RequestedRole,
            string.Join(' ', intent.Role.RequiredCoreRoles),
            string.Join(' ', intent.Role.AcceptableSubRoles),
            string.Join(' ', intent.Role.Modifiers),
            string.Join(' ', intent.HardFilters),
            string.Join(' ', intent.SoftPreferences),
            string.Join(' ', intent.RequestedDetailFields)));

        var guards = new List<CompanionAmbiguityGuardDefinition>();
        AddIf(haystack.Contains("bank", StringComparison.Ordinal) && !haystack.Contains("atm", StringComparison.Ordinal), "bank_branch_vs_atm");
        AddIf(haystack.Contains("atm", StringComparison.Ordinal), "atm_vs_bank_branch");
        AddIf(haystack.Contains("post office", StringComparison.Ordinal) || haystack.Contains("post offices", StringComparison.Ordinal), "post_office_vs_mailbox");
        AddIf(haystack.Contains("hotel", StringComparison.Ordinal) || haystack.Contains("lodging", StringComparison.Ordinal), "hotel_vs_hotel_restaurant");
        AddIf(haystack.Contains("car park", StringComparison.Ordinal) || haystack.Contains("parking", StringComparison.Ordinal), "car_park_vs_public_park");
        AddIf(haystack.Contains("fine dining", StringComparison.Ordinal) || haystack.Contains("upscale", StringComparison.Ordinal), "fine_dining_vs_fast_food");
        AddIf(haystack.Contains("delivery", StringComparison.Ordinal), "restaurant_delivery_vs_dine_in_only");
        AddIf(haystack.Contains("takeaway", StringComparison.Ordinal) || haystack.Contains("takeout", StringComparison.Ordinal), "takeaway_vs_dine_in_only");
        AddIf(haystack.Contains("dog friendly", StringComparison.Ordinal) || haystack.Contains("dogs allowed", StringComparison.Ordinal) || haystack.Contains("dog_friendly", StringComparison.Ordinal), "dog_friendly_policy");
        AddIf(haystack.Contains("wheelchair", StringComparison.Ordinal) || haystack.Contains("accessible", StringComparison.Ordinal), "wheelchair_accessibility");
        AddIf(haystack.Contains("outdoor seating", StringComparison.Ordinal) || haystack.Contains("outside seating", StringComparison.Ordinal), "outdoor_seating");
        AddIf(haystack.Contains("card", StringComparison.Ordinal) || haystack.Contains("cashless", StringComparison.Ordinal), "card_payments");
        AddIf(haystack.Contains("parking", StringComparison.Ordinal), "parking_availability");
        return guards.DistinctBy(static guard => guard.GuardId, StringComparer.OrdinalIgnoreCase).ToArray();

        void AddIf(bool condition, string guardId)
        {
            if (condition)
            {
                var guard = Catalogue.First(item => item.GuardId == guardId);
                guards.Add(guard);
            }
        }
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
    }
}
