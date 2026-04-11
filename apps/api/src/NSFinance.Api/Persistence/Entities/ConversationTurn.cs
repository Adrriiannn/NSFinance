namespace NSFinance.Api.Persistence.Entities;

public class ConversationTurn
{
    public Guid Id { get; set; }
    public Guid ConversationThreadId { get; set; }
    public string ClientRequestId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string ModelClass { get; set; } = string.Empty;
    public string? ModelUsed { get; set; }
    public string? ModelDeployment { get; set; }
    public ConversationTurnStatus Status { get; set; } = ConversationTurnStatus.Received;
    public string ContextSource { get; set; } = "none";
    public int? EstimatedPromptTokenCount { get; set; }
    public long? ResponseLatencyMs { get; set; }
    public Guid? UserMessageId { get; set; }
    public Guid? AssistantMessageId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public int AttemptCount { get; set; } = 1;
    public bool WasDeduplicated { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime? CancelledUtc { get; set; }
    public DateTime? TimedOutUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ConversationThread ConversationThread { get; set; } = null!;
    public ICollection<ConversationMessage> Messages { get; set; } = [];
    public ICollection<ConversationContextBuildLog> ContextBuildLogs { get; set; } = [];
}
