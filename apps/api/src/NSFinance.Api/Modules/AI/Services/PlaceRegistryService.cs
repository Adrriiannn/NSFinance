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

public sealed class PlaceRegistryService(AppDbContext dbContext, IChatTelemetry telemetry) : IPlaceRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RegisterSeenAsync(
        string provider,
        string providerPlaceId,
        IReadOnlyList<string> internalTags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerPlaceId))
        {
            return;
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedPlaceId = providerPlaceId.Trim();
        var now = DateTime.UtcNow;
        var entity = await dbContext.PlaceRegistry
            .SingleOrDefaultAsync(
                item => item.Provider == normalizedProvider && item.ProviderPlaceId == normalizedPlaceId,
                cancellationToken);
        if (entity is null)
        {
            entity = new PlaceRegistryEntry
            {
                Id = Guid.NewGuid(),
                Provider = normalizedProvider,
                ProviderPlaceId = normalizedPlaceId,
                FirstSeenAtUtc = now
            };
            dbContext.PlaceRegistry.Add(entity);
        }

        entity.LastSeenAtUtc = now;
        entity.InternalTagsJson = JsonSerializer.Serialize(
            internalTags
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToArray(),
            JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
        await telemetry.TrackAsync(
            "places.registry.place_seen",
            new Dictionary<string, object?>
            {
                ["provider"] = normalizedProvider
            },
            cancellationToken);
    }
}
