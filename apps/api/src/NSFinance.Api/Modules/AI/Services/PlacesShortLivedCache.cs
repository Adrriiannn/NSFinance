using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
namespace NSFinance.Api.Modules.AI.Services;

public sealed class PlacesShortLivedCache(AppDbContext dbContext, IChatTelemetry telemetry) : IPlacesShortLivedCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string provider, string placeId, string fieldMaskHash, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entry = await dbContext.PlacesShortLivedCache
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Provider == provider
                        && item.PlaceId == placeId
                        && item.FieldMaskHash == fieldMaskHash
                        && item.ExpiresAtUtc > now,
                ct);
        await telemetry.TrackAsync(
            entry is null ? "places.cache.miss" : "places.cache.hit",
            new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["fieldMaskHash"] = fieldMaskHash
            },
            ct);
        if (entry is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(entry.PayloadJson, JsonOptions);
    }

    public async Task SetAsync<T>(string provider, string placeId, string fieldMaskHash, T payload, TimeSpan ttl, CancellationToken ct)
    {
        if (payload is null || ttl <= TimeSpan.Zero)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var entry = await dbContext.PlacesShortLivedCache
            .SingleOrDefaultAsync(
                item => item.Provider == provider
                        && item.PlaceId == placeId
                        && item.FieldMaskHash == fieldMaskHash,
                ct);
        if (entry is null)
        {
            entry = new PlacesShortLivedCacheEntry
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                PlaceId = placeId,
                FieldMaskHash = fieldMaskHash
            };
            dbContext.PlacesShortLivedCache.Add(entry);
        }

        entry.PayloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        entry.CreatedAtUtc = now;
        entry.ExpiresAtUtc = now.Add(ttl);
        await dbContext.SaveChangesAsync(ct);
    }
}
