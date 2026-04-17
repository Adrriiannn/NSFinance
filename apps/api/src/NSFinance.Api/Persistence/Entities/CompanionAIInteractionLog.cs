namespace NSFinance.Api.Persistence.Entities;

public sealed class CompanionAIInteractionLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string ToolsUsed { get; set; } = string.Empty;
    public int TokensInput { get; set; }
    public int TokensOutput { get; set; }
    public string Model { get; set; } = string.Empty;
    public long ResponseTimeMs { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedUtc { get; set; }
}
