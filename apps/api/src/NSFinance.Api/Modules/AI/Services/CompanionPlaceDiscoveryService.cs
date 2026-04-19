using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class CompanionPlaceDiscoveryService(
    IGooglePlacesClient placesClient,
    IGooglePlacesFieldMaskProvider fieldMaskProvider,
    IGooglePlacesCache cache,
    IGooglePlacesCacheKeyBuilder cacheKeyBuilder,
    IOptions<GooglePlacesOptions> options,
    ILogger<CompanionPlaceDiscoveryService> logger) : ICompanionPlaceDiscoveryService
{
    private const string DiscoveryUseCase = "companion_discovery";
    private const string CompanionFieldMaskVariant = "companion_discovery_v1";
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
        CompanionPlaceDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var maxCandidates = Math.Clamp(placesOptions.MaxCompanionCandidates, 1, 8);
        var normalizedQuery = request.Query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return BuildResult(
                succeeded: false,
                fromCache: false,
                requestedCount: maxCandidates,
                candidates: [],
                elapsed: TimeSpan.Zero,
                timedOut: false,
                providerErrorCode: "empty_query",
                warnings: ["places_empty_query"]);
        }

        var cacheKey = cacheKeyBuilder.BuildCompanionDiscoveryKey(request, maxCandidates);
        if (cache.TryGet(cacheKey, nowUtc, out CompanionPlaceDiscoveryResult cached))
        {
            logger.LogInformation(
                "Google Places companion discovery cache hit queryHash={QueryHash} requested={Requested} returned={Returned}",
                cacheKey,
                maxCandidates,
                cached.Candidates.Count);

            return cached with
            {
                Metadata = cached.Metadata with
                {
                    FromCache = true,
                    Elapsed = TimeSpan.Zero
                }
            };
        }

        logger.LogInformation(
            "Google Places companion discovery cache miss requested={Requested} country={CountryCode}",
            maxCandidates,
            request.CountryCode ?? string.Empty);

        var providerResult = await placesClient.SearchTextAsync(
            new GooglePlacesSearchTextRequest(
                Query: normalizedQuery,
                MaxResultCount: maxCandidates,
                RegionCode: request.CountryCode,
                LanguageCode: request.LanguageCode,
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                RadiusMeters: request.RadiusMeters,
                FieldMask: fieldMaskProvider.CompanionDiscoverySearchMask,
                UseCaseTag: DiscoveryUseCase),
            cancellationToken);

        if (!providerResult.Succeeded)
        {
            var failureWarnings = new List<string>(2)
            {
                "places_provider_unavailable"
            };
            if (providerResult.TimedOut)
            {
                failureWarnings.Add("places_timeout");
            }

            var failed = BuildResult(
                succeeded: false,
                fromCache: false,
                requestedCount: maxCandidates,
                candidates: [],
                elapsed: providerResult.Elapsed,
                timedOut: providerResult.TimedOut,
                providerErrorCode: providerResult.ErrorCode,
                warnings: failureWarnings);
            cache.Set(
                cacheKey,
                failed,
                nowUtc,
                TimeSpan.FromSeconds(Math.Max(1, placesOptions.FailureCacheTtlSeconds)));

            logger.LogWarning(
                "Google Places companion discovery failed timedOut={TimedOut} providerError={ProviderError}",
                providerResult.TimedOut,
                providerResult.ErrorCode ?? "unknown");
            return failed;
        }

        var candidates = (providerResult.Value ?? [])
            .Take(maxCandidates)
            .Select(MapToCandidate)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.PlaceId))
            .ToArray();
        var success = BuildResult(
            succeeded: true,
            fromCache: false,
            requestedCount: maxCandidates,
            candidates: candidates,
            elapsed: providerResult.Elapsed,
            timedOut: false,
            providerErrorCode: null,
            warnings: []);
        cache.Set(
            cacheKey,
            success,
            nowUtc,
            TimeSpan.FromSeconds(Math.Max(1, placesOptions.CompanionCacheTtlSeconds)));

        logger.LogInformation(
            "Google Places companion discovery success requested={Requested} returned={Returned} elapsedMs={ElapsedMs} fieldMaskVariant={FieldMaskVariant}",
            maxCandidates,
            candidates.Length,
            providerResult.Elapsed.TotalMilliseconds,
            CompanionFieldMaskVariant);
        return success;
    }

    private static CompanionPlaceCandidate MapToCandidate(GooglePlacesClientPlace place)
    {
        return new CompanionPlaceCandidate(
            PlaceId: place.PlaceId,
            ResourceName: place.ResourceName,
            DisplayName: place.DisplayName,
            PrimaryType: place.PrimaryType,
            PrimaryTypeDisplayName: place.PrimaryTypeDisplayName,
            Types: place.Types,
            NationalPhoneNumber: place.NationalPhoneNumber,
            FormattedAddress: place.FormattedAddress,
            ShortFormattedAddress: place.ShortFormattedAddress,
            Rating: place.Rating,
            UserRatingCount: place.UserRatingCount,
            GoogleMapsUri: place.GoogleMapsUri,
            WebsiteUri: place.WebsiteUri,
            OpeningHours: place.OpeningHours,
            BusinessStatus: place.BusinessStatus,
            PriceLevel: place.PriceLevel,
            IconMaskBaseUri: place.IconMaskBaseUri,
            IconBackgroundColor: place.IconBackgroundColor,
            Takeout: place.Takeout,
            Delivery: place.Delivery,
            DineIn: place.DineIn,
            Reservable: place.Reservable,
            ServesBreakfast: place.ServesBreakfast,
            ServesLunch: place.ServesLunch,
            ServesDinner: place.ServesDinner,
            ServesBeer: place.ServesBeer,
            ServesWine: place.ServesWine,
            ServesBrunch: place.ServesBrunch,
            ServesVegetarianFood: place.ServesVegetarianFood,
            OutdoorSeating: place.OutdoorSeating,
            LiveMusic: place.LiveMusic,
            MenuForChildren: place.MenuForChildren,
            ServesCocktails: place.ServesCocktails,
            ServesDessert: place.ServesDessert,
            ServesCoffee: place.ServesCoffee,
            AllowsDogs: place.AllowsDogs,
            Restroom: place.Restroom,
            GoodForGroups: place.GoodForGroups,
            GoodForWatchingSports: place.GoodForWatchingSports,
            PaymentOptions: place.PaymentOptions,
            AccessibilityOptions: place.AccessibilityOptions,
            EditorialSummary: place.EditorialSummary,
            Location: place.Location);
    }

    private static CompanionPlaceDiscoveryResult BuildResult(
        bool succeeded,
        bool fromCache,
        int requestedCount,
        IReadOnlyList<CompanionPlaceCandidate> candidates,
        TimeSpan elapsed,
        bool timedOut,
        string? providerErrorCode,
        IReadOnlyList<string> warnings)
    {
        return new CompanionPlaceDiscoveryResult(
            Succeeded: succeeded,
            Candidates: candidates,
            Metadata: new PlaceSearchMetadata(
                UseCase: DiscoveryUseCase,
                FromCache: fromCache,
                RequestedCandidateCount: requestedCount,
                ReturnedCandidateCount: candidates.Count,
                FieldMaskVariant: CompanionFieldMaskVariant,
                Elapsed: elapsed,
                TimedOut: timedOut,
                ProviderErrorCode: providerErrorCode),
            Warnings: warnings);
    }
}

