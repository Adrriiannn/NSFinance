namespace NSFinance.Api.Persistence.Entities;

public class ConversationThread
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? Title { get; set; }
    public ConversationThreadStatus Status { get; set; } = ConversationThreadStatus.Active;
    public DateTime StartedUtc { get; set; }
    public DateTime LastMessageUtc { get; set; }
    public DateTime? LastContextRefreshUtc { get; set; }
    public int ActiveSummaryVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public User User { get; set; } = null!;
    public ICollection<ConversationMessage> Messages { get; set; } = [];
    public ICollection<ConversationTurn> Turns { get; set; } = [];
    public ICollection<ConversationStateSnapshot> StateSnapshots { get; set; } = [];
    public ICollection<ConversationSummary> Summaries { get; set; } = [];
    public ICollection<ConversationContextBuildLog> ContextBuildLogs { get; set; } = [];
}
