namespace NSFinance.Api.Persistence.Entities;

public class BankConnectionAttempt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConnectionId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderEnvironment { get; set; } = "sandbox";
    public string Status { get; set; } = "created";
    public string? LaunchOriginPath { get; set; }
    public string? AppReturnUri { get; set; }
    public string CallbackState { get; set; } = string.Empty;
    public string PublicToken { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? AuthLaunchedUtc { get; set; }
    public DateTime? CallbackHandledUtc { get; set; }
    public DateTime? AppReturnInitiatedUtc { get; set; }
    public DateTime? AppReturnConfirmedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime? FailedUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public Guid? SupersededByAttemptId { get; set; }

    public User? User { get; set; }
    public OpenBankingConnection? Connection { get; set; }
    public BankConnectionAttempt? SupersededByAttempt { get; set; }
}