public sealed class MerchantPlaceLookupService(
    IGooglePlacesClient placesClient,
    IGooglePlacesFieldMaskProvider fieldMaskProvider,
    IGooglePlacesCache cache,
    IGooglePlacesCacheKeyBuilder cacheKeyBuilder,
    IOptions<GooglePlacesOptions> options,
    ILogger<MerchantPlaceLookupService> logger) : IMerchantPlaceLookupService
{
    private const string LookupUseCase = "merchant_lookup";
    private const string MerchantFieldMaskVariant = "merchant_lookup_v1";
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<MerchantPlaceLookupResult> LookupAsync(
        MerchantPlaceLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var maxLookupCandidates = Math.Clamp(
            request.MaxCandidates,
            1,
            Math.Max(1, placesOptions.MaxMerchantLookupCandidates));
        var descriptor = request.MerchantDescriptor?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return BuildLookupResult(
                succeeded: false,
                fromCache: false,
                requestedCount: maxLookupCandidates,
                matches: [],
                elapsed: TimeSpan.Zero,
                timedOut: false,
                providerErrorCode: "empty_descriptor",
                warnings: ["merchant_lookup_empty_descriptor"]);
        }

        var cacheKey = cacheKeyBuilder.BuildMerchantLookupKey(request, maxLookupCandidates);
        if (cache.TryGet(cacheKey, nowUtc, out MerchantPlaceLookupResult cached))
        {
            logger.LogInformation(
                "Google Places merchant lookup cache hit keyHash={KeyHash} requested={Requested} returned={Returned}",
                cacheKey,
                maxLookupCandidates,
                cached.Matches.Count);
            return cached with
            {
                Metadata = cached.Metadata with
                {
                    FromCache = true,
                    Elapsed = TimeSpan.Zero
                }
            };
        }

        var providerResult = await placesClient.SearchTextAsync(
            new GooglePlacesSearchTextRequest(
                Query: descriptor,
                MaxResultCount: maxLookupCandidates,
                RegionCode: request.CountryCode,
                LanguageCode: request.LanguageCode,
                Latitude: null,
                Longitude: null,
                RadiusMeters: null,
                FieldMask: fieldMaskProvider.MerchantLookupSearchMask,
                UseCaseTag: LookupUseCase),
            cancellationToken);

        if (!providerResult.Succeeded)
        {
            var warnings = new List<string>(2) { "merchant_lookup_provider_unavailable" };
            if (providerResult.TimedOut)
            {
                warnings.Add("merchant_lookup_timeout");
            }

            var failedResult = BuildLookupResult(
                succeeded: false,
                fromCache: false,
                requestedCount: maxLookupCandidates,
                matches: [],
                elapsed: providerResult.Elapsed,
                timedOut: providerResult.TimedOut,
                providerErrorCode: providerResult.ErrorCode,
                warnings: warnings);
            cache.Set(
                cacheKey,
                failedResult,
                nowUtc,
                TimeSpan.FromSeconds(Math.Max(1, placesOptions.FailureCacheTtlSeconds)));

            logger.LogWarning(
                "Google Places merchant lookup failed timedOut={TimedOut} providerError={ProviderError}",
                providerResult.TimedOut,
                providerResult.ErrorCode ?? "unknown");
            return failedResult;
        }

        var matches = (providerResult.Value ?? [])
            .Take(maxLookupCandidates)
            .Select(MapToMatch)
            .Where(match => !string.IsNullOrWhiteSpace(match.PlaceId))
            .ToArray();
        var successResult = BuildLookupResult(
            succeeded: true,
            fromCache: false,
            requestedCount: maxLookupCandidates,
            matches: matches,
            elapsed: providerResult.Elapsed,
            timedOut: false,
            providerErrorCode: null,
            warnings: []);
        cache.Set(
            cacheKey,
            successResult,
            nowUtc,
            TimeSpan.FromSeconds(Math.Max(1, placesOptions.MerchantLookupCacheTtlSeconds)));

        logger.LogInformation(
            "Google Places merchant lookup success requested={Requested} returned={Returned} elapsedMs={ElapsedMs} fieldMaskVariant={FieldMaskVariant}",
            maxLookupCandidates,
            matches.Length,
            providerResult.Elapsed.TotalMilliseconds,
            MerchantFieldMaskVariant);
        return successResult;
    }

    private static MerchantPlaceMatch MapToMatch(GooglePlacesClientPlace place)
    {
        return new MerchantPlaceMatch(
            PlaceId: place.PlaceId,
            ResourceName: place.ResourceName,
            DisplayName: place.DisplayName,
            PrimaryType: place.PrimaryType,
            PrimaryTypeDisplayName: place.PrimaryTypeDisplayName,
            Types: place.Types,
            FormattedAddress: place.FormattedAddress,
            ShortFormattedAddress: place.ShortFormattedAddress,
            GoogleMapsUri: place.GoogleMapsUri,
            WebsiteUri: place.WebsiteUri,
            NationalPhoneNumber: place.NationalPhoneNumber,
            BusinessStatus: place.BusinessStatus,
            Rating: place.Rating,
            UserRatingCount: place.UserRatingCount,
            Location: place.Location);
    }

    private static MerchantPlaceLookupResult BuildLookupResult(
        bool succeeded,
        bool fromCache,
        int requestedCount,
        IReadOnlyList<MerchantPlaceMatch> matches,
        TimeSpan elapsed,
        bool timedOut,
        string? providerErrorCode,
        IReadOnlyList<string> warnings)
    {
        return new MerchantPlaceLookupResult(
            Succeeded: succeeded,
            Matches: matches,
            Metadata: new PlaceSearchMetadata(
                UseCase: LookupUseCase,
                FromCache: fromCache,
                RequestedCandidateCount: requestedCount,
                ReturnedCandidateCount: matches.Count,
                FieldMaskVariant: MerchantFieldMaskVariant,
                Elapsed: elapsed,
                TimedOut: timedOut,
                ProviderErrorCode: providerErrorCode),
            Warnings: warnings);
    }
}

