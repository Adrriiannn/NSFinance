namespace NSFinance.Api.Persistence.Entities;

public class ConversationContextBuildLog
{
    public Guid Id { get; set; }
    public Guid ConversationThreadId { get; set; }
    public string? CorrelationId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string ModelClass { get; set; } = string.Empty;
    public int IncludedRecentMessageCount { get; set; }
    public int? IncludedSummaryVersion { get; set; }
    public int? IncludedStateVersion { get; set; }
    public int EstimatedPromptTokenCount { get; set; }
    public string? TrimReason { get; set; }
    public DateTime CreatedUtc { get; set; }

    public ConversationThread ConversationThread { get; set; } = null!;
}
