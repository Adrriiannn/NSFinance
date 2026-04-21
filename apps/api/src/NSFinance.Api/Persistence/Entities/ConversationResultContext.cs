namespace NSFinance.Api.Persistence.Entities;

public class ConversationResultContext
{
    public Guid Id { get; set; }
    public Guid ConversationThreadId { get; set; }
    public Guid? ParentResultSetId { get; set; }
    public Guid BranchRootResultSetId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime ActiveUntilUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ConversationThread ConversationThread { get; set; } = null!;
}
