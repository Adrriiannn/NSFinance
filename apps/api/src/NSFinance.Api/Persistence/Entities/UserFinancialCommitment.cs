namespace NSFinance.Api.Persistence.Entities;

public sealed class UserFinancialCommitment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? TargetCommitmentId { get; set; }
    public string OriginType { get; set; } = "decision";
    public string State { get; set; } = "active";
    public string DecisionMode { get; set; } = "confirmed";
    public string LastAction { get; set; } = "confirm";
    public string SnapshotJson { get; set; } = "{}";
    public string? OverrideJson { get; set; }
    public Guid? EffectiveAccountId { get; set; }
    public DateTime? EffectiveNextDateUtc { get; set; }
    public int Revision { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? ConfirmedUtc { get; set; }
    public DateTime? DismissedUtc { get; set; }

    public User? User { get; set; }
    public FinancialAccount? EffectiveAccount { get; set; }
}
