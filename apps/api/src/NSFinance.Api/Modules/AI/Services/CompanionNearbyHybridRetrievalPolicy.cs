namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionNearbyHybridRetrievalDecision(
    bool UseHybridRetrieval,
    string ReasonCode);

public interface ICompanionNearbyHybridRetrievalPolicy
{
    CompanionNearbyHybridRetrievalDecision Decide(
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult constraints);
}

public sealed class CompanionNearbyHybridRetrievalPolicy : ICompanionNearbyHybridRetrievalPolicy
{
    public CompanionNearbyHybridRetrievalDecision Decide(
        PlaceSearchLocationContext? locationContext,
        LocalDiscoveryConstraintExtractionResult constraints)
    {
        var hasGpsCoordinates = string.Equals(locationContext?.Source, "gps", StringComparison.OrdinalIgnoreCase)
                                && locationContext?.Latitude.HasValue == true
                                && locationContext?.Longitude.HasValue == true;
        if (!hasGpsCoordinates)
        {
            return new CompanionNearbyHybridRetrievalDecision(
                UseHybridRetrieval: false,
                ReasonCode: "places_retrieval:hybrid_not_applicable_non_gps");
        }

        var hasNearMeSemantic = constraints.HasNearMeLanguage || locationContext?.HasNearMeSemantic == true;
        if (!hasNearMeSemantic)
        {
            var implicitCommerceLocalBias = locationContext?.ImplicitLocalBias == true
                                            || locationContext?.PlannerIntentFamily == RealWorldIntentFamily.CommerceDiscovery
                                            || IsCommerceDomain(locationContext?.PlannerSelectedDomain);
            if (implicitCommerceLocalBias)
            {
                return new CompanionNearbyHybridRetrievalDecision(
                    UseHybridRetrieval: true,
                    ReasonCode: "places_retrieval:hybrid_applicable_gps_commerce_local_bias");
            }

            return new CompanionNearbyHybridRetrievalDecision(
                UseHybridRetrieval: false,
                ReasonCode: "places_retrieval:hybrid_not_applicable_non_near_me");
        }

        return new CompanionNearbyHybridRetrievalDecision(
            UseHybridRetrieval: true,
            ReasonCode: "places_retrieval:hybrid_applicable_gps_near_me");
    }

    private static bool IsCommerceDomain(RealWorldDiscoveryDomain? domain)
    {
        return domain is RealWorldDiscoveryDomain.ElectronicsRetail
            or RealWorldDiscoveryDomain.ConvenienceStore
            or RealWorldDiscoveryDomain.Grocery
            or RealWorldDiscoveryDomain.ShoppingGeneral
            or RealWorldDiscoveryDomain.CommerceGeneral
            or RealWorldDiscoveryDomain.PetrolStation;
    }
}
