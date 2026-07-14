namespace NSFinance.Api.Persistence.Entities;

public class BankingOperationJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConnectionId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public string? LeaseId { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public string? LastFailureCode { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime? FailedUtc { get; set; }

    public OpenBankingConnection? Connection { get; set; }
}
