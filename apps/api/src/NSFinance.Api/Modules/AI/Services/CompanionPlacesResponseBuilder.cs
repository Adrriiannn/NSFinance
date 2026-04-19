using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionPlacesResponseBuilder
{
    CompanionPlacesResponseBuildResult Build(FinancialCompanionContext context);
}

public sealed record CompanionPlacesResponseBuildResult(
    string ReplyText,
    bool Succeeded,
    bool HasInsufficientData,
    IReadOnlyList<string> InsufficientDataReasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> BasisSummaryAdditions);

public sealed class CompanionPlacesResponseBuilder(
    IOptions<CompanionOrchestrationOptions> options) : ICompanionPlacesResponseBuilder
{
    private readonly CompanionOrchestrationOptions orchestrationOptions = options.Value;

    public CompanionPlacesResponseBuildResult Build(FinancialCompanionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.ToolOutputs.TryGetValue(CompanionTool.PlacesSearch.ToOutputKey(), out var placesOutput)
            || placesOutput is not CompanionPlacesSearchContext places
            || places.Items.Count == 0)
        {
            return new CompanionPlacesResponseBuildResult(
                ReplyText:
                "I couldn't find grounded place results for that request yet. "
                + "Please try a nearby area or a more specific place type.",
                Succeeded: false,
                HasInsufficientData: true,
                InsufficientDataReasons: ["local_places_intent_missing_places_grounding"],
                Warnings: ["places_search_no_data"],
                BasisSummaryAdditions:
                [
                    "places_search_no_data",
                    "places_response_not_built_due_to_missing_candidates"
                ]);
        }

        var maxItems = Math.Clamp(orchestrationOptions.MaxPlaceItems, 1, 8);
        var shortlist = places.Items
            .Take(maxItems)
            .ToArray();
        var lines = new List<string>(shortlist.Length + 4)
        {
            $"I found {shortlist.Length} place option{(shortlist.Length == 1 ? string.Empty : "s")} based on live place search results:"
        };

        for (var i = 0; i < shortlist.Length; i++)
        {
            lines.Add($"{i + 1}. {BuildPlaceLine(shortlist[i])}");
        }

        var budgetHint = BuildBudgetHint(context, shortlist);
        if (!string.IsNullOrWhiteSpace(budgetHint))
        {
            lines.Add(budgetHint);
        }

        var detailsHint = BuildTopDetailsHint(context, shortlist[0]);
        if (!string.IsNullOrWhiteSpace(detailsHint))
        {
            lines.Add(detailsHint);
        }

        return new CompanionPlacesResponseBuildResult(
            ReplyText: string.Join(Environment.NewLine, lines),
            Succeeded: true,
            HasInsufficientData: false,
            InsufficientDataReasons: [],
            Warnings:
            [
                "places_response_built_from_grounded_candidates",
                $"places_response_candidate_count:{shortlist.Length}"
            ],
            BasisSummaryAdditions:
            [
                "places_response_built_from_grounded_candidates",
                $"places_response_candidate_count:{shortlist.Length}"
            ]);
    }

    private static string BuildPlaceLine(CompanionPlaceSearchContextItem place)
    {
        var segments = new List<string>(6)
        {
            place.Name ?? "Unnamed place"
        };

        var descriptors = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(place.PrimaryTypeDisplayName))
        {
            descriptors.Add(place.PrimaryTypeDisplayName);
        }
        else if (!string.IsNullOrWhiteSpace(place.Category))
        {
            descriptors.Add(place.Category);
        }

        if (place.Rating.HasValue && place.UserRatingCount.HasValue)
        {
            descriptors.Add($"{place.Rating.Value:0.0} stars ({place.UserRatingCount.Value} ratings)");
        }
        else if (place.Rating.HasValue)
        {
            descriptors.Add($"{place.Rating.Value:0.0} stars");
        }

        if (place.OpeningHours?.OpenNow == true)
        {
            descriptors.Add("open now");
        }
        else if (place.OpeningHours?.OpenNow == false)
        {
            descriptors.Add("currently closed");
        }

        var readablePriceLevel = ToReadablePriceLevel(place.PriceLevel);
        if (!string.IsNullOrWhiteSpace(readablePriceLevel))
        {
            descriptors.Add($"price: {readablePriceLevel}");
        }

        if (!string.IsNullOrWhiteSpace(place.ShortFormattedAddress))
        {
            descriptors.Add($"near {place.ShortFormattedAddress}");
        }
        else if (!string.IsNullOrWhiteSpace(place.FormattedAddress))
        {
            descriptors.Add($"near {place.FormattedAddress}");
        }

        if (descriptors.Count > 0)
        {
            segments.Add(string.Join(", ", descriptors));
        }

        return string.Join(" - ", segments);
    }

    private static string? BuildBudgetHint(
        FinancialCompanionContext context,
        IReadOnlyList<CompanionPlaceSearchContextItem> shortlist)
    {
        if (!context.ToolOutputs.TryGetValue(CompanionTool.BudgetStatus.ToOutputKey(), out var budgetOutput)
            || budgetOutput is not CompanionBudgetStatusContext budget)
        {
            return null;
        }

        if (budget.RemainingBudget.HasValue && budget.RemainingBudget.Value <= 0m)
        {
            return "Your budget looks tight right now, so start with lower-price options where available.";
        }

        var hasLowerPriceOptions = shortlist.Any(place =>
            string.Equals(place.PriceLevel, "PRICE_LEVEL_FREE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(place.PriceLevel, "PRICE_LEVEL_INEXPENSIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(place.PriceLevel, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(place.PriceLevel, "2", StringComparison.OrdinalIgnoreCase));

        if (hasLowerPriceOptions)
        {
            return "Several options show lower-to-moderate pricing, which can help keep this outing budget-friendly.";
        }

        return null;
    }

    private static string? BuildTopDetailsHint(
        FinancialCompanionContext context,
        CompanionPlaceSearchContextItem topPlace)
    {
        if (!context.ToolOutputs.TryGetValue(CompanionTool.PlaceDetails.ToOutputKey(), out var detailsOutput)
            || detailsOutput is not CompanionPlaceDetailsContext details
            || string.IsNullOrWhiteSpace(details.PlaceId)
            || !string.Equals(details.PlaceId, topPlace.PlaceId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var detailBits = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(details.Website))
        {
            detailBits.Add("website available");
        }

        if (!string.IsNullOrWhiteSpace(details.Address))
        {
            detailBits.Add($"address: {details.Address}");
        }

        if (!string.IsNullOrWhiteSpace(details.PriceLevel))
        {
            detailBits.Add($"price: {ToReadablePriceLevel(details.PriceLevel) ?? details.PriceLevel}");
        }

        if (detailBits.Count == 0)
        {
            return null;
        }

        return $"Top result details for {details.Name ?? topPlace.Name ?? "this place"}: {string.Join(", ", detailBits)}.";
    }

    private static string? ToReadablePriceLevel(string? priceLevel)
    {
        if (string.IsNullOrWhiteSpace(priceLevel))
        {
            return null;
        }

        return priceLevel.Trim().ToUpperInvariant() switch
        {
            "PRICE_LEVEL_FREE" => "free",
            "PRICE_LEVEL_INEXPENSIVE" => "inexpensive",
            "PRICE_LEVEL_MODERATE" => "moderate",
            "PRICE_LEVEL_EXPENSIVE" => "expensive",
            "PRICE_LEVEL_VERY_EXPENSIVE" => "very expensive",
            "1" => "inexpensive",
            "2" => "moderate",
            "3" => "expensive",
            "4" => "very expensive",
            _ => priceLevel
        };
    }
}