public sealed class GooglePlacesCompanionSearchService(
    ICompanionPlaceDiscoveryService discoveryService,
    ILocalDiscoveryQueryShaper localDiscoveryQueryShaper,
    ICompanionLocalityResolutionService localityResolutionService,
    ICompanionPlaceRankingPolicy placeRankingPolicy,
    IOptions<GooglePlacesOptions> options,
    ILogger<GooglePlacesCompanionSearchService> logger) : IPlacesSearchService
{
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<PlaceSearchResult> SearchAsync(
        string query,
        string country,
        PlaceSearchLocationContext? locationContext,
        CancellationToken cancellationToken)
    {
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var shapedQuery = localDiscoveryQueryShaper.Shape(query, locationContext);
        AppendQueryShapeWarnings(shapedQuery, warnings);
        logger.LogInformation(
            "Companion places query shaped localDiscoveryCandidate={IsCandidate} confidence={Confidence} hasNearMeLanguage={HasNearMeLanguage} hasExplicitLocality={HasExplicitLocality} reasonCodes={ReasonCodes}",
            shapedQuery.Constraints.IsLocalDiscoveryCandidate,
            shapedQuery.Constraints.Confidence,
            shapedQuery.Constraints.HasNearMeLanguage,
            shapedQuery.Constraints.HasExplicitLocality,
            string.Join(',', shapedQuery.ReasonCodes));
        var rankingContext = BuildRankingContext(locationContext, shapedQuery.Constraints);

        var effectiveLocationContext = await ResolveLocationBiasAsync(
            locationContext,
            shapedQuery.Constraints,
            country,
            warnings,
            cancellationToken);

        var primaryRequest = BuildDiscoveryRequest(
            query: shapedQuery.Query,
            countryCode: country,
            locationContext: effectiveLocationContext);
        logger.LogInformation(
            "Companion places primary search query={Query} hasCoordinates={HasCoordinates} radiusMeters={RadiusMeters}",
            primaryRequest.Query,
            primaryRequest.Latitude.HasValue && primaryRequest.Longitude.HasValue,
            primaryRequest.RadiusMeters ?? 0);
        var primaryResult = await discoveryService.DiscoverAsync(
            primaryRequest,
            cancellationToken);
        warnings.UnionWith(primaryResult.Warnings ?? []);

        if (primaryResult.Succeeded && primaryResult.Candidates.Count > 0)
        {
            warnings.Add("places_query_shape:results_found_primary");
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        if (!primaryResult.Succeeded)
        {
            warnings.Add("places_query_shape:primary_search_failed");
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        warnings.Add("places_query_shape:no_results_primary");
        var fallbackQuery = BuildFallbackQuery(
            originalQuery: query,
            constraints: shapedQuery.Constraints,
            locationContext: effectiveLocationContext);
        if (string.IsNullOrWhiteSpace(fallbackQuery)
            || string.Equals(
                fallbackQuery.Trim(),
                shapedQuery.Query.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("places_query_shape:no_results_fallback");
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        warnings.Add("places_query_shape:fallback_text_search");
        var fallbackLocationContext = BuildFallbackLocationContext(
            effectiveLocationContext,
            warnings);
        var fallbackRequest = BuildDiscoveryRequest(
            query: fallbackQuery,
            countryCode: country,
            locationContext: fallbackLocationContext);
        logger.LogInformation(
            "Companion places fallback search query={Query} hasCoordinates={HasCoordinates} radiusMeters={RadiusMeters}",
            fallbackRequest.Query,
            fallbackRequest.Latitude.HasValue && fallbackRequest.Longitude.HasValue,
            fallbackRequest.RadiusMeters ?? 0);

        var fallbackResult = await discoveryService.DiscoverAsync(
            fallbackRequest,
            cancellationToken);
        warnings.UnionWith(fallbackResult.Warnings ?? []);
        if (fallbackResult.Succeeded && fallbackResult.Candidates.Count > 0)
        {
            warnings.Add("places_query_shape:results_found_fallback");
            return BuildSearchResult(fallbackResult, warnings, rankingContext);
        }

        if (!fallbackResult.Succeeded)
        {
            warnings.Add("places_query_shape:fallback_search_failed");
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        warnings.Add("places_query_shape:no_results_fallback");
        return BuildSearchResult(fallbackResult, warnings, rankingContext);
    }

    private async Task<PlaceSearchLocationContext?> ResolveLocationBiasAsync(
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult constraints,
        string? countryCode,
        ISet<string> warnings,
        CancellationToken cancellationToken)
    {
        if (locationContext?.Latitude.HasValue == true
            && locationContext.Longitude.HasValue)
        {
            warnings.Add("places_query_shape:locality_bias_applied");
            return locationContext;
        }

        var locality = Normalize(locationContext?.TypedArea)
                       ?? Normalize(constraints.LocalityHint)
                       ?? Normalize(locationContext?.LocalityLabel);
        if (string.IsNullOrWhiteSpace(locality))
        {
            return locationContext;
        }

        var resolved = await localityResolutionService.ResolveAsync(
            locality,
            countryCode,
            languageCode: null,
            cancellationToken);
        if (!resolved.HasCoordinates || !resolved.Latitude.HasValue || !resolved.Longitude.HasValue)
        {
            warnings.Add("places_query_shape:locality_resolution_failed");
            return locationContext;
        }

        warnings.Add("places_query_shape:locality_resolution_succeeded");
        warnings.Add("places_query_shape:locality_bias_applied");
        return new PlaceSearchLocationContext(
            Source: "locality_resolution",
            Latitude: resolved.Latitude,
            Longitude: resolved.Longitude,
            RadiusMeters: locationContext?.RadiusMeters
                          ?? Math.Clamp(placesOptions.DefaultSearchRadiusMeters, 1_000, 15_000),
            TypedArea: locationContext?.TypedArea ?? locality,
            LocalityLabel: locationContext?.LocalityLabel ?? resolved.ResolvedLocalityLabel ?? locality,
            AccuracyBucket: locationContext?.AccuracyBucket,
            CapturedAtUtc: locationContext?.CapturedAtUtc);
    }

    private static CompanionPlaceDiscoveryRequest BuildDiscoveryRequest(
        string query,
        string? countryCode,
        PlaceSearchLocationContext? locationContext)
    {
        return new CompanionPlaceDiscoveryRequest(
            Query: query,
            CountryCode: countryCode,
            Latitude: locationContext?.Latitude,
            Longitude: locationContext?.Longitude,
            RadiusMeters: locationContext?.RadiusMeters);
    }

    private static string BuildFallbackQuery(
        string originalQuery,
        LocalDiscoveryConstraintExtractionResult constraints,
        PlaceSearchLocationContext? locationContext)
    {
        var locality = Normalize(locationContext?.TypedArea)
                       ?? Normalize(constraints.LocalityHint)
                       ?? Normalize(locationContext?.LocalityLabel);
        var canonicalType = ResolveFallbackType(constraints);
        var preferencePrefix = constraints.PreferenceHints.Any(value =>
            string.Equals(value, "dog_friendly", StringComparison.OrdinalIgnoreCase))
            ? "dog friendly "
            : string.Empty;
        var timeSuffix = constraints.TimeHints.Any(value =>
            string.Equals(value, "open_now", StringComparison.OrdinalIgnoreCase))
            ? " open now"
            : string.Empty;
        var fallback = $"{preferencePrefix}{canonicalType}{timeSuffix}".Trim();

        if (!string.IsNullOrWhiteSpace(locality))
        {
            fallback = $"{fallback} in {locality}";
        }
        else if (constraints.HasNearMeLanguage)
        {
            fallback = $"{fallback} nearby";
        }

        if (string.IsNullOrWhiteSpace(fallback))
        {
            fallback = originalQuery.Trim();
        }

        return fallback.Length <= 180
            ? fallback
            : fallback[..180].TrimEnd();
    }

    private static string ResolveFallbackType(LocalDiscoveryConstraintExtractionResult constraints)
    {
        if (constraints.PlaceTypeHints.Count > 0)
        {
            return constraints.PlaceTypeHints[0] switch
            {
                "tourist_attraction" => "tourist attractions",
                "movie_theater" => "cinema",
                "performing_arts_theater" => "theatre",
                "pet_friendly" => "pet friendly places",
                _ => constraints.PlaceTypeHints[0].Replace('_', ' ')
            };
        }

        if (constraints.AudienceHints.Any(value =>
                string.Equals(value, "kids", StringComparison.OrdinalIgnoreCase)))
        {
            return "playgrounds";
        }

        if (constraints.AudienceHints.Any(value =>
                string.Equals(value, "family", StringComparison.OrdinalIgnoreCase)))
        {
            return "family friendly attractions";
        }

        return "tourist attractions";
    }

    private static PlaceSearchLocationContext? BuildFallbackLocationContext(
        PlaceSearchLocationContext? currentContext,
        ISet<string> warnings)
    {
        if (currentContext?.Latitude.HasValue != true || !currentContext.Longitude.HasValue)
        {
            return currentContext;
        }

        var currentRadius = Math.Clamp(
            currentContext.RadiusMeters ?? 2_500,
            500,
            50_000);
        var widenedRadius = Math.Clamp((int)Math.Round(currentRadius * 1.8d), 1_500, 15_000);
        if (widenedRadius <= currentRadius)
        {
            return currentContext;
        }

        warnings.Add("places_query_shape:fallback_radius_widened");
        return currentContext with
        {
            RadiusMeters = widenedRadius
        };
    }

    private static void AppendQueryShapeWarnings(
        LocalDiscoveryShapedQueryResult shapedQuery,
        ISet<string> warnings)
    {
        foreach (var reasonCode in shapedQuery.ReasonCodes)
        {
            warnings.Add($"places_query_shape:{reasonCode}");
            switch (reasonCode)
            {
                case "local_discovery_query_locality_applied":
                    warnings.Add("places_query_shape:locality_bias_applied");
                    break;
                case "local_discovery_query_place_types_appended":
                case "local_discovery_query_default_type_appended":
                    warnings.Add("places_query_shape:type_hint_applied");
                    break;
                case "local_discovery_query_audience_appended":
                    warnings.Add("places_query_shape:audience_hint_applied");
                    break;
                case "local_discovery_query_preference_appended":
                    warnings.Add("places_query_shape:preference_hint_applied");
                    break;
            }
        }

        if (shapedQuery.Constraints.PlaceTypeHints.Count > 0)
        {
            warnings.Add("places_query_shape:type_hint_applied");
        }

        if (shapedQuery.Constraints.AudienceHints.Count > 0)
        {
            warnings.Add("places_query_shape:audience_hint_applied");
        }

        if (shapedQuery.Constraints.PreferenceHints.Count > 0)
        {
            warnings.Add("places_query_shape:preference_hint_applied");
        }
    }

    private PlaceSearchResult BuildSearchResult(
        CompanionPlaceDiscoveryResult result,
        ISet<string> warnings,
        CompanionPlaceRankingContext rankingContext)
    {
        var ranking = placeRankingPolicy.Rank(result.Candidates, rankingContext);
        warnings.UnionWith(ranking.Diagnostics);
        logger.LogInformation(
            "Companion places ranking applied={Applied} candidateCount={CandidateCount} diagnostics={Diagnostics}",
            rankingContext.ApplyDistanceRanking,
            ranking.RankedCandidates.Count,
            string.Join(',', ranking.Diagnostics));
        var items = ranking.RankedCandidates
            .Select(candidate => new PlaceSearchItem(
                PlaceId: candidate.PlaceId,
                Name: candidate.DisplayName,
                Category: candidate.PrimaryTypeDisplayName ?? candidate.PrimaryType,
                PriceLevel: candidate.PriceLevel,
                ResourceName: candidate.ResourceName,
                DisplayName: candidate.DisplayName,
                PrimaryType: candidate.PrimaryType,
                PrimaryTypeDisplayName: candidate.PrimaryTypeDisplayName,
                Types: candidate.Types,
                NationalPhoneNumber: candidate.NationalPhoneNumber,
                FormattedAddress: candidate.FormattedAddress,
                ShortFormattedAddress: candidate.ShortFormattedAddress,
                Rating: candidate.Rating,
                UserRatingCount: candidate.UserRatingCount,
                GoogleMapsUri: candidate.GoogleMapsUri,
                WebsiteUri: candidate.WebsiteUri,
                OpeningHours: candidate.OpeningHours,
                BusinessStatus: candidate.BusinessStatus,
                IconMaskBaseUri: candidate.IconMaskBaseUri,
                IconBackgroundColor: candidate.IconBackgroundColor,
                Takeout: candidate.Takeout,
                Delivery: candidate.Delivery,
                DineIn: candidate.DineIn,
                Reservable: candidate.Reservable,
                ServesBreakfast: candidate.ServesBreakfast,
                ServesLunch: candidate.ServesLunch,
                ServesDinner: candidate.ServesDinner,
                ServesBeer: candidate.ServesBeer,
                ServesWine: candidate.ServesWine,
                ServesBrunch: candidate.ServesBrunch,
                ServesVegetarianFood: candidate.ServesVegetarianFood,
                OutdoorSeating: candidate.OutdoorSeating,
                LiveMusic: candidate.LiveMusic,
                MenuForChildren: candidate.MenuForChildren,
                ServesCocktails: candidate.ServesCocktails,
                ServesDessert: candidate.ServesDessert,
                ServesCoffee: candidate.ServesCoffee,
                AllowsDogs: candidate.AllowsDogs,
                Restroom: candidate.Restroom,
                GoodForGroups: candidate.GoodForGroups,
                GoodForWatchingSports: candidate.GoodForWatchingSports,
                PaymentOptions: candidate.PaymentOptions,
                AccessibilityOptions: candidate.AccessibilityOptions,
                EditorialSummary: candidate.EditorialSummary,
                Location: candidate.Location))
            .ToArray();

        return new PlaceSearchResult(
            Items: items,
            Metadata: result.Metadata with
            {
                ReturnedCandidateCount = items.Length
            },
            Warnings: warnings.ToArray());
    }

    private static CompanionPlaceRankingContext BuildRankingContext(
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult constraints)
    {
        var isGpsGrounded = string.Equals(locationContext?.Source, "gps", StringComparison.OrdinalIgnoreCase)
                            && locationContext?.Latitude.HasValue == true
                            && locationContext?.Longitude.HasValue == true;
        var applyDistanceRanking = isGpsGrounded && constraints.HasNearMeLanguage;

        return new CompanionPlaceRankingContext(
            ApplyDistanceRanking: applyDistanceRanking,
            UserLatitude: locationContext?.Latitude,
            UserLongitude: locationContext?.Longitude,
            PlaceTypeHints: constraints.PlaceTypeHints);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed class GooglePlacesPlaceDetailsService(
    IGooglePlacesClient placesClient,
    IGooglePlacesFieldMaskProvider fieldMaskProvider,
    IGooglePlacesCache cache,
    IGooglePlacesCacheKeyBuilder cacheKeyBuilder,
    IOptions<GooglePlacesOptions> options,
    ILogger<GooglePlacesPlaceDetailsService> logger) : IPlaceDetailsService
{
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<PlaceDetailsResult> GetDetailsAsync(
        string placeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(placeId) || !placesOptions.Enabled)
        {
            return new PlaceDetailsResult(
                PlaceId: placeId,
                Name: string.Empty,
                Address: null,
                Website: null,
                PriceLevel: null);
        }

        var nowUtc = DateTime.UtcNow;
        var cacheKey = cacheKeyBuilder.BuildPlaceDetailsKey(placeId);
        if (cache.TryGet(cacheKey, nowUtc, out PlaceDetailsResult cached))
        {
            logger.LogInformation(
                "Google Places place details cache hit keyHash={KeyHash}",
                cacheKey);
            return cached;
        }

        var providerResult = await placesClient.GetPlaceDetailsAsync(
            placeId,
            fieldMaskProvider.PlaceDetailsMask,
            useCaseTag: "place_details",
            cancellationToken);
        if (!providerResult.Succeeded || providerResult.Value is null)
        {
            var failed = new PlaceDetailsResult(
                PlaceId: placeId,
                Name: string.Empty,
                Address: null,
                Website: null,
                PriceLevel: null);
            cache.Set(
                cacheKey,
                failed,
                nowUtc,
                TimeSpan.FromSeconds(Math.Max(1, placesOptions.FailureCacheTtlSeconds)));
            logger.LogWarning(
                "Google Places place details unavailable placeId={PlaceId} providerError={ProviderErrorCode}",
                placeId,
                providerResult.ErrorCode ?? "unknown");
            return failed;
        }

        var place = providerResult.Value;
        var success = new PlaceDetailsResult(
            PlaceId: place.PlaceId,
            Name: place.DisplayName,
            Address: place.FormattedAddress ?? place.ShortFormattedAddress,
            Website: place.WebsiteUri,
            PriceLevel: place.PriceLevel,
            NationalPhoneNumber: place.NationalPhoneNumber,
            GoogleMapsUri: place.GoogleMapsUri,
            BusinessStatus: place.BusinessStatus,
            Rating: place.Rating,
            UserRatingCount: place.UserRatingCount,
            PrimaryType: place.PrimaryType,
            PrimaryTypeDisplayName: place.PrimaryTypeDisplayName,
            Types: place.Types,
            OpeningHours: place.OpeningHours,
            PaymentOptions: place.PaymentOptions,
            AccessibilityOptions: place.AccessibilityOptions,
            EditorialSummary: place.EditorialSummary,
            Location: place.Location);
        cache.Set(
            cacheKey,
            success,
            nowUtc,
            TimeSpan.FromSeconds(Math.Max(1, placesOptions.PlaceDetailsCacheTtlSeconds)));

        return success;
    }
}
