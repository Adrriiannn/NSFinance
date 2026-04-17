namespace NSFinance.Api.Persistence.Entities;

public class UnresolvedMerchant
{
    public Guid Id { get; set; }
    public string RawDescriptor { get; set; } = string.Empty;
    public string NormalizedDescriptor { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTime? LastInvestigationUtc { get; set; }
    public DateTime? NextEligibleInvestigationUtc { get; set; }
    public int InvestigationAttemptCount { get; set; }
    public DateTime? LastInvestigationFailureUtc { get; set; }
    public string? LastInvestigationFailureCode { get; set; }
    public UnresolvedMerchantStatus Status { get; set; } = UnresolvedMerchantStatus.New;
    public string? Notes { get; set; }
    public decimal TotalObservedSpendAbs { get; set; }
    public double QueuePriorityScore { get; set; }
    public DateTime? QueueEnqueuedAtUtc { get; set; }
    public DateTime? QueueLastScoredUtc { get; set; }
    public int QueueRetryCount { get; set; }
    public DateTime? QueueNextRetryUtc { get; set; }
    public DateTime? LastBudgetSkipUtc { get; set; }
    public DateTime? LastCooldownSkipUtc { get; set; }
    public bool InvestigationInProgress { get; set; }
    public Guid? InvestigationLockId { get; set; }
    public DateTime? InvestigationLockAcquiredUtc { get; set; }
}
