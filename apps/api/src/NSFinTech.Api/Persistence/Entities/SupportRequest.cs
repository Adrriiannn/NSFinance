namespace NSFinTech.Api.Persistence.Entities;

public class SupportRequest
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public User? User { get; set; }
}
