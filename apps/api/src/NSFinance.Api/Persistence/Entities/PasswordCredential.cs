namespace NSFinance.Api.Persistence.Entities;

public class PasswordCredential
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string HashAlgorithm { get; set; } = "pbkdf2-sha256";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool RequiresRehash { get; set; }

    public User? User { get; set; }
}
