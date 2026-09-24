namespace NSFinance.Api.Persistence.Entities;

// Ledger of catalog seed runs (CAT-001 v6 safety): one row per characteristics
// version, written in the same transaction as the seed changes it records.
// "Has this version been seeded?" is answered here - never inferred from the
// knowledge rows themselves - and the unique version makes a concurrent
// second pass fail instead of duplicating global rows. The change record is
// complete enough to revert the knowledge base exactly.
public class MerchantKnowledgeSeedRun
{
    public Guid Id { get; set; }

    public int CharacteristicsVersion { get; set; }

    // Hash of the seed plan that was applied; a later run under the same
    // version with a different hash means the catalog changed without a bump.
    public string PlanHash { get; set; } = string.Empty;

    // applied | reverted
    public string Status { get; set; } = MerchantKnowledgeSeedRunStatuses.Applied;

    public int InsertedCount { get; set; }
    public int RetargetedCount { get; set; }
    public int DeactivatedCount { get; set; }
    public int ReopenedCount { get; set; }
    public int SkippedDuplicateCount { get; set; }

    // Inserted row IDs, prior values of retargeted rows, deactivated row IDs,
    // and prior state of reopened candidates. Global merchant patterns only -
    // never user identifiers or statement text.
    public string ChangesJson { get; set; } = "{}";

    public DateTime AppliedUtc { get; set; }
    public DateTime? RevertedUtc { get; set; }
}

public static class MerchantKnowledgeSeedRunStatuses
{
    public const string Applied = "applied";
    public const string Reverted = "reverted";
}
