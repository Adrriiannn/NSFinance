namespace NSFinance.Api.Modules.AI.Services;

public interface IUserFinancialProfileSignalMetadataPolicy
{
    void EnsureSignalMetadataCoherence(UserFinancialContextProfileData state, DateTime nowUtc);
}

public sealed class UserFinancialProfileSignalMetadataPolicy : IUserFinancialProfileSignalMetadataPolicy
{
    public void EnsureSignalMetadataCoherence(UserFinancialContextProfileData state, DateTime nowUtc)
    {
        CoerceMap(state.ExplicitSignals, isExplicit: true, nowUtc);
        CoerceMap(state.InferredSignals, isExplicit: false, nowUtc);

        var keysToDrop = state.UnmappedMetadata
            .Where(pair => !state.UnmappedExplicitSignals.ContainsKey(pair.Key)
                           && !state.UnmappedInferredSignals.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in keysToDrop)
        {
            state.UnmappedMetadata.Remove(key);
        }
    }

    private static void CoerceMap(
        IDictionary<UserFinancialProfileSignalKey, UserFinancialProfileSignal> map,
        bool isExplicit,
        DateTime nowUtc)
    {
        var keysToRemove = map
            .Where(x => string.IsNullOrWhiteSpace(x.Value.Value))
            .Select(x => x.Key)
            .ToArray();
        foreach (var key in keysToRemove)
        {
            map.Remove(key);
        }

        var keys = map.Keys.ToArray();
        foreach (var key in keys)
        {
            var existing = map[key];
            var updatedAt = existing.Metadata.UpdatedAtUtc == default ? nowUtc : existing.Metadata.UpdatedAtUtc;
            if (isExplicit)
            {
                map[key] = existing with
                {
                    Metadata = new UserFinancialProfileSignalMetadata(
                        Source: UserFinancialProfileSignalSource.ExplicitUser,
                        Strength: UserFinancialProfileSignalStrength.Explicit,
                        IsExplicit: true,
                        UpdatedAtUtc: updatedAt)
                };
            }
            else
            {
                map[key] = existing with
                {
                    Metadata = new UserFinancialProfileSignalMetadata(
                        Source: existing.Metadata.IsExplicit
                            ? UserFinancialProfileSignalCatalog.GetDefaultInferredSource(key)
                            : existing.Metadata.Source,
                        Strength: existing.Metadata.Strength == UserFinancialProfileSignalStrength.Explicit
                            ? UserFinancialProfileSignalStrength.Acceptable
                            : existing.Metadata.Strength,
                        IsExplicit: false,
                        UpdatedAtUtc: updatedAt)
                };
            }
        }
    }
}
