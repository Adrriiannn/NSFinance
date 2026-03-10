namespace NSFinTech.Api.Persistence.Entities;

public class SupportRequest
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Subcategory { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ScreenshotReference { get; set; }
    public Guid? ConnectionId { get; set; }
    public Guid? LinkedBankAccountId { get; set; }
    public string DiagnosticsJson { get; set; } = "{}";
    public string Status { get; set; } = "open";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public User? User { get; set; }
}
