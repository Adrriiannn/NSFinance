namespace NSFinance.Api.Persistence.Entities;

public class AuthAttempt
{
    public Guid Id { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? IpAddress { get; set; }
    public bool WasSuccessful { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedUtc { get; set; }
}
