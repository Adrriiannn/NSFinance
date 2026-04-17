namespace NSFinance.Api.Persistence.Entities;

public sealed class MerchantAIDecisionLog
{
    public Guid Id { get; set; }
    public Guid? TransactionId { get; set; }
    public Guid? NormalizedTransactionId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ConnectionId { get; set; }
    public Guid? SyncRunId { get; set; }
    public string Descriptor { get; set; } = string.Empty;
    public string NormalizedDescriptor { get; set; } = string.Empty;
    public string MerchantKey { get; set; } = string.Empty;
    public string DomainCandidates { get; set; } = string.Empty;
    public string TriggerMode { get; set; } = string.Empty;
    public string DeterministicResult { get; set; } = string.Empty;
    public string RegistryResult { get; set; } = string.Empty;
    public bool AIGateDecision { get; set; }
    public string AISkipReason { get; set; } = string.Empty;
    public string BudgetState { get; set; } = string.Empty;
    public string CooldownState { get; set; } = string.Empty;
    public string? ModelUsed { get; set; }
    public string FinalState { get; set; } = string.Empty;
    public bool AICallExecuted { get; set; }
    public DateTime CreatedUtc { get; set; }
}

