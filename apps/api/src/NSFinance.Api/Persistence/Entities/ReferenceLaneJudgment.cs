namespace NSFinance.Api.Persistence.Entities;

// Per-row audit and cooldown ledger for the reference-lane AI assignment
// (CAT-001 phase two): person-to-person and rail rows are judged one row at a
// time, and every judgment - assigned or abstained - leaves exactly one row
// here per catalog version. The ledger is what stops an abstained row from
// burning a model call on every sync, and what a review surface renders;
// nothing in the lane is ever silent. A catalog version bump makes rows
// eligible again automatically.
public class ReferenceLaneJudgment
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid UserId { get; set; }

    // assigned | abstained
    public string Outcome { get; set; } = ReferenceLaneJudgmentOutcomes.Abstained;

    public string? DefinitionKey { get; set; }
    public double Confidence { get; set; }

    // Stable machine code (never model free-text): the gate that decided.
    public string? OutcomeCode { get; set; }

    // Judgment rationale and abstain reason for review surfaces. Lives in the
    // database beside the transaction it describes; never logged.
    public string? SummaryJson { get; set; }

    public int CharacteristicsVersion { get; set; }
    public DateTime JudgedUtc { get; set; }
}

public static class ReferenceLaneJudgmentOutcomes
{
    public const string Assigned = "assigned";
    public const string Abstained = "abstained";
}
