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
    ICompanionPlaceDiscoveryService discoveryService) : IPlacesSearchService
{
    public async Task<PlaceSearchResult> SearchAsync(
        string query,
        string country,
        CancellationToken cancellationToken)
    {
        var result = await discoveryService.DiscoverAsync(
            new CompanionPlaceDiscoveryRequest(
                Query: query,
                CountryCode: country),
            cancellationToken);

        var items = result.Candidates
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
            Metadata: result.Metadata,
            Warnings: result.Warnings);
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
