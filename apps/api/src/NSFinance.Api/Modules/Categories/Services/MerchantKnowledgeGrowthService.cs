using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

public sealed class MerchantKnowledgeGrowthOptions
{
    public const string SectionName = "Categorization:AiGrowth";

    // Master flag for the AI growth loop. Ships dark; flipped per environment
    // for watched runs, exactly like the backfill flag before it.
    public bool Enabled { get; set; }

    public int MaxInvestigationsPerRun { get; set; } = 3;
    public int FailureCooldownHours { get; set; } = 72;
}

public sealed record MerchantGrowthRunSummary(
    int DescriptorsConsidered,
    int Investigated,
    int Promoted,
    int SentToReview,
    int SkippedCooldown);

// The AI growth loop (CAT-001, user-directed architecture): descriptors the
// knowledge base does not recognize are investigated online, integrity-checked
// by the acceptance policy, judged against the characteristics catalog, and -
// only when every gate passes - written into MerchantKnowledge so the DB-first
// pass categorizes them now and forever. Uncertain outcomes park in the
// candidate ledger as needs_review with full evidence; nothing is silent.
public sealed class MerchantKnowledgeGrowthService(
    AppDbContext dbContext,
    IMerchantInvestigationService investigationService,
    IMerchantAcceptancePolicy acceptancePolicy,
    IMerchantCategoryJudge categoryJudge,
    IOptions<MerchantKnowledgeGrowthOptions> options,
    ILogger<MerchantKnowledgeGrowthService> logger)
{
    public bool IsEnabled => options.Value.Enabled;

    public async Task<MerchantGrowthRunSummary> GrowAsync(
        IReadOnlyList<Transaction> unmatchedTransactions,
        CancellationToken cancellationToken)
    {
        var groups = unmatchedTransactions
            .Select(t => (Transaction: t, Normalized: DeterministicMerchantCategorizer.NormalizeStatementText(t.Description)))
            .Where(x => x.Normalized.Length >= 2)
            .GroupBy(x => x.Normalized.Length <= 200 ? x.Normalized : x.Normalized[..200], StringComparer.Ordinal)
            .Select(g => new
            {
                NormalizedDescriptor = g.Key,
                RawSample = g.First().Transaction.Description,
                Occurrences = g.Count(),
                SpendAbs = g.Sum(x => Math.Abs(x.Transaction.Amount)),
                Direction = ResolveDirection(g.Select(x => x.Transaction.Amount))
            })
            .OrderByDescending(x => x.Occurrences)
            .ThenByDescending(x => x.SpendAbs)
            .ToList();

        var investigated = 0;
        var promoted = 0;
        var sentToReview = 0;
        var skippedCooldown = 0;
        var maxPerRun = Math.Clamp(options.Value.MaxInvestigationsPerRun, 1, 10);
        var nowUtc = DateTime.UtcNow;

        foreach (var group in groups)
        {
            if (investigated >= maxPerRun)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var candidate = await dbContext.MerchantKnowledgeCandidates
                .SingleOrDefaultAsync(x => x.NormalizedDescriptor == group.NormalizedDescriptor, cancellationToken);

            if (candidate is null)
            {
                candidate = new MerchantKnowledgeCandidate
                {
                    Id = Guid.NewGuid(),
                    NormalizedDescriptor = group.NormalizedDescriptor,
                    RawDescriptorSample = Clamp(group.RawSample, 500),
                    CreatedUtc = nowUtc
                };
                dbContext.MerchantKnowledgeCandidates.Add(candidate);
            }

            candidate.ObservedOccurrences = group.Occurrences;
            candidate.ObservedSpendAbs = group.SpendAbs;
            candidate.ObservedDirection = group.Direction;
            candidate.UpdatedUtc = nowUtc;

            if (candidate.Status is MerchantKnowledgeCandidateStatuses.Promoted
                or MerchantKnowledgeCandidateStatuses.Rejected
                or MerchantKnowledgeCandidateStatuses.NeedsReview)
            {
                continue;
            }

            if (candidate.NextEligibleUtc is { } nextEligible && nextEligible > nowUtc)
            {
                skippedCooldown += 1;
                continue;
            }

            investigated += 1;
            candidate.AttemptCount += 1;
            candidate.LastAttemptUtc = nowUtc;

            var investigation = await investigationService.InvestigateAsync(
                new MerchantInvestigationRequest(
                    RawDescriptor: group.RawSample,
                    NormalizedDescriptor: group.NormalizedDescriptor,
                    TriggerSource: "categorization_growth"),
                cancellationToken);

            var acceptance = acceptancePolicy.Evaluate(investigation);
            if (acceptance.DecisionType is not (MerchantAcceptanceDecisionType.AcceptedTrusted
                or MerchantAcceptanceDecisionType.AcceptedCautious)
                || acceptance.SelectedCandidate is null)
            {
                ApplyFailureCooldown(candidate, nowUtc, $"identity_{acceptance.DecisionType.ToString().ToLowerInvariant()}");
                candidate.InvestigationSummaryJson = BuildSummaryJson(investigation, acceptance, judgment: null);
                logger.LogInformation(
                    "Merchant growth identity not accepted candidateId={CandidateId} decision={Decision} reasons={Reasons}",
                    candidate.Id,
                    acceptance.DecisionType,
                    string.Join(',', acceptance.ReasonCodes));
                continue;
            }

            var identity = acceptance.SelectedCandidate;
            var judgment = await categoryJudge.JudgeAsync(
                new MerchantCategoryJudgmentInput(
                    NormalizedDescriptor: group.NormalizedDescriptor,
                    CanonicalName: identity.CanonicalName,
                    BusinessSummary: identity.DescriptionSummary,
                    OfficialWebsite: identity.OfficialWebsite,
                    MerchantType: identity.MerchantType.ToString(),
                    MerchantUsageType: identity.MerchantUsageType.ToString(),
                    PrimaryCountryCode: identity.PrimaryCountryCode,
                    ObservedDirection: group.Direction,
                    ObservedOccurrences: group.Occurrences,
                    TypicalAbsAmount: group.Occurrences > 0 ? group.SpendAbs / group.Occurrences : 0m),
                cancellationToken);

            candidate.InvestigationSummaryJson = BuildSummaryJson(investigation, acceptance, judgment);

            if (!judgment.Assigned
                || judgment.DefinitionKey is null
                || !MerchantCategoryJudgmentService.TryGetDefinition(judgment.DefinitionKey, out var definition))
            {
                ParkForReview(candidate, nowUtc, judgment.AbstainReason ?? "judgment_abstained");
                sentToReview += 1;
                continue;
            }

            if (definition.ConfidenceFloor is not { } floor || judgment.Confidence < floor)
            {
                ParkForReview(candidate, nowUtc, "below_confidence_floor");
                sentToReview += 1;
                continue;
            }

            if (!DirectionCompatible(definition.DirectionExpectation, group.Direction))
            {
                ParkForReview(candidate, nowUtc, "direction_mismatch");
                sentToReview += 1;
                continue;
            }

            if (!CharacteristicsTaxonomyResolver.TryResolve(definition, out var domainId, out var categoryId, out var subcategoryId))
            {
                ParkForReview(candidate, nowUtc, "taxonomy_resolution_failed");
                sentToReview += 1;
                continue;
            }

            var patternExists = await dbContext.MerchantKnowledge
                .AnyAsync(
                    x => x.UserId == null && x.NormalizedPattern == group.NormalizedDescriptor,
                    cancellationToken);
            if (patternExists)
            {
                candidate.Status = MerchantKnowledgeCandidateStatuses.Promoted;
                candidate.LastOutcomeCode = "pattern_already_known";
                continue;
            }

            var knowledge = new MerchantKnowledge
            {
                Id = Guid.NewGuid(),
                NormalizedPattern = group.NormalizedDescriptor,
                DisplayName = Clamp(
                    string.IsNullOrWhiteSpace(identity.DisplayName) ? identity.CanonicalName : identity.DisplayName,
                    200),
                TaxonomyDomainId = domainId,
                TaxonomyCategoryId = categoryId,
                TaxonomySubcategoryId = subcategoryId,
                DirectionExpectation = definition.DirectionExpectation switch
                {
                    CharacteristicsDirection.Outflow => "outflow",
                    CharacteristicsDirection.Inflow => "inflow",
                    _ => "either"
                },
                Source = MerchantKnowledgeSources.AiInvestigation,
                VerificationEvidenceJson = candidate.InvestigationSummaryJson,
                Confidence = judgment.Confidence,
                CharacteristicsVersion = CategoryCharacteristicsCatalog.Version,
                IsActive = true,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };
            dbContext.MerchantKnowledge.Add(knowledge);

            candidate.Status = MerchantKnowledgeCandidateStatuses.Promoted;
            candidate.LastOutcomeCode = "promoted";
            candidate.ProposedTaxonomyDomainId = domainId;
            candidate.ProposedTaxonomyCategoryId = categoryId;
            candidate.ProposedTaxonomySubcategoryId = subcategoryId;
            candidate.ProposedConfidence = judgment.Confidence;
            candidate.PromotedKnowledgeId = knowledge.Id;
            promoted += 1;

            // Verified business identity is loggable; raw statement text is not.
            logger.LogInformation(
                "Merchant growth promoted candidateId={CandidateId} canonicalName={CanonicalName} definitionKey={DefinitionKey} confidence={Confidence} domainId={DomainId} categoryId={CategoryId} subcategoryId={SubcategoryId}",
                candidate.Id,
                identity.CanonicalName,
                judgment.DefinitionKey,
                judgment.Confidence,
                domainId,
                categoryId,
                subcategoryId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var summary = new MerchantGrowthRunSummary(
            DescriptorsConsidered: groups.Count,
            Investigated: investigated,
            Promoted: promoted,
            SentToReview: sentToReview,
            SkippedCooldown: skippedCooldown);

        logger.LogInformation(
            "Merchant growth run descriptorsConsidered={DescriptorsConsidered} investigated={Investigated} promoted={Promoted} sentToReview={SentToReview} skippedCooldown={SkippedCooldown}",
            summary.DescriptorsConsidered,
            summary.Investigated,
            summary.Promoted,
            summary.SentToReview,
            summary.SkippedCooldown);

        return summary;
    }

    private void ApplyFailureCooldown(MerchantKnowledgeCandidate candidate, DateTime nowUtc, string outcomeCode)
    {
        candidate.LastOutcomeCode = Clamp(outcomeCode, 120);
        candidate.NextEligibleUtc = nowUtc.AddHours(Math.Max(1, options.Value.FailureCooldownHours));
    }

    private static void ParkForReview(MerchantKnowledgeCandidate candidate, DateTime nowUtc, string outcomeCode)
    {
        candidate.Status = MerchantKnowledgeCandidateStatuses.NeedsReview;
        candidate.LastOutcomeCode = Clamp(outcomeCode, 120);
        candidate.NextEligibleUtc = null;
    }

    private static bool DirectionCompatible(CharacteristicsDirection expected, string observed)
    {
        return expected switch
        {
            CharacteristicsDirection.Outflow => observed == "outflow",
            CharacteristicsDirection.Inflow => observed == "inflow",
            _ => true
        };
    }

    private static string ResolveDirection(IEnumerable<decimal> amounts)
    {
        var anyOutflow = false;
        var anyInflow = false;
        foreach (var amount in amounts)
        {
            if (amount < 0)
            {
                anyOutflow = true;
            }
            else if (amount > 0)
            {
                anyInflow = true;
            }
        }

        return (anyOutflow, anyInflow) switch
        {
            (true, false) => "outflow",
            (false, true) => "inflow",
            _ => "mixed"
        };
    }

    private static string BuildSummaryJson(
        MerchantInvestigationResult investigation,
        MerchantAcceptanceDecision acceptance,
        MerchantCategoryJudgment? judgment)
    {
        return JsonSerializer.Serialize(new
        {
            investigation = new
            {
                recommendation = investigation.Recommendation.ToString(),
                overallConfidence = investigation.OverallConfidence,
                ambiguityLevel = investigation.AmbiguityLevel,
                evidence = investigation.Evidence
                    .Take(6)
                    .Select(e => new
                    {
                        type = e.EvidenceType.ToString(),
                        summary = e.EvidenceSummary,
                        confidence = e.Confidence,
                        sourceTrust = e.SourceTrustLevel.ToString()
                    })
            },
            acceptance = new
            {
                decision = acceptance.DecisionType.ToString(),
                confidence = acceptance.Confidence,
                reasonCodes = acceptance.ReasonCodes
            },
            judgment = judgment is null
                ? null
                : new
                {
                    assigned = judgment.Assigned,
                    definitionKey = judgment.DefinitionKey,
                    confidence = judgment.Confidence,
                    rationale = judgment.Rationale,
                    abstainReason = judgment.AbstainReason
                }
        });
    }

    private static string Clamp(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
