namespace NSFinance.Api.Persistence.Entities;

public class ExpensePlanPublicationDownload
{
    public Guid Id { get; set; }
    public Guid PublicationId { get; set; }
    public Guid UserId { get; set; }
    public Guid CreatedPlanId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ExpensePlanPublication? Publication { get; set; }
    public User? User { get; set; }
}
