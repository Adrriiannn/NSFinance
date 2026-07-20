namespace NSFinance.Api.Persistence.Entities;

// The growing merchant knowledge base (CAT-001, user-directed architecture):
// verified merchant patterns and their category assignments. Seeded once from
// the characteristics catalog, grown by AI investigation of unknown merchants
// and by user corrections. Categorization checks this table first; AI and
// online research are spent only on merchants not yet known here.
public class MerchantKnowledge
{
    public Guid Id { get; set; }

    // Uppercase contains-pattern matched against normalized statement text.
    public string NormalizedPattern { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int? TaxonomyDomainId { get; set; }
    public int? TaxonomyCategoryId { get; set; }
    public int? TaxonomySubcategoryId { get; set; }

    // outflow | inflow | either - copied from the judging characteristics so
    // lookups stay direction-safe without re-reading the catalog.
    public string DirectionExpectation { get; set; } = "either";

    // seed | ai_investigation | user_correction
    public string Source { get; set; } = MerchantKnowledgeSources.Seed;

    // Integrity-check summary for AI-researched rows; null for seeds.
    public string? VerificationEvidenceJson { get; set; }

    public double Confidence { get; set; } = 1.0;

    // Characteristics catalog version the assignment was judged against.
    public int CharacteristicsVersion { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public static class MerchantKnowledgeSources
{
    public const string Seed = "seed";
    public const string AiInvestigation = "ai_investigation";
    public const string UserCorrection = "user_correction";
}
