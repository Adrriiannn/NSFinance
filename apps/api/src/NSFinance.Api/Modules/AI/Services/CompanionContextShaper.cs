using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionContextShaper
{
    CompanionShapedToolData ShapeFinancialSummary(UserFinancialSummary summary);
    CompanionShapedToolData ShapeSpendingAnalysis(SpendingAnalysisResult spending);
    CompanionShapedToolData ShapeRecurringObligations(RecurringObligationsResult recurring);
    CompanionShapedToolData ShapeBudgetStatus(BudgetStatusResult budget);
    CompanionShapedToolData ShapeTransactionMatches(TransactionQueryResult transactions);
    CompanionShapedToolData ShapePlaceSearch(PlaceSearchResult places);
    CompanionShapedToolData ShapePlaceDetails(PlaceDetailsResult details);
    CompanionShapedToolData ShapeReviewInsights(ReviewInsightsResult insights);
    CompanionContextTrimResult TrimToPayloadBudget(
        IReadOnlyDictionary<string, object?> contextOutputs,
        IReadOnlyList<CompanionToolExecutionRecord> records);
}

public sealed record CompanionShapedToolData(
    string OutputKey,
    object? Output,
    bool HasData,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> TrimIndicators);

public sealed class CompanionContextShaper(
    IOptions<CompanionOrchestrationOptions> options) : ICompanionContextShaper
{
    private readonly CompanionOrchestrationOptions _options = options.Value;

    private static readonly JsonSerializerOptions ContextJsonOptions = new()
    {
        WriteIndented = false
    };

    public CompanionShapedToolData ShapeFinancialSummary(UserFinancialSummary summary)
    {
        var output = new CompanionFinancialSummaryContext(
            summary.IncomeLast30Days,
            summary.SpendLast30Days,
            summary.NetLast30Days,
            summary.Currency);
        var hasData = !string.IsNullOrWhiteSpace(summary.Currency);
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.FinancialSummary.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: [],
            TrimIndicators: []);
    }

    public CompanionShapedToolData ShapeSpendingAnalysis(SpendingAnalysisResult spending)
    {
        var topDomains = spending.SpendByDomain
            .OrderByDescending(x => Math.Abs(x.Value))
            .Take(_options.MaxSpendDomains)
            .Select(x => new CompanionDomainSpendContextItem(x.Key, x.Value))
            .ToArray();
        var output = new CompanionSpendingAnalysisContext(
            TopDomainSpend: topDomains,
            DomainCount: spending.SpendByDomain.Count,
            AverageDailySpend: spending.AverageDailySpend,
            LargestExpense: spending.LargestExpense);
        var hasData = topDomains.Length > 0 || spending.AverageDailySpend > 0m || spending.LargestExpense > 0m;
        var trimmed = topDomains.Length < spending.SpendByDomain.Count
            ? new[] { "payload_trimmed:spending_analysis_top_domains" }
            : Array.Empty<string>();
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.SpendingAnalysis.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: [],
            TrimIndicators: trimmed);
    }

    public CompanionShapedToolData ShapeRecurringObligations(RecurringObligationsResult recurring)
    {
        var topItems = recurring.Items
            .OrderByDescending(x => Math.Abs(x.Amount))
            .Take(_options.MaxRecurringItems)
            .Select(item => new CompanionRecurringItemContext(
                Name: ClampText(item.Name, 60),
                Amount: item.Amount,
                Currency: item.Currency,
                FrequencyDays: item.FrequencyDays))
            .ToArray();
        var output = new CompanionRecurringObligationsContext(
            TotalItemCount: recurring.Items.Count,
            EstimatedMonthlyTotal: recurring.EstimatedMonthlyTotal,
            TopItems: topItems);
        var hasData = topItems.Length > 0 || recurring.EstimatedMonthlyTotal > 0m;
        var trimmed = topItems.Length < recurring.Items.Count
            ? new[] { "payload_trimmed:recurring_items" }
            : Array.Empty<string>();
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.RecurringObligations.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: [],
            TrimIndicators: trimmed);
    }

    public CompanionShapedToolData ShapeBudgetStatus(BudgetStatusResult budget)
    {
        var output = new CompanionBudgetStatusContext(
            budget.HasBudgetPlan,
            budget.MonthlyBudget,
            budget.MonthToDateSpend,
            budget.RemainingBudget);
        var hasData = budget.HasBudgetPlan || budget.MonthToDateSpend > 0m || budget.MonthlyBudget.HasValue || budget.RemainingBudget.HasValue;
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.BudgetStatus.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: [],
            TrimIndicators: []);
    }

    public CompanionShapedToolData ShapeTransactionMatches(TransactionQueryResult transactions)
    {
        var items = transactions.Items
            .Take(_options.MaxTransactionRows)
            .Select(item => new CompanionTransactionMatchContext(
                BookedDateUtc: item.BookedAtUtc.Date,
                Amount: item.Amount,
                Currency: item.Currency,
                Description: ClampText(item.Description, 90),
                DomainCode: item.DomainCode,
                CategoryCode: item.CategoryCode))
            .ToArray();
        var output = new CompanionTransactionMatchesContext(
            TotalItemCount: transactions.Items.Count,
            Items: items);
        var hasData = items.Length > 0;
        var trimmed = items.Length < transactions.Items.Count
            ? new[] { "payload_trimmed:transaction_matches_rows" }
            : Array.Empty<string>();
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.TransactionQuery.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: [],
            TrimIndicators: trimmed);
    }

    public CompanionShapedToolData ShapePlaceSearch(PlaceSearchResult places)
    {
        var items = places.Items
            .Take(_options.MaxPlaceItems)
            .Select(item => new CompanionPlaceSearchContextItem(
                PlaceId: ClampText(item.PlaceId, 80),
                Name: ClampText(item.DisplayName ?? item.Name, 80),
                Category: ClampText(item.Category, 40),
                PriceLevel: ClampText(item.PriceLevel, 24),
                PrimaryType: ClampText(item.PrimaryType, 40),
                PrimaryTypeDisplayName: ClampText(item.PrimaryTypeDisplayName, 40),
                Types: item.Types?.Take(10).ToArray() ?? [],
                NationalPhoneNumber: ClampText(item.NationalPhoneNumber, 40),
                FormattedAddress: ClampText(item.FormattedAddress, 140),
                ShortFormattedAddress: ClampText(item.ShortFormattedAddress, 100),
                Rating: item.Rating,
                UserRatingCount: item.UserRatingCount,
                GoogleMapsUri: ClampText(item.GoogleMapsUri, 140),
                WebsiteUri: ClampText(item.WebsiteUri, 140),
                OpeningHours: item.OpeningHours,
                BusinessStatus: ClampText(item.BusinessStatus, 30),
                IconMaskBaseUri: ClampText(item.IconMaskBaseUri, 140),
                IconBackgroundColor: ClampText(item.IconBackgroundColor, 30),
                Takeout: item.Takeout,
                Delivery: item.Delivery,
                DineIn: item.DineIn,
                Reservable: item.Reservable,
                ServesBreakfast: item.ServesBreakfast,
                ServesLunch: item.ServesLunch,
                ServesDinner: item.ServesDinner,
                ServesBeer: item.ServesBeer,
                ServesWine: item.ServesWine,
                ServesBrunch: item.ServesBrunch,
                ServesVegetarianFood: item.ServesVegetarianFood,
                OutdoorSeating: item.OutdoorSeating,
                LiveMusic: item.LiveMusic,
                MenuForChildren: item.MenuForChildren,
                ServesCocktails: item.ServesCocktails,
                ServesDessert: item.ServesDessert,
                ServesCoffee: item.ServesCoffee,
                AllowsDogs: item.AllowsDogs,
                Restroom: item.Restroom,
                GoodForGroups: item.GoodForGroups,
                GoodForWatchingSports: item.GoodForWatchingSports,
                PaymentOptions: item.PaymentOptions,
                AccessibilityOptions: item.AccessibilityOptions,
                EditorialSummary: item.EditorialSummary,
                Location: item.Location))
            .ToArray();
        var output = new CompanionPlacesSearchContext(
            TotalItemCount: places.Items.Count,
            Items: items);
        var hasData = items.Length > 0;
        var trimmed = items.Length < places.Items.Count
            ? new[] { "payload_trimmed:place_search_items" }
            : Array.Empty<string>();
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.PlacesSearch.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: places.Warnings ?? [],
            TrimIndicators: trimmed);
    }

    public CompanionShapedToolData ShapePlaceDetails(PlaceDetailsResult details)
    {
        var output = new CompanionPlaceDetailsContext(
            PlaceId: ClampText(details.PlaceId, 80),
            Name: ClampText(details.Name, 80),
            Address: ClampText(details.Address, 120),
            Website: ClampText(details.Website, 120),
            PriceLevel: ClampText(details.PriceLevel, 20));
        var hasData = !string.IsNullOrWhiteSpace(output.PlaceId) || !string.IsNullOrWhiteSpace(output.Name);
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.PlaceDetails.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: [],
            TrimIndicators: []);
    }

    public CompanionShapedToolData ShapeReviewInsights(ReviewInsightsResult insights)
    {
        var output = new CompanionReviewInsightsContext(
            PlaceId: ClampText(insights.PlaceId, 80),
            Summary: ClampText(insights.Summary, _options.MaxSummaryTextLength),
            AverageRating: insights.AverageRating);
        var hasData = !string.IsNullOrWhiteSpace(output.Summary) || output.AverageRating.HasValue;
        return new CompanionShapedToolData(
            OutputKey: CompanionTool.ReviewInsights.ToOutputKey(),
            Output: output,
            HasData: hasData,
            Warnings: [],
            TrimIndicators: []);
    }

    public CompanionContextTrimResult TrimToPayloadBudget(
        IReadOnlyDictionary<string, object?> contextOutputs,
        IReadOnlyList<CompanionToolExecutionRecord> records)
    {
        var outputs = new Dictionary<string, object?>(contextOutputs, StringComparer.OrdinalIgnoreCase);
        var recordByOutputKey = records
            .Where(record => record.IncludedInContext && !string.IsNullOrWhiteSpace(record.OutputKey))
            .ToDictionary(record => record.OutputKey, record => record, StringComparer.OrdinalIgnoreCase);
        var adjustedRecords = new List<CompanionToolExecutionRecord>(records);
        var warnings = new List<string>(2);
        var trimmedIndicators = new List<string>(2);

        var removableOrder = new[]
        {
            CompanionTool.ReviewInsights,
            CompanionTool.PlaceDetails,
            CompanionTool.PlacesSearch,
            CompanionTool.TransactionQuery,
            CompanionTool.SpendingAnalysis,
            CompanionTool.RecurringObligations,
            CompanionTool.BudgetStatus
        };

        string Serialize() => JsonSerializer.Serialize(outputs, ContextJsonOptions);
        while (Serialize().Length > _options.MaxSerializedContextChars)
        {
            var candidate = removableOrder
                .Select(tool => tool.ToOutputKey())
                .FirstOrDefault(key =>
                    outputs.ContainsKey(key)
                    && recordByOutputKey.TryGetValue(key, out var record)
                    && !record.PlannedTool.IsRequired);
            if (candidate is null)
            {
                warnings.Add("context_payload_over_budget");
                break;
            }

            outputs.Remove(candidate);
            var oldRecord = recordByOutputKey[candidate];
            var trimmedRecord = oldRecord with
            {
                Status = CompanionToolExecutionStatus.TrimmedOut,
                IncludedInContext = false,
                ReasonCode = "payload_trimmed"
            };
            recordByOutputKey[candidate] = trimmedRecord;
            for (var i = 0; i < adjustedRecords.Count; i++)
            {
                if (adjustedRecords[i] == oldRecord)
                {
                    adjustedRecords[i] = trimmedRecord;
                    break;
                }
            }

            trimmedIndicators.Add($"{oldRecord.ContractName}:payload_trimmed");
            warnings.Add("context_payload_trimmed");
        }

        return new CompanionContextTrimResult(
            Outputs: outputs,
            TrimmedIndicators: trimmedIndicators,
            Warnings: warnings,
            AdjustedRecords: adjustedRecords);
    }

    private string? ClampText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var safeMax = Math.Clamp(maxLength, 12, _options.MaxSummaryTextLength);
        var trimmed = value.Trim();
        return trimmed.Length <= safeMax ? trimmed : trimmed[..safeMax];
    }
}

