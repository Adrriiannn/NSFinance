namespace NSFinance.Api.Persistence.Entities;

public class ExpensePlanPublicationModerationEvent
{
    public Guid Id { get; set; }
    public Guid PublicationId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string MatchedRulesJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }

    public ExpensePlanPublication? Publication { get; set; }
}
