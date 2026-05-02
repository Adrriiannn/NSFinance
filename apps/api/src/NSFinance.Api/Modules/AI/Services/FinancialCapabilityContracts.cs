namespace NSFinance.Api.Modules.AI.Services;

public interface IUserFinancialSummaryService
{
    Task<UserFinancialSummary> GetSummaryAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ISpendingAnalysisService
{
    Task<SpendingAnalysisResult> AnalyzeAsync(Guid userId, int lookbackDays, CancellationToken cancellationToken);
}

public interface IRecurringObligationsService
{
    Task<RecurringObligationsResult> GetRecurringAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IBudgetStatusService
{
    Task<BudgetStatusResult> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ITransactionQueryService
{
    Task<TransactionQueryResult> QueryAsync(Guid userId, string query, int maxRows, CancellationToken cancellationToken);
}

public interface IUserFinancialContextProfileService
{
    Task<UserFinancialContextSnapshot> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IPlacesSearchService
{
    Task<PlaceSearchResult> SearchAsync(
        string query,
        string country,
        PlaceSearchLocationContext? locationContext,
        CancellationToken cancellationToken);
}

public interface IPlaceDetailsService
{
    Task<PlaceDetailsResult> GetDetailsAsync(string placeId, CancellationToken cancellationToken);
}

public interface IReviewInsightsService
{
    Task<ReviewInsightsResult> GetInsightsAsync(string placeId, CancellationToken cancellationToken);
}

public sealed record UserFinancialSummary(
    decimal IncomeLast30Days,
    decimal SpendLast30Days,
    decimal NetLast30Days,
    string Currency);

public sealed record SpendingAnalysisResult(
    IReadOnlyDictionary<int, decimal> SpendByDomain,
    decimal AverageDailySpend,
    decimal LargestExpense);

public sealed record RecurringObligationsResult(
    IReadOnlyList<RecurringObligationItem> Items,
    decimal EstimatedMonthlyTotal);

public sealed record RecurringObligationItem(
    string Name,
    decimal Amount,
    string Currency,
    int FrequencyDays);

public sealed record BudgetStatusResult(
    bool HasBudgetPlan,
    decimal? MonthlyBudget,
    decimal MonthToDateSpend,
    decimal? RemainingBudget);

public sealed record TransactionQueryResult(
    IReadOnlyList<TransactionQueryItem> Items);

public sealed record TransactionQueryItem(
    DateTime BookedAtUtc,
    decimal Amount,
    string Currency,
    string Description,
    int? DomainCode,
    int? CategoryCode);

public sealed record PlaceSearchResult(
    IReadOnlyList<PlaceSearchItem> Items,
    PlaceSearchMetadata? Metadata = null,
    IReadOnlyList<string>? Warnings = null);

public sealed record PlaceSearchLocationContext(
    string? Source = null,
    double? Latitude = null,
    double? Longitude = null,
    int? RadiusMeters = null,
    string? TypedArea = null,
    string? LocalityLabel = null,
    string? AccuracyBucket = null,
    DateTimeOffset? CapturedAtUtc = null,
    RealWorldDiscoveryDomain? PlannerSelectedDomain = null,
    string? PlannerSelectedConcept = null,
    RealWorldIntentFamily? PlannerIntentFamily = null,
    bool PlannerAuthoritative = false,
    bool HasNearMeSemantic = false,
    bool ImplicitLocalBias = false,
    RealWorldExecutionMode? PlannerExecutionMode = null,
    int? PlannerMaxShortlist = null,
    string? SearchScope = null,
    string? PlannerBrandTerm = null,
    string? PlannerCanonicalConcept = null,
    IReadOnlyList<string>? PlannerIncludeTypes = null,
    IReadOnlyList<string>? PlannerExcludeTypes = null,
    double? DeviceLatitude = null,
    double? DeviceLongitude = null,
    int? DeviceRadiusMeters = null,
    string? DeviceLocalityLabel = null,
    string? DeviceSource = null);

public sealed record PlaceSearchItem(
    string PlaceId,
    string Name,
    string? Category,
    string? PriceLevel,
    string? ResourceName = null,
    string? DisplayName = null,
    string? PrimaryType = null,
    string? PrimaryTypeDisplayName = null,
    IReadOnlyList<string>? Types = null,
    string? NationalPhoneNumber = null,
    string? FormattedAddress = null,
    string? ShortFormattedAddress = null,
    double? Rating = null,
    int? UserRatingCount = null,
    string? GoogleMapsUri = null,
    string? WebsiteUri = null,
    PlaceOpeningHoursSummary? OpeningHours = null,
    string? BusinessStatus = null,
    string? IconMaskBaseUri = null,
    string? IconBackgroundColor = null,
    bool? Takeout = null,
    bool? Delivery = null,
    bool? DineIn = null,
    bool? Reservable = null,
    bool? ServesBreakfast = null,
    bool? ServesLunch = null,
    bool? ServesDinner = null,
    bool? ServesBeer = null,
    bool? ServesWine = null,
    bool? ServesBrunch = null,
    bool? ServesVegetarianFood = null,
    bool? OutdoorSeating = null,
    bool? LiveMusic = null,
    bool? MenuForChildren = null,
    bool? ServesCocktails = null,
    bool? ServesDessert = null,
    bool? ServesCoffee = null,
    bool? AllowsDogs = null,
    bool? Restroom = null,
    bool? GoodForGroups = null,
    bool? GoodForWatchingSports = null,
    PlacePaymentOptionsSummary? PaymentOptions = null,
    PlaceAccessibilitySummary? AccessibilityOptions = null,
    PlaceEditorialSummary? EditorialSummary = null,
    IReadOnlyList<PlacePhotoSummary>? Photos = null,
    PlaceLocationSummary? Location = null);

public sealed record PlaceDetailsResult(
    string PlaceId,
    string Name,
    string? Address,
    string? Website,
    string? PriceLevel,
    string? NationalPhoneNumber = null,
    string? GoogleMapsUri = null,
    string? BusinessStatus = null,
    double? Rating = null,
    int? UserRatingCount = null,
    string? PrimaryType = null,
    string? PrimaryTypeDisplayName = null,
    IReadOnlyList<string>? Types = null,
    PlaceOpeningHoursSummary? OpeningHours = null,
    PlacePaymentOptionsSummary? PaymentOptions = null,
    PlaceAccessibilitySummary? AccessibilityOptions = null,
    PlaceEditorialSummary? EditorialSummary = null,
    PlaceLocationSummary? Location = null,
    IReadOnlyList<PlacePhotoSummary>? Photos = null);

public sealed record ReviewInsightsResult(
    string PlaceId,
    string Summary,
    double? AverageRating);