public sealed record CompanionFinancialSummaryContext(
    decimal IncomeLast30Days,
    decimal SpendLast30Days,
    decimal NetLast30Days,
    string Currency);

public sealed record CompanionSpendingAnalysisContext(
    IReadOnlyList<CompanionDomainSpendContextItem> TopDomainSpend,
    int DomainCount,
    decimal AverageDailySpend,
    decimal LargestExpense);

public sealed record CompanionDomainSpendContextItem(
    int DomainCode,
    decimal Amount);

public sealed record CompanionRecurringObligationsContext(
    int TotalItemCount,
    decimal EstimatedMonthlyTotal,
    IReadOnlyList<CompanionRecurringItemContext> TopItems);

public sealed record CompanionRecurringItemContext(
    string? Name,
    decimal Amount,
    string Currency,
    int FrequencyDays);

public sealed record CompanionBudgetStatusContext(
    bool HasBudgetPlan,
    decimal? MonthlyBudget,
    decimal MonthToDateSpend,
    decimal? RemainingBudget);

public sealed record CompanionTransactionMatchesContext(
    int TotalItemCount,
    IReadOnlyList<CompanionTransactionMatchContext> Items);

public sealed record CompanionTransactionMatchContext(
    DateTime BookedDateUtc,
    decimal Amount,
    string Currency,
    string? Description,
    int? DomainCode,
    int? CategoryCode);

