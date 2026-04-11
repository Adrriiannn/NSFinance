namespace NSFinance.Api.Persistence.Entities;

public class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationThreadId { get; set; }
    public ConversationMessageRole Role { get; set; } = ConversationMessageRole.User;
    public string Content { get; set; } = string.Empty;
    public int MessageOrder { get; set; }
    public string? Topic { get; set; }
    public string? ModelUsed { get; set; }
    public string? TaskType { get; set; }
    public bool IsResolved { get; set; }
    public bool WasTrimEligible { get; set; } = true;
    public bool WasSummaryDerived { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedUtc { get; set; }

    public ConversationThread ConversationThread { get; set; } = null!;
}
