namespace NSFinance.Api.Persistence.Entities;

public class PlacesShortLivedCacheEntry
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public string FieldMaskHash { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
