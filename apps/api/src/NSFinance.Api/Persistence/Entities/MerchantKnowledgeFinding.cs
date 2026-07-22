namespace NSFinance.Api.Persistence.Entities;

// The extensive findings store (CAT-001 phase two): every AI investigation
// deposits its COMPLETE record here - all candidate identities with their
// for/against reasoning and risk flags, the full evidence list with sources
// and trust tiers, alias suggestions, acceptance reasoning, and the category
// judgment rationale - one immutable row per investigation, versioned per
// candidate descriptor. Global by design: one user's investigation enriches
// the dictionary every user draws from. The candidate ledger keeps only its
// trimmed working summary; this table is the archive curation jobs mine.
public class MerchantKnowledgeFinding
{
    public Guid Id { get; set; }

    // The candidate descriptor this investigation ran for. Always set.
    public Guid CandidateId { get; set; }

    // The knowledge row this investigation produced or confirmed; null when
    // the run ended in review, cooldown, or refusal.
    public Guid? KnowledgeId { get; set; }

    // Accepted identity name when identity checks passed; null otherwise.
    public string? CanonicalName { get; set; }

    public string AcceptanceDecision { get; set; } = string.Empty;

    // Stable machine code for how the run ended: promoted, judgment_abstained,
    // below_confidence_floor, direction_mismatch, taxonomy_resolution_failed,
    // pattern_already_known, or identity_<decision>.
    public string OutcomeCode { get; set; } = string.Empty;

    // The full findings record, serialized with nothing trimmed.
    public string FindingsJson { get; set; } = string.Empty;

    public int CharacteristicsVersion { get; set; }

    // 1-based, increments per re-investigation of the same candidate.
    public int FindingVersion { get; set; }

    public DateTime CreatedUtc { get; set; }
}