public sealed record CompanionPlacesSearchContext(
    int TotalItemCount,
    IReadOnlyList<CompanionPlaceSearchContextItem> Items);

public sealed record CompanionPlaceSearchContextItem(
    string? PlaceId,
    string? Name,
    string? Category,
    string? PriceLevel,
    string? PrimaryType,
    string? PrimaryTypeDisplayName,
    IReadOnlyList<string> Types,
    string? NationalPhoneNumber,
    string? FormattedAddress,
    string? ShortFormattedAddress,
    double? Rating,
    int? UserRatingCount,
    string? GoogleMapsUri,
    string? WebsiteUri,
    PlaceOpeningHoursSummary? OpeningHours,
    string? BusinessStatus,
    string? IconMaskBaseUri,
    string? IconBackgroundColor,
    bool? Takeout,
    bool? Delivery,
    bool? DineIn,
    bool? Reservable,
    bool? ServesBreakfast,
    bool? ServesLunch,
    bool? ServesDinner,
    bool? ServesBeer,
    bool? ServesWine,
    bool? ServesBrunch,
    bool? ServesVegetarianFood,
    bool? OutdoorSeating,
    bool? LiveMusic,
    bool? MenuForChildren,
    bool? ServesCocktails,
    bool? ServesDessert,
    bool? ServesCoffee,
    bool? AllowsDogs,
    bool? Restroom,
    bool? GoodForGroups,
    bool? GoodForWatchingSports,
    PlacePaymentOptionsSummary? PaymentOptions,
    PlaceAccessibilitySummary? AccessibilityOptions,
    PlaceEditorialSummary? EditorialSummary,
    PlaceLocationSummary? Location);

public sealed record CompanionPlaceDetailsContext(
    string? PlaceId,
    string? Name,
    string? Address,
    string? Website,
    string? PriceLevel);

public sealed record CompanionReviewInsightsContext(
    string? PlaceId,
    string? Summary,
    double? AverageRating);
