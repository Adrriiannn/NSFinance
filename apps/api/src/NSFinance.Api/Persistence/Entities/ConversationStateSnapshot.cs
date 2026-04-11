namespace NSFinance.Api.Persistence.Entities;

public class ConversationStateSnapshot
{
    public Guid Id { get; set; }
    public Guid ConversationThreadId { get; set; }
    public string StateJson { get; set; } = "{}";
    public int StateVersion { get; set; }
    public ConversationStateSnapshotReason Reason { get; set; } = ConversationStateSnapshotReason.ManualUpdate;
    public DateTime CreatedUtc { get; set; }

    public ConversationThread ConversationThread { get; set; } = null!;
}
