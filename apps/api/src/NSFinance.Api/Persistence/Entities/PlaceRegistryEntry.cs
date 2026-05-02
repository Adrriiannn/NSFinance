namespace NSFinance.Api.Persistence.Entities;

public class PlaceRegistryEntry
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderPlaceId { get; set; } = string.Empty;
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? LastRefreshedAtUtc { get; set; }
    public string InternalTagsJson { get; set; } = "[]";
    public string InternalMetricsJson { get; set; } = "{}";
}
