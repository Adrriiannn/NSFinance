namespace NSFinance.Api.Persistence.Entities;

public class ConversationSummary
{
    public Guid Id { get; set; }
    public Guid ConversationThreadId { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public ConversationSummaryScope SummaryScope { get; set; } = ConversationSummaryScope.FullThreadToPoint;
    public int MessageStartOrder { get; set; }
    public int MessageEndOrder { get; set; }
    public int SummaryVersion { get; set; }
    public DateTime CreatedUtc { get; set; }

    public ConversationThread ConversationThread { get; set; } = null!;
}
