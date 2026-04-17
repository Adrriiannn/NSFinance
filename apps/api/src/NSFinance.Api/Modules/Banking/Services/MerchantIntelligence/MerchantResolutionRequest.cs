using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed record MerchantResolutionRequest(
    string RawDescriptor,
    Guid? UserId = null,
    Guid? ConnectionId = null,
    Guid? SyncRunId = null,
    Guid? TransactionId = null,
    Guid? NormalizedTransactionId = null,
    int? TaxonomyDomainId = null,
    int? TaxonomyCategoryId = null,
    int? TaxonomySubcategoryId = null,
    bool DeterministicTerminal = false,
    string? DeterministicResultCode = null,
    bool ManualOverridePresent = false,
    decimal Amount = 0m,
    bool DescriptorMerchantLike = true,
    string TriggerSource = "account_transaction_upsert",
    MerchantResolutionRunState? RunState = null)
{
    public static MerchantResolutionRequest CreateLegacy(string rawDescriptor)
    {
        return new MerchantResolutionRequest(
            RawDescriptor: rawDescriptor,
            UserId: null,
            ConnectionId: null,
            SyncRunId: Guid.NewGuid(),
            TransactionId: null,
            NormalizedTransactionId: null,
            TaxonomyDomainId: null,
            TaxonomyCategoryId: null,
            TaxonomySubcategoryId: null,
            DeterministicTerminal: false,
            DeterministicResultCode: "legacy_unscoped",
            ManualOverridePresent: false,
            Amount: -100m,
            DescriptorMerchantLike: true,
            TriggerSource: "legacy_resolution",
            RunState: new MerchantResolutionRunState(Guid.NewGuid()));
    }
}

public sealed class MerchantResolutionRunState(Guid syncRunId)
{
    private readonly object _gate = new();
    private readonly HashSet<string> _processedMerchantKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, int> _connectionAiCallCounts = new();
    private int _aiCallsThisRun;

    public Guid SyncRunId { get; } = syncRunId;

    public int AICallsThisRun
    {
        get
        {
            lock (_gate)
            {
                return _aiCallsThisRun;
            }
        }
    }

    public int GetAICallsForConnection(Guid connectionId)
    {
        lock (_gate)
        {
            return _connectionAiCallCounts.TryGetValue(connectionId, out var calls)
                ? calls
                : 0;
        }
    }

    public bool TryMarkMerchantProcessed(string merchantKey)
    {
        if (string.IsNullOrWhiteSpace(merchantKey))
        {
            return false;
        }

        lock (_gate)
        {
            return _processedMerchantKeys.Add(merchantKey);
        }
    }

    public void MarkAICallExecuted(Guid? connectionId)
    {
        lock (_gate)
        {
            _aiCallsThisRun += 1;
            if (connectionId.HasValue)
            {
                var current = _connectionAiCallCounts.GetValueOrDefault(connectionId.Value);
                _connectionAiCallCounts[connectionId.Value] = current + 1;
            }
        }
    }
}

public sealed record MerchantResolutionProjectedContext(
    Guid? ProjectedTransactionId,
    int? TaxonomyDomainId,
    int? TaxonomyCategoryId,
    int? TaxonomySubcategoryId,
    bool DeterministicTerminal,
    string? DeterministicResultCode,
    DeterministicClassificationStatus DeterministicStatus);

