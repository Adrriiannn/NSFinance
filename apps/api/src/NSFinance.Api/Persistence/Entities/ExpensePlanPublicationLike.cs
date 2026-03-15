namespace NSFinance.Api.Persistence.Entities;

public class ExpensePlanPublicationLike
{
    public Guid Id { get; set; }
    public Guid PublicationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ExpensePlanPublication? Publication { get; set; }
    public User? User { get; set; }
}
