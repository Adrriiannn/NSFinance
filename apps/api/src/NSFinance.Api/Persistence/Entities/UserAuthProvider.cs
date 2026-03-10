namespace NSFinance.Api.Persistence.Entities;

public class UserAuthProvider
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ProviderType { get; set; } = string.Empty;
    public string? ProviderSubject { get; set; }
    public DateTime LinkedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public User? User { get; set; }
}
