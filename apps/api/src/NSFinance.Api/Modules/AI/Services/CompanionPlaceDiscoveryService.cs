using System.Text.RegularExpressions;
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
    private const string NearbyDiscoveryUseCase = "companion_discovery_nearby";
    private const string CompanionNearbyFieldMaskVariant = "companion_nearby_v1";
    private readonly GooglePlacesOptions placesOptions = options.Value;

    public async Task<CompanionPlaceDiscoveryResult> DiscoverAsync(
        CompanionPlaceDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var defaultBudget = Math.Clamp(placesOptions.MaxCompanionCandidates, 1, 8);
        var maxCandidates = Math.Clamp(request.MaxCandidates ?? defaultBudget, 1, 16);
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

    public async Task<CompanionPlaceDiscoveryResult> DiscoverNearbyAsync(
        CompanionNearbyDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var maxCandidates = Math.Clamp(
            request.MaxCandidates ?? Math.Max(4, placesOptions.MaxCompanionCandidates),
            1,
            16);
        var includedTypes = request.IncludedTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        if (includedTypes.Length == 0)
        {
            return BuildNearbyResult(
                succeeded: false,
                fromCache: false,
                requestedCount: maxCandidates,
                candidates: [],
                elapsed: TimeSpan.Zero,
                timedOut: false,
                providerErrorCode: "nearby_empty_types",
                warnings: ["nearby_empty_types"]);
        }

        var radiusMeters = Math.Clamp(
            request.RadiusMeters,
            200,
            25_000);
        var cacheKey = cacheKeyBuilder.BuildCompanionNearbyKey(
            request with { IncludedTypes = includedTypes, RadiusMeters = radiusMeters },
            maxCandidates);
        if (cache.TryGet(cacheKey, nowUtc, out CompanionPlaceDiscoveryResult cached))
        {
            logger.LogInformation(
                "Google Places nearby discovery cache hit queryHash={QueryHash} requested={Requested} returned={Returned}",
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

        var providerResult = await placesClient.SearchNearbyAsync(
            new GooglePlacesSearchNearbyRequest(
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                RadiusMeters: radiusMeters,
                IncludedTypes: includedTypes,
                MaxResultCount: maxCandidates,
                RegionCode: request.CountryCode,
                LanguageCode: request.LanguageCode,
                FieldMask: fieldMaskProvider.CompanionNearbySearchMask,
                UseCaseTag: NearbyDiscoveryUseCase),
            cancellationToken);
        if (!providerResult.Succeeded)
        {
            var failureWarnings = new List<string>(2)
            {
                "nearby_provider_unavailable"
            };
            if (providerResult.TimedOut)
            {
                failureWarnings.Add("nearby_timeout");
            }

            var failed = BuildNearbyResult(
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
            return failed;
        }

        var candidates = (providerResult.Value ?? [])
            .Take(maxCandidates)
            .Select(MapToCandidate)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.PlaceId))
            .ToArray();
        var success = BuildNearbyResult(
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
            "Google Places nearby discovery success requested={Requested} returned={Returned} elapsedMs={ElapsedMs} fieldMaskVariant={FieldMaskVariant}",
            maxCandidates,
            candidates.Length,
            providerResult.Elapsed.TotalMilliseconds,
            CompanionNearbyFieldMaskVariant);
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

    private static CompanionPlaceDiscoveryResult BuildNearbyResult(
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
                UseCase: NearbyDiscoveryUseCase,
                FromCache: fromCache,
                RequestedCandidateCount: requestedCount,
                ReturnedCandidateCount: candidates.Count,
                FieldMaskVariant: CompanionNearbyFieldMaskVariant,
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
    ILocalDiscoveryConstraintExtractor localDiscoveryConstraintExtractor,
    ICompanionPlacesTextQueryBuilder textQueryBuilder,
    ICompanionPlacesNearbyRequestBuilder nearbyRequestBuilder,
    ICompanionLocalityResolutionService localityResolutionService,
    ICompanionNearbyTypeMapper nearbyTypeMapper,
    ICompanionNearbyHybridRetrievalPolicy hybridRetrievalPolicy,
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
        if (locationContext?.PlannerAuthoritative == true)
        {
            warnings.Add("real_world_retrieval_plan_authoritative:true");
        }

        if (locationContext?.PlannerSelectedDomain is not null)
        {
            warnings.Add($"real_world_retrieval_plan_domain:{locationContext.PlannerSelectedDomain.Value.ToString().ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(locationContext?.PlannerSelectedConcept))
        {
            warnings.Add($"real_world_retrieval_plan_concept:{locationContext.PlannerSelectedConcept!.Trim().ToLowerInvariant()}");
        }

        warnings.Add($"real_world_retrieval_plan_near_me:{(locationContext?.HasNearMeSemantic == true).ToString().ToLowerInvariant()}");
        var extractedConstraints = localDiscoveryConstraintExtractor.Extract(query);
        var constraints = ApplyPlannerAuthoritativeOverrides(extractedConstraints, locationContext, warnings);
        AppendConstraintWarnings(constraints, warnings);
        logger.LogInformation(
            "Companion places query shaped localDiscoveryCandidate={IsCandidate} confidence={Confidence} hasNearMeLanguage={HasNearMeLanguage} hasExplicitLocality={HasExplicitLocality} reasonCodes={ReasonCodes}",
            constraints.IsLocalDiscoveryCandidate,
            constraints.Confidence,
            constraints.HasNearMeLanguage,
            constraints.HasExplicitLocality,
            string.Join(',', constraints.ReasonCodes));

        var effectiveLocationContext = await ResolveLocationBiasAsync(
            locationContext,
            constraints,
            country,
            warnings,
            cancellationToken);
        var rankingContext = BuildRankingContext(effectiveLocationContext, constraints);
        var hybridDecision = hybridRetrievalPolicy.Decide(locationContext, constraints);
        warnings.Add(hybridDecision.ReasonCode);
        var retrievalCandidateBudget = ResolveRetrievalCandidateBudget(constraints, hybridDecision.UseHybridRetrieval);
        warnings.Add($"places_retrieval:candidate_budget:{retrievalCandidateBudget}");
        if (effectiveLocationContext?.ImplicitLocalBias == true)
        {
            warnings.Add("real_world_commerce_local_bias_enabled");
        }

        var primaryQueryBuild = textQueryBuilder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: query,
                Constraints: constraints,
                LocationContext: effectiveLocationContext,
                IsGpsNearMe: hybridDecision.UseHybridRetrieval));
        warnings.UnionWith(primaryQueryBuild.ReasonCodes);
        if (!primaryQueryBuild.Succeeded || string.IsNullOrWhiteSpace(primaryQueryBuild.Query))
        {
            warnings.Add("places_request:provider_branch_text_skipped_invalid_query");
            return BuildSearchResult(
                BuildRequestConstructionFailure(
                    failureCode: primaryQueryBuild.FailureReason ?? "text_query_preflight_failed"),
                warnings,
                rankingContext);
        }

        var primaryRequest = BuildDiscoveryRequest(
            query: primaryQueryBuild.Query,
            countryCode: country,
            locationContext: effectiveLocationContext,
            maxCandidates: retrievalCandidateBudget,
            warnings: warnings);
        logger.LogInformation(
            "Companion places primary search query={Query} hasCoordinates={HasCoordinates} radiusMeters={RadiusMeters} country={CountryCode}",
            primaryRequest.Query,
            primaryRequest.Latitude.HasValue && primaryRequest.Longitude.HasValue,
            primaryRequest.RadiusMeters ?? 0,
            primaryRequest.CountryCode ?? string.Empty);
        warnings.Add("places_retrieval:text_search_used");
        var primaryResult = await discoveryService.DiscoverAsync(
            primaryRequest,
            cancellationToken);
        warnings.UnionWith(primaryResult.Warnings ?? []);
        warnings.Add($"places_retrieval:text_search_candidate_count:{primaryResult.Candidates.Count}");

        if (hybridDecision.UseHybridRetrieval)
        {
            var nearbyTypeMapping = nearbyTypeMapper.Map(query, constraints, effectiveLocationContext);
            foreach (var reasonCode in nearbyTypeMapping.ReasonCodes)
            {
                warnings.Add($"places_retrieval:{reasonCode}");
            }

            CompanionPlaceDiscoveryResult mergedResult;
            if (nearbyTypeMapping.IncludedTypes.Count == 0)
            {
                warnings.Add("places_retrieval:nearby_search_skipped_no_type_mapping");
                mergedResult = primaryResult;
            }
            else
            {
                var nearbyRequestBuild = nearbyRequestBuilder.Build(
                    new CompanionPlacesNearbyRequestBuildRequest(
                        CountryCode: country,
                        LocationContext: effectiveLocationContext,
                        IncludedTypes: nearbyTypeMapping.IncludedTypes,
                        MaxCandidates: Math.Clamp(retrievalCandidateBudget, 4, 16),
                        DefaultRadiusMeters: placesOptions.DefaultSearchRadiusMeters));
                warnings.UnionWith(nearbyRequestBuild.ReasonCodes);
                if (!nearbyRequestBuild.Succeeded || nearbyRequestBuild.Request is null)
                {
                    warnings.Add("places_retrieval:nearby_search_skipped_preflight_failed");
                    mergedResult = primaryResult;
                }
                else
                {
                    warnings.Add("places_retrieval:nearby_search_used");
                    var nearbyRequest = nearbyRequestBuild.Request;
                    logger.LogInformation(
                        "Companion places nearby search includedTypes={IncludedTypes} radiusMeters={RadiusMeters} country={CountryCode}",
                        string.Join(',', nearbyRequest.IncludedTypes),
                        nearbyRequest.RadiusMeters,
                        nearbyRequest.CountryCode ?? string.Empty);
                    var nearbyResult = await discoveryService.DiscoverNearbyAsync(
                        nearbyRequest,
                        cancellationToken);
                    warnings.UnionWith(nearbyResult.Warnings ?? []);
                    warnings.Add($"places_retrieval:nearby_search_candidate_count:{nearbyResult.Candidates.Count}");
                    mergedResult = MergeHybridResults(primaryResult, nearbyResult, warnings);
                }
            }

            if (mergedResult.Succeeded && mergedResult.Candidates.Count > 0)
            {
                warnings.Add("places_query_shape:results_found_primary");
            }
            else if (!mergedResult.Succeeded)
            {
                warnings.Add("places_query_shape:primary_search_failed");
                AppendProviderFailureWarnings(mergedResult, warnings);
            }
            else
            {
                warnings.Add("places_query_shape:no_results_primary");
            }

            return BuildSearchResult(mergedResult, warnings, rankingContext);
        }

        if (primaryResult.Succeeded && primaryResult.Candidates.Count > 0)
        {
            warnings.Add("places_query_shape:results_found_primary");
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        if (!primaryResult.Succeeded)
        {
            warnings.Add("places_query_shape:primary_search_failed");
            AppendProviderFailureWarnings(primaryResult, warnings);
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        warnings.Add("places_query_shape:no_results_primary");
        var shouldBroadenFromBrand = string.Equals(
                                         effectiveLocationContext?.SearchScope,
                                         "brand_first",
                                         StringComparison.OrdinalIgnoreCase)
                                     && !string.IsNullOrWhiteSpace(effectiveLocationContext?.PlannerCanonicalConcept);
        var fallbackContext = shouldBroadenFromBrand && effectiveLocationContext is not null
            ? effectiveLocationContext with
            {
                PlannerBrandTerm = null,
                SearchScope = "concept_broadened"
            }
            : effectiveLocationContext;
        var fallbackUserQuery = shouldBroadenFromBrand
            ? effectiveLocationContext?.PlannerCanonicalConcept ?? query
            : query;
        if (shouldBroadenFromBrand)
        {
            warnings.Add("places_query_shape:brand_first_broaden_to_concept");
        }

        var fallbackQueryBuild = textQueryBuilder.Build(
            new CompanionPlacesTextQueryBuildRequest(
                UserQuery: fallbackUserQuery,
                Constraints: constraints,
                LocationContext: fallbackContext,
                IsGpsNearMe: false,
                ForceSimplifiedFallback: true));
        warnings.UnionWith(fallbackQueryBuild.ReasonCodes);
        if (!fallbackQueryBuild.Succeeded || string.IsNullOrWhiteSpace(fallbackQueryBuild.Query))
        {
            warnings.Add("places_query_shape:no_results_fallback");
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        var fallbackQuery = fallbackQueryBuild.Query;
        if (string.IsNullOrWhiteSpace(fallbackQuery))
        {
            warnings.Add("places_query_shape:no_results_fallback");
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        warnings.Add("places_query_shape:fallback_text_search");
        var fallbackLocationContext = BuildFallbackLocationContext(
            fallbackContext,
            warnings);
        var fallbackRequest = BuildDiscoveryRequest(
            query: fallbackQuery,
            countryCode: country,
            locationContext: fallbackLocationContext,
            maxCandidates: retrievalCandidateBudget,
            warnings: warnings);
        logger.LogInformation(
            "Companion places fallback search query={Query} hasCoordinates={HasCoordinates} radiusMeters={RadiusMeters} country={CountryCode}",
            fallbackRequest.Query,
            fallbackRequest.Latitude.HasValue && fallbackRequest.Longitude.HasValue,
            fallbackRequest.RadiusMeters ?? 0,
            fallbackRequest.CountryCode ?? string.Empty);

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
            AppendProviderFailureWarnings(fallbackResult, warnings);
            return BuildSearchResult(primaryResult, warnings, rankingContext);
        }

        warnings.Add("places_query_shape:no_results_fallback");
        return BuildSearchResult(fallbackResult, warnings, rankingContext);
    }

    private static LocalDiscoveryConstraintExtractionResult ApplyPlannerAuthoritativeOverrides(
        LocalDiscoveryConstraintExtractionResult constraints,
        PlaceSearchLocationContext? locationContext,
        ISet<string> warnings)
    {
        if (locationContext?.PlannerAuthoritative != true)
        {
            return constraints;
        }

        var placeTypeHints = constraints.PlaceTypeHints.ToList();
        var appliedConceptHint = false;
        if (locationContext.PlannerIncludeTypes is { Count: > 0 })
        {
            placeTypeHints = locationContext.PlannerIncludeTypes
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            warnings.Add("real_world_retrieval_planner_include_types_applied");
        }
        else if (!string.IsNullOrWhiteSpace(locationContext.PlannerSelectedConcept))
        {
            var mappedFromConcept = MapConceptToPlaceTypeHint(locationContext.PlannerSelectedConcept!);
            if (!string.IsNullOrWhiteSpace(mappedFromConcept))
            {
                placeTypeHints.Insert(0, mappedFromConcept!);
                placeTypeHints = placeTypeHints
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                warnings.Add("real_world_retrieval_planner_concept_hint_applied");
                appliedConceptHint = true;
            }
        }

        if (!appliedConceptHint && locationContext.PlannerSelectedDomain is { } selectedDomain)
        {
            var mappedType = MapDomainToPlaceTypeHint(selectedDomain);
            if (!string.IsNullOrWhiteSpace(mappedType))
            {
                placeTypeHints.Insert(0, mappedType!);
                placeTypeHints = placeTypeHints
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                warnings.Add("real_world_retrieval_planner_type_hint_applied");
            }
        }

        if (locationContext.PlannerExcludeTypes is { Count: > 0 })
        {
            var excludeSet = locationContext.PlannerExcludeTypes
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (excludeSet.Count > 0)
            {
                placeTypeHints = placeTypeHints
                    .Where(type => !excludeSet.Contains(type))
                    .ToList();
                warnings.Add("real_world_retrieval_planner_exclude_types_applied");
            }
        }

        warnings.Add("real_world_retrieval_planner_constraints_applied");
        return constraints with
        {
            IsLocalDiscoveryCandidate = true,
            Confidence = Math.Max(constraints.Confidence, 0.95d),
            HasNearMeLanguage = constraints.HasNearMeLanguage || locationContext.HasNearMeSemantic,
            HasExplicitLocality = constraints.HasExplicitLocality || !string.IsNullOrWhiteSpace(locationContext.TypedArea),
            LocalityHint = !string.IsNullOrWhiteSpace(locationContext.TypedArea)
                ? locationContext.TypedArea
                : constraints.LocalityHint,
            PlaceTypeHints = placeTypeHints
        };
    }

    private static string? MapConceptToPlaceTypeHint(string concept)
    {
        if (string.IsNullOrWhiteSpace(concept))
        {
            return null;
        }

        var normalized = concept.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "movietheater" => "movie_theater",
            "movietheatre" => "movie_theater",
            "cafe" or "coffee_shop" => "cafe",
            "restaurant" or "takeaway" => "restaurant",
            "bar" or "pub" => "bar",
            "movie_theater" or "cinema" => "movie_theater",
            "park" => "park",
            "playground" => "playground",
            "pharmacy" => "pharmacy",
            "gas_station" or "petrol_station" => "gas_station",
            "gym" => "gym",
            "electronics_store" => "electronics_store",
            "convenience_store" => "convenience_store",
            "grocery_store" or "supermarket" => "grocery_store",
            "parking" or "car_park" => "parking",
            "post_office" => "post_office",
            _ => null
        };
    }

    private static string? MapDomainToPlaceTypeHint(RealWorldDiscoveryDomain domain)
    {
        return domain switch
        {
            RealWorldDiscoveryDomain.Cafe => "cafe",
            RealWorldDiscoveryDomain.Restaurant => "restaurant",
            RealWorldDiscoveryDomain.Takeaway => "restaurant",
            RealWorldDiscoveryDomain.PubBar => "bar",
            RealWorldDiscoveryDomain.MovieTheater => "movie_theater",
            RealWorldDiscoveryDomain.ParkWalk => "park",
            RealWorldDiscoveryDomain.Playground => "playground",
            RealWorldDiscoveryDomain.Pharmacy => "pharmacy",
            RealWorldDiscoveryDomain.PetrolStation => "gas_station",
            RealWorldDiscoveryDomain.Gym => "gym",
            RealWorldDiscoveryDomain.ElectronicsRetail => "electronics_store",
            RealWorldDiscoveryDomain.ConvenienceStore => "convenience_store",
            RealWorldDiscoveryDomain.Grocery => "grocery_store",
            RealWorldDiscoveryDomain.OutdoorActivity => "tourist_attraction",
            RealWorldDiscoveryDomain.EntertainmentGeneral => "tourist_attraction",
            RealWorldDiscoveryDomain.NightlifeGeneral => "bar",
            RealWorldDiscoveryDomain.FoodDrinkGeneral => "restaurant",
            RealWorldDiscoveryDomain.ServiceGeneral => null,
            RealWorldDiscoveryDomain.CommerceGeneral => "store",
            RealWorldDiscoveryDomain.ExploratoryEveningActivity => "tourist_attraction",
            RealWorldDiscoveryDomain.ExploratoryFamilyActivity => "tourist_attraction",
            _ => null
        };
    }

    private static CompanionPlaceDiscoveryResult BuildRequestConstructionFailure(string failureCode)
    {
        return new CompanionPlaceDiscoveryResult(
            Succeeded: false,
            Candidates: [],
            Metadata: new PlaceSearchMetadata(
                UseCase: "companion_discovery",
                FromCache: false,
                RequestedCandidateCount: 0,
                ReturnedCandidateCount: 0,
                FieldMaskVariant: "companion_discovery_v1",
                Elapsed: TimeSpan.Zero,
                TimedOut: false,
                ProviderErrorCode: failureCode),
            Warnings:
            [
                "places_provider_request_construction_failed",
                $"places_provider_request_failure:{failureCode}"
            ]);
    }

    private static void AppendProviderFailureWarnings(
        CompanionPlaceDiscoveryResult result,
        ISet<string> warnings)
    {
        if (result.Succeeded)
        {
            return;
        }

        var providerErrorCode = result.Metadata.ProviderErrorCode;
        if (string.IsNullOrWhiteSpace(providerErrorCode))
        {
            return;
        }

        warnings.Add($"places_provider_error:{providerErrorCode.ToLowerInvariant()}");
        if (string.Equals(
                providerErrorCode,
                "INVALID_ARGUMENT",
                StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("places_provider_request_rejected_invalid_argument");
        }
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
        if (locationContext is not null)
        {
            return locationContext with
            {
                Source = "locality_resolution",
                Latitude = resolved.Latitude,
                Longitude = resolved.Longitude,
                RadiusMeters = locationContext.RadiusMeters
                              ?? Math.Clamp(placesOptions.DefaultSearchRadiusMeters, 1_000, 15_000),
                TypedArea = locationContext.TypedArea ?? locality,
                LocalityLabel = locationContext.LocalityLabel ?? resolved.ResolvedLocalityLabel ?? locality
            };
        }

        return new PlaceSearchLocationContext(
            Source: "locality_resolution",
            Latitude: resolved.Latitude,
            Longitude: resolved.Longitude,
            RadiusMeters: Math.Clamp(placesOptions.DefaultSearchRadiusMeters, 1_000, 15_000),
            TypedArea: locality,
            LocalityLabel: resolved.ResolvedLocalityLabel ?? locality);
    }

    private static CompanionPlaceDiscoveryRequest BuildDiscoveryRequest(
        string query,
        string? countryCode,
        PlaceSearchLocationContext? locationContext,
        int? maxCandidates,
        ISet<string> warnings)
    {
        var normalizedCountryCode = NormalizeCountryCode(countryCode);
        if (!string.IsNullOrWhiteSpace(countryCode) && normalizedCountryCode is null)
        {
            warnings.Add("places_request:region_code_simplified_invalid_country_code");
        }

        return new CompanionPlaceDiscoveryRequest(
            Query: query,
            CountryCode: normalizedCountryCode,
            Latitude: locationContext?.Latitude,
            Longitude: locationContext?.Longitude,
            RadiusMeters: locationContext?.RadiusMeters,
            MaxCandidates: maxCandidates);
    }

    private static string? NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        var normalized = countryCode.Trim().ToUpperInvariant();
        if (normalized == "ZZ")
        {
            return null;
        }

        return Regex.IsMatch(normalized, "^[A-Z]{2}$", RegexOptions.CultureInvariant)
            ? normalized
            : null;
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

    private static void AppendConstraintWarnings(
        LocalDiscoveryConstraintExtractionResult constraints,
        ISet<string> warnings)
    {
        foreach (var reasonCode in constraints.ReasonCodes)
        {
            warnings.Add($"places_query_shape:{reasonCode}");
        }

        if (constraints.PlaceTypeHints.Count > 0)
        {
            warnings.Add("places_query_shape:type_hint_applied");
        }

        if (constraints.AudienceHints.Count > 0)
        {
            warnings.Add("places_query_shape:audience_hint_applied");
        }

        if (constraints.PreferenceHints.Count > 0)
        {
            warnings.Add("places_query_shape:preference_hint_applied");
        }

        if (constraints.HasExplicitLocality)
        {
            warnings.Add("places_query_shape:locality_hint_present");
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
        var visibleTarget = Math.Clamp(placesOptions.MaxCompanionCandidates, 1, 8);
        var maxFinalItems = Math.Clamp(visibleTarget * 3, 8, 24);
        warnings.Add($"places_retrieval:internal_pool_cap:{maxFinalItems}");
        var items = ranking.RankedCandidates
            .Take(maxFinalItems)
            .Select(candidate => new PlaceSearchItem(
                PlaceId: candidate.PlaceId,
                Name: candidate.DisplayName,
                Category: ResolveCategoryFromPlaces(candidate),
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

    private int ResolveRetrievalCandidateBudget(
        LocalDiscoveryConstraintExtractionResult constraints,
        bool useHybridRetrieval)
    {
        var visibleTarget = Math.Clamp(placesOptions.MaxCompanionCandidates, 1, 8);
        if (constraints.PlaceTypeHints.Count == 0)
        {
            return visibleTarget;
        }

        var multiplier = useHybridRetrieval ? 2 : 1;
        return Math.Clamp(visibleTarget * multiplier, visibleTarget, 16);
    }

    private static CompanionPlaceRankingContext BuildRankingContext(
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult constraints)
    {
        var isGpsGrounded = string.Equals(locationContext?.Source, "gps", StringComparison.OrdinalIgnoreCase)
                            && locationContext?.Latitude.HasValue == true
                            && locationContext?.Longitude.HasValue == true;
        var applyDistanceRanking = isGpsGrounded
                                   && (constraints.HasNearMeLanguage
                                       || locationContext?.HasNearMeSemantic == true
                                       || locationContext?.ImplicitLocalBias == true);

        return new CompanionPlaceRankingContext(
            ApplyDistanceRanking: applyDistanceRanking,
            UserLatitude: locationContext?.Latitude,
            UserLongitude: locationContext?.Longitude,
            PlaceTypeHints: constraints.PlaceTypeHints,
            BrandTerm: locationContext?.PlannerBrandTerm,
            CanonicalConcept: locationContext?.PlannerCanonicalConcept,
            ExcludedTypeHints: locationContext?.PlannerExcludeTypes ?? [],
            PreferenceHints: constraints.PreferenceHints,
            TimeHints: constraints.TimeHints);
    }

    private static CompanionPlaceDiscoveryResult MergeHybridResults(
        CompanionPlaceDiscoveryResult textResult,
        CompanionPlaceDiscoveryResult nearbyResult,
        ISet<string> warnings)
    {
        warnings.Add("places_retrieval:hybrid_merge_applied");
        var dedupe = new Dictionary<string, CompanionPlaceCandidate>(StringComparer.OrdinalIgnoreCase);
        var overlapCount = 0;
        foreach (var candidate in textResult.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.PlaceId))
            {
                continue;
            }

            dedupe[candidate.PlaceId] = candidate;
        }

        foreach (var candidate in nearbyResult.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.PlaceId))
            {
                continue;
            }

            if (dedupe.TryGetValue(candidate.PlaceId, out var existing))
            {
                overlapCount += 1;
                dedupe[candidate.PlaceId] = MergeCandidate(existing, candidate);
            }
            else
            {
                dedupe[candidate.PlaceId] = candidate;
            }
        }

        warnings.Add($"places_retrieval:deduped_overlap_count:{overlapCount}");
        warnings.Add($"places_retrieval:merged_candidate_count:{dedupe.Count}");
        var mergedCandidates = dedupe.Values.ToArray();
        var mergedSucceeded = textResult.Succeeded || nearbyResult.Succeeded;
        var baseMetadata = textResult.Succeeded
            ? textResult.Metadata
            : nearbyResult.Metadata;
        var providerErrorCode = mergedSucceeded
            ? null
            : textResult.Metadata.ProviderErrorCode ?? nearbyResult.Metadata.ProviderErrorCode;

        return new CompanionPlaceDiscoveryResult(
            Succeeded: mergedSucceeded,
            Candidates: mergedCandidates,
            Metadata: baseMetadata with
            {
                UseCase = "companion_hybrid",
                FieldMaskVariant = "companion_hybrid_v1",
                RequestedCandidateCount = Math.Max(
                    textResult.Metadata.RequestedCandidateCount,
                    nearbyResult.Metadata.RequestedCandidateCount),
                ReturnedCandidateCount = mergedCandidates.Length,
                TimedOut = textResult.Metadata.TimedOut && nearbyResult.Metadata.TimedOut,
                ProviderErrorCode = providerErrorCode
            },
            Warnings: textResult.Warnings
                .Concat(nearbyResult.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static CompanionPlaceCandidate MergeCandidate(
        CompanionPlaceCandidate primary,
        CompanionPlaceCandidate secondary)
    {
        return primary with
        {
            ResourceName = Pick(primary.ResourceName, secondary.ResourceName) ?? primary.ResourceName,
            DisplayName = Pick(primary.DisplayName, secondary.DisplayName) ?? primary.DisplayName,
            PrimaryType = Pick(primary.PrimaryType, secondary.PrimaryType),
            PrimaryTypeDisplayName = Pick(primary.PrimaryTypeDisplayName, secondary.PrimaryTypeDisplayName),
            Types = Merge(primary.Types, secondary.Types),
            NationalPhoneNumber = Pick(primary.NationalPhoneNumber, secondary.NationalPhoneNumber),
            FormattedAddress = Pick(primary.FormattedAddress, secondary.FormattedAddress),
            ShortFormattedAddress = Pick(primary.ShortFormattedAddress, secondary.ShortFormattedAddress),
            Rating = primary.Rating ?? secondary.Rating,
            UserRatingCount = primary.UserRatingCount ?? secondary.UserRatingCount,
            GoogleMapsUri = Pick(primary.GoogleMapsUri, secondary.GoogleMapsUri),
            WebsiteUri = Pick(primary.WebsiteUri, secondary.WebsiteUri),
            BusinessStatus = Pick(primary.BusinessStatus, secondary.BusinessStatus),
            PriceLevel = Pick(primary.PriceLevel, secondary.PriceLevel),
            IconMaskBaseUri = Pick(primary.IconMaskBaseUri, secondary.IconMaskBaseUri),
            IconBackgroundColor = Pick(primary.IconBackgroundColor, secondary.IconBackgroundColor),
            Takeout = primary.Takeout ?? secondary.Takeout,
            Delivery = primary.Delivery ?? secondary.Delivery,
            DineIn = primary.DineIn ?? secondary.DineIn,
            Reservable = primary.Reservable ?? secondary.Reservable,
            ServesBreakfast = primary.ServesBreakfast ?? secondary.ServesBreakfast,
            ServesLunch = primary.ServesLunch ?? secondary.ServesLunch,
            ServesDinner = primary.ServesDinner ?? secondary.ServesDinner,
            ServesBeer = primary.ServesBeer ?? secondary.ServesBeer,
            ServesWine = primary.ServesWine ?? secondary.ServesWine,
            ServesBrunch = primary.ServesBrunch ?? secondary.ServesBrunch,
            ServesVegetarianFood = primary.ServesVegetarianFood ?? secondary.ServesVegetarianFood,
            OutdoorSeating = primary.OutdoorSeating ?? secondary.OutdoorSeating,
            LiveMusic = primary.LiveMusic ?? secondary.LiveMusic,
            MenuForChildren = primary.MenuForChildren ?? secondary.MenuForChildren,
            ServesCocktails = primary.ServesCocktails ?? secondary.ServesCocktails,
            ServesDessert = primary.ServesDessert ?? secondary.ServesDessert,
            ServesCoffee = primary.ServesCoffee ?? secondary.ServesCoffee,
            AllowsDogs = primary.AllowsDogs ?? secondary.AllowsDogs,
            Restroom = primary.Restroom ?? secondary.Restroom,
            GoodForGroups = primary.GoodForGroups ?? secondary.GoodForGroups,
            GoodForWatchingSports = primary.GoodForWatchingSports ?? secondary.GoodForWatchingSports,
            PaymentOptions = Merge(primary.PaymentOptions, secondary.PaymentOptions),
            AccessibilityOptions = Merge(primary.AccessibilityOptions, secondary.AccessibilityOptions),
            EditorialSummary = Merge(primary.EditorialSummary, secondary.EditorialSummary),
            Location = primary.Location ?? secondary.Location
        };
    }

    private static string? Pick(string? first, string? second)
    {
        return string.IsNullOrWhiteSpace(first) ? second : first;
    }

    private static IReadOnlyList<string> Merge(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second)
    {
        return first
            .Concat(second)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PlacePaymentOptionsSummary Merge(
        PlacePaymentOptionsSummary first,
        PlacePaymentOptionsSummary second)
    {
        return new PlacePaymentOptionsSummary(
            AcceptsCreditCards: first.AcceptsCreditCards ?? second.AcceptsCreditCards,
            AcceptsDebitCards: first.AcceptsDebitCards ?? second.AcceptsDebitCards,
            AcceptsCashOnly: first.AcceptsCashOnly ?? second.AcceptsCashOnly,
            AcceptsNfc: first.AcceptsNfc ?? second.AcceptsNfc);
    }

    private static PlaceAccessibilitySummary Merge(
        PlaceAccessibilitySummary first,
        PlaceAccessibilitySummary second)
    {
        return new PlaceAccessibilitySummary(
            WheelchairAccessibleParking: first.WheelchairAccessibleParking ?? second.WheelchairAccessibleParking,
            WheelchairAccessibleEntrance: first.WheelchairAccessibleEntrance ?? second.WheelchairAccessibleEntrance,
            WheelchairAccessibleRestroom: first.WheelchairAccessibleRestroom ?? second.WheelchairAccessibleRestroom,
            WheelchairAccessibleSeating: first.WheelchairAccessibleSeating ?? second.WheelchairAccessibleSeating);
    }

    private static PlaceEditorialSummary Merge(
        PlaceEditorialSummary first,
        PlaceEditorialSummary second)
    {
        return new PlaceEditorialSummary(
            Text: Pick(first.Text, second.Text),
            LanguageCode: Pick(first.LanguageCode, second.LanguageCode));
    }

    private static string? ResolveCategoryFromPlaces(CompanionPlaceCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.PrimaryTypeDisplayName))
        {
            return candidate.PrimaryTypeDisplayName.Trim();
        }

        var normalizedPrimary = NormalizeTypeToken(candidate.PrimaryType);
        if (!string.IsNullOrWhiteSpace(normalizedPrimary))
        {
            return HumanizeTypeToken(normalizedPrimary);
        }

        var bestType = candidate.Types
            .Select(NormalizeTypeToken)
            .Where(static type => !string.IsNullOrWhiteSpace(type))
            .OrderByDescending(GetTypeSpecificityScore)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(bestType))
        {
            return HumanizeTypeToken(bestType);
        }

        return null;
    }

    private static string NormalizeTypeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
    }

    private static int GetTypeSpecificityScore(string typeToken)
    {
        var tokenCount = typeToken.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        var lengthScore = Math.Min(typeToken.Length / 8, 4);
        var genericPenalty = typeToken switch
        {
            "point_of_interest" => 4,
            "establishment" => 4,
            "food" => 2,
            _ => 0
        };

        return (tokenCount * 3) + lengthScore - genericPenalty;
    }

    private static string HumanizeTypeToken(string typeToken)
    {
        var words = typeToken
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CapitalizeWord)
            .ToArray();
        return words.Length == 0
            ? typeToken
            : string.Join(' ', words);
    }

    private static string CapitalizeWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return $"{char.ToUpperInvariant(value[0])}{value[1..].ToLowerInvariant()}";
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
