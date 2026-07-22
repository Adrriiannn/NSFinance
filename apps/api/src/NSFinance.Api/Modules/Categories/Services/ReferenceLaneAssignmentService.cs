using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

public sealed class ReferenceLaneOptions
{
    public const string SectionName = "Categorization:ReferenceLane";

    // Master flag for the per-row reference lane. Ships dark; flipped per
    // environment for a watched run, like every lane before it.
    public bool Enabled { get; set; }

    public int MaxJudgmentsPerRun { get; set; } = 5;

    // Substrings (against uppercase-normalized statement text) that mark a
    // row as riding a P2P rail rather than naming a business. AIB's Zippay
    // stamps *ZPAY; further rails join by configuration, not code.
    public string[] RailMarkers { get; set; } = ["ZPAY"];
}

public sealed record ReferenceLaneRunSummary(
    int RowsEligible,
    int Judged,
    int Assigned,
    int Abstained,
    int SkippedAlreadyJudged);

// CAT-001 phase two, reference lane: rows that ride P2P rails are judged one
// row at a time against a curated set of reference-shaped definitions, and
// only a floored, direction-compatible assignment writes taxonomy - with rule
// key "ai_assignment" and a ledger row either way. Runs strictly after the
// merchant-knowledge and growth passes, so a business descriptor always gets
// its merchant meaning first.
public sealed class ReferenceLaneAssignmentService(
    AppDbContext dbContext,
    IReferenceRowJudge referenceRowJudge,
    IOptions<ReferenceLaneOptions> options,
    ILogger<ReferenceLaneAssignmentService> logger)
{
    public bool IsEnabled => options.Value.Enabled;

    public async Task<ReferenceLaneRunSummary> AssignAsync(
        Guid userId,
        IReadOnlyList<Transaction> stillUncategorized,
        CancellationToken cancellationToken)
    {
        var railMarkers = options.Value.RailMarkers
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToUpperInvariant())
            .ToArray();

        var eligible = stillUncategorized
            .Where(t => t.TaxonomyDomainId == null
                && t.TaxonomyCategoryId == null
                && t.TaxonomySubcategoryId == null
                && t.DeterministicRelationshipType == null
                && t.TransferKind == null
                && t.AnalyticsTreatment == TransactionAnalyticsTreatments.Ordinary
                && t.Amount != 0)
            .Select(t => (Transaction: t, Normalized: DeterministicMerchantCategorizer.NormalizeStatementText(t.Description)))
            .Where(x => railMarkers.Any(marker => x.Normalized.Contains(marker, StringComparison.Ordinal)))
            .OrderByDescending(x => x.Transaction.BookedAtUtc)
            .ToList();

        if (eligible.Count == 0)
        {
            return new ReferenceLaneRunSummary(0, 0, 0, 0, 0);
        }

        var catalogVersion = CategoryCharacteristicsCatalog.Version;
        var eligibleIds = eligible.Select(x => x.Transaction.Id).ToList();
        var alreadyJudgedIds = await dbContext.ReferenceLaneJudgments
            .Where(j => eligibleIds.Contains(j.TransactionId) && j.CharacteristicsVersion == catalogVersion)
            .Select(j => j.TransactionId)
            .ToListAsync(cancellationToken);
        var alreadyJudged = alreadyJudgedIds.ToHashSet();

        var accountNames = await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.Name)
            .ToListAsync(cancellationToken);

        var referenceCounts = eligible
            .GroupBy(x => x.Normalized, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var judged = 0;
        var assigned = 0;
        var abstained = 0;
        var skippedAlreadyJudged = 0;
        var maxPerRun = Math.Clamp(options.Value.MaxJudgmentsPerRun, 1, 25);
        var nowUtc = DateTime.UtcNow;

        foreach (var (transaction, normalized) in eligible)
        {
            if (judged >= maxPerRun)
            {
                break;
            }

            if (alreadyJudged.Contains(transaction.Id))
            {
                skippedAlreadyJudged += 1;
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            judged += 1;

            var direction = transaction.Amount < 0 ? "outflow" : "inflow";
            var judgment = await referenceRowJudge.JudgeAsync(
                new ReferenceRowJudgmentInput(
                    ReferenceText: normalized,
                    Direction: direction,
                    AbsAmountEur: Math.Abs(transaction.Amount),
                    BookedDate: DateOnly.FromDateTime(transaction.BookedAtUtc),
                    SameReferenceOccurrences: referenceCounts.GetValueOrDefault(normalized, 1),
                    UserAccountNames: accountNames),
                cancellationToken);

            var ledger = new ReferenceLaneJudgment
            {
                Id = Guid.NewGuid(),
                TransactionId = transaction.Id,
                UserId = userId,
                Confidence = judgment.Confidence,
                CharacteristicsVersion = catalogVersion,
                JudgedUtc = nowUtc,
                SummaryJson = JsonSerializer.Serialize(new
                {
                    rationale = judgment.Rationale,
                    abstainReason = judgment.AbstainReason,
                    definitionKey = judgment.DefinitionKey
                })
            };
            dbContext.ReferenceLaneJudgments.Add(ledger);

            var outcomeCode = ResolveOutcome(transaction, direction, judgment, ledger, nowUtc);
            if (outcomeCode is null)
            {
                assigned += 1;
            }
            else
            {
                abstained += 1;
            }

            // Ledger and transaction ids only - reference text never reaches logs.
            logger.LogInformation(
                "Reference lane judged transactionId={TransactionId} direction={Direction} outcome={Outcome} outcomeCode={OutcomeCode} definitionKey={DefinitionKey} confidence={Confidence}",
                transaction.Id,
                direction,
                ledger.Outcome,
                ledger.OutcomeCode,
                ledger.DefinitionKey,
                ledger.Confidence);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var summary = new ReferenceLaneRunSummary(
            RowsEligible: eligible.Count,
            Judged: judged,
            Assigned: assigned,
            Abstained: abstained,
            SkippedAlreadyJudged: skippedAlreadyJudged);

        logger.LogInformation(
            "Reference lane run rowsEligible={RowsEligible} judged={Judged} assigned={Assigned} abstained={Abstained} skippedAlreadyJudged={SkippedAlreadyJudged}",
            summary.RowsEligible,
            summary.Judged,
            summary.Assigned,
            summary.Abstained,
            summary.SkippedAlreadyJudged);

        return summary;
    }

    // Applies the gates in order; returns null when the row was assigned, or
    // the stable outcome code that stopped it. The ledger row is updated to
    // match either way.
    private static string? ResolveOutcome(
        Transaction transaction,
        string direction,
        MerchantCategoryJudgment judgment,
        ReferenceLaneJudgment ledger,
        DateTime nowUtc)
    {
        string? Stop(string code)
        {
            ledger.Outcome = ReferenceLaneJudgmentOutcomes.Abstained;
            ledger.OutcomeCode = code;
            return code;
        }

        if (!judgment.Assigned || judgment.DefinitionKey is null)
        {
            return Stop("judgment_abstained");
        }

        if (!ReferenceRowJudgmentService.TryGetAllowedDefinition(judgment.DefinitionKey, out var definition))
        {
            return Stop("definition_not_allowed");
        }

        if (definition.ConfidenceFloor is not { } floor || judgment.Confidence < floor)
        {
            return Stop("below_confidence_floor");
        }

        var directionCompatible = definition.DirectionExpectation switch
        {
            CharacteristicsDirection.Outflow => direction == "outflow",
            CharacteristicsDirection.Inflow => direction == "inflow",
            _ => true
        };
        if (!directionCompatible)
        {
            return Stop("direction_mismatch");
        }

        if (!CharacteristicsTaxonomyResolver.TryResolve(definition, out var domainId, out var categoryId, out var subcategoryId))
        {
            return Stop("taxonomy_resolution_failed");
        }

        transaction.TaxonomyDomainId = domainId;
        transaction.TaxonomyCategoryId = categoryId;
        transaction.TaxonomySubcategoryId = subcategoryId;
        transaction.CategorizationRuleKey = "ai_assignment";
        // The signal names the lane, never the reference text.
        transaction.CategorizationSignal = "reference_lane";
        transaction.CategorizationCharacteristicsVersion = CategoryCharacteristicsCatalog.Version;
        transaction.CategorizedUtc = nowUtc;

        ledger.Outcome = ReferenceLaneJudgmentOutcomes.Assigned;
        ledger.OutcomeCode = "assigned";
        ledger.DefinitionKey = judgment.DefinitionKey;
        return null;
    }
}
