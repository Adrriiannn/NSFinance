namespace NSFinance.Api.Persistence.Entities;

public class ImportJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    public User? User { get; set; }
}
