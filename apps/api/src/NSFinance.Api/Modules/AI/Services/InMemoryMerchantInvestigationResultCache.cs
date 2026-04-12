using System.Collections.Concurrent;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class InMemoryMerchantInvestigationResultCache : IMerchantInvestigationResultCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string normalizedDescriptor, DateTime nowUtc, out MerchantInvestigationResult result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(normalizedDescriptor))
        {
            return false;
        }

        if (!_entries.TryGetValue(normalizedDescriptor, out var entry))
        {
            return false;
        }

        if (entry.ExpiresUtc <= nowUtc)
        {
            _entries.TryRemove(normalizedDescriptor, out _);
            return false;
        }

        result = entry.Result;
        return true;
    }

    public void Set(string normalizedDescriptor, MerchantInvestigationResult result, DateTime nowUtc, AIExecutionOptions options)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescriptor))
        {
            return;
        }

        var ttlSeconds = result.Succeeded && !result.InsufficientEvidence
            ? Math.Max(10, options.MerchantInvestigationResultCacheSeconds)
            : Math.Max(10, options.MerchantInvestigationFailureCacheSeconds);

        var expiresUtc = nowUtc.AddSeconds(ttlSeconds);
        _entries[normalizedDescriptor] = new CacheEntry(result, expiresUtc);

        if (_entries.Count > 10_000)
        {
            var expiredKeys = _entries
                .Where(x => x.Value.ExpiresUtc <= nowUtc)
                .Select(x => x.Key)
                .ToArray();

            foreach (var key in expiredKeys)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private sealed record CacheEntry(MerchantInvestigationResult Result, DateTime ExpiresUtc);
}
