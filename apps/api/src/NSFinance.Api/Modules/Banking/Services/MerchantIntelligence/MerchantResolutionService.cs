using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantResolutionService(
    AppDbContext dbContext,
    MerchantDescriptorNormalizer normalizer,
    IMerchantRegistryService merchantRegistryService,
    IMerchantInvestigationService investigationService,
    IMerchantAcceptancePolicy acceptancePolicy,
    ILogger<MerchantResolutionService> logger) : IMerchantResolutionService
{
    private const int MaxFuzzyAliasCandidates = 80;
    private const int MaxFamilyCandidates = 30;
    private const double FuzzyAcceptanceThreshold = 0.82d;
    private const double FamilyAcceptanceThreshold = 0.76d;

    public async Task<MerchantResolutionResult> ResolveAsync(string rawDescriptor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedDescriptor = normalizer.Normalize(rawDescriptor);
        if (normalizedDescriptor.Length == 0)
        {
            return new MerchantResolutionResult(
                MerchantId: null,
                ResolutionConfidence: 0d,
                ResolutionType: MerchantResolutionType.None,
                MatchedAlias: null,
                IsResolved: false,
                UnresolvedMerchantId: null,
                NormalizedDescriptor: string.Empty,
                AcceptanceDecisionType: null,
                ReasonCodes: ["descriptor_empty"]);
        }

        var exact = await TryResolveExactAliasAsync(normalizedDescriptor, cancellationToken);
        if (exact.IsResolved)
        {
            return exact;
        }

        var fuzzy = await TryResolveFuzzyAliasAsync(normalizedDescriptor, cancellationToken);
        if (fuzzy.IsResolved)
        {
            return fuzzy;
        }

        var family = await TryResolveFamilyMatchAsync(normalizedDescriptor, cancellationToken);
        if (family.IsResolved)
        {
            return family;
        }

        return await ResolveThroughUnresolvedLifecycleAsync(rawDescriptor, normalizedDescriptor, cancellationToken);
    }

    private async Task<MerchantResolutionResult> TryResolveExactAliasAsync(string normalizedDescriptor, CancellationToken cancellationToken)
    {
        var exactMatch = await dbContext.MerchantAliases
            .AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedAliasText == normalizedDescriptor)
            .Join(
                dbContext.Merchants.AsNoTracking(),
                alias => alias.MerchantId,
                merchant => merchant.Id,
                (alias, merchant) => new
                {
                    Alias = alias,
                    MerchantStatus = merchant.MerchantStatus
                })
            .Where(x => x.MerchantStatus != MerchantStatus.Retired && x.MerchantStatus != MerchantStatus.Unresolved)
            .OrderByDescending(x => x.Alias.IsExactMatchPreferred)
            .ThenByDescending(x => x.Alias.Confidence)
            .ThenByDescending(x => x.Alias.LastSeenUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (exactMatch is null)
        {
            return UnresolvedResult(normalizedDescriptor, ["exact_alias_not_found"]);
        }

        var confidence = exactMatch.Alias.IsExactMatchPreferred
            ? Math.Max(0.96d, exactMatch.Alias.Confidence)
            : Math.Max(0.90d, exactMatch.Alias.Confidence);

        logger.LogDebug(
            "Merchant resolved by exact alias merchantId={MerchantId} aliasId={AliasId} normalizedAlias={Alias}",
            exactMatch.Alias.MerchantId,
            exactMatch.Alias.Id,
            exactMatch.Alias.NormalizedAliasText);

        return new MerchantResolutionResult(
            MerchantId: exactMatch.Alias.MerchantId,
            ResolutionConfidence: Math.Round(Math.Clamp(confidence, 0d, 1d), 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.ExactAlias,
            MatchedAlias: exactMatch.Alias.AliasText,
            IsResolved: true,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes: ["matched_exact_alias"]);
    }

    private async Task<MerchantResolutionResult> TryResolveFuzzyAliasAsync(string normalizedDescriptor, CancellationToken cancellationToken)
    {
        var descriptorTokens = normalizer.Tokenize(normalizedDescriptor);
        var firstToken = descriptorTokens.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return UnresolvedResult(normalizedDescriptor, ["fuzzy_alias_not_attempted"]);
        }

        var fuzzyCandidates = await dbContext.MerchantAliases
            .AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedAliasText.StartsWith(firstToken))
            .Join(
                dbContext.Merchants.AsNoTracking(),
                alias => alias.MerchantId,
                merchant => merchant.Id,
                (alias, merchant) => new
                {
                    Alias = alias,
                    merchant.MerchantStatus,
                    merchant.MerchantUsageType
                })
            .Where(x => x.MerchantStatus != MerchantStatus.Retired && x.MerchantStatus != MerchantStatus.Unresolved)
            .OrderByDescending(x => x.Alias.IsExactMatchPreferred)
            .ThenByDescending(x => x.Alias.Confidence)
            .Take(MaxFuzzyAliasCandidates)
            .ToListAsync(cancellationToken);

        var best = fuzzyCandidates
            .Select(candidate =>
            {
                var aliasTokens = normalizer.Tokenize(candidate.Alias.NormalizedAliasText);
                var tokenSimilarity = ComputeJaccard(descriptorTokens, aliasTokens);
                var startsWith = normalizedDescriptor.StartsWith(candidate.Alias.NormalizedAliasText, StringComparison.Ordinal)
                                 || candidate.Alias.NormalizedAliasText.StartsWith(normalizedDescriptor, StringComparison.Ordinal);
                var score = (tokenSimilarity * 0.72d)
                            + (Math.Clamp(candidate.Alias.Confidence, 0d, 1d) * 0.18d)
                            + (candidate.Alias.IsExactMatchPreferred ? 0.08d : 0d)
                            + (startsWith ? 0.06d : 0d);

                return new
                {
                    candidate.Alias,
                    candidate.MerchantUsageType,
                    Score = Math.Clamp(score, 0d, 1d),
                    TokenSimilarity = tokenSimilarity
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.TokenSimilarity)
            .FirstOrDefault();

        if (best is null || best.Score < FuzzyAcceptanceThreshold || best.TokenSimilarity < 0.72d)
        {
            return UnresolvedResult(normalizedDescriptor, ["fuzzy_alias_not_found"]);
        }

        return new MerchantResolutionResult(
            MerchantId: best.Alias.MerchantId,
            ResolutionConfidence: Math.Round(best.Score, 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.FuzzyAlias,
            MatchedAlias: best.Alias.AliasText,
            IsResolved: true,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes:
            [
                "matched_fuzzy_alias",
                best.MerchantUsageType == MerchantUsageType.MixedUse ? "mixed_use_alias_resolved" : "alias_resolved"
            ]);
    }

    private async Task<MerchantResolutionResult> TryResolveFamilyMatchAsync(string normalizedDescriptor, CancellationToken cancellationToken)
    {
        var descriptorTokens = normalizer.Tokenize(normalizedDescriptor);
        var firstToken = descriptorTokens.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return UnresolvedResult(normalizedDescriptor, ["family_match_not_attempted"]);
        }

        var familyCandidates = await dbContext.Merchants
            .AsNoTracking()
            .Where(x => x.MerchantStatus != MerchantStatus.Retired && x.MerchantStatus != MerchantStatus.Unresolved)
            .Where(x => x.NormalizedCanonicalName.StartsWith(firstToken))
            .OrderByDescending(x => x.MerchantUsageType == MerchantUsageType.MixedUse)
            .ThenBy(x => x.NormalizedCanonicalName.Length)
            .Take(MaxFamilyCandidates)
            .Select(x => new
            {
                x.Id,
                x.CanonicalName,
                x.NormalizedCanonicalName
            })
            .ToListAsync(cancellationToken);

        var best = familyCandidates
            .Select(candidate =>
            {
                var candidateTokens = normalizer.Tokenize(candidate.NormalizedCanonicalName);
                var similarity = ComputeJaccard(descriptorTokens, candidateTokens);
                var score = (similarity * 0.86d)
                            + (candidate.NormalizedCanonicalName == normalizedDescriptor ? 0.1d : 0d);
                return new
                {
                    candidate.Id,
                    candidate.CanonicalName,
                    Score = Math.Clamp(score, 0d, 1d),
                    Similarity = similarity
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Similarity)
            .FirstOrDefault();

        if (best is null || best.Score < FamilyAcceptanceThreshold || best.Similarity < 0.75d)
        {
            return UnresolvedResult(normalizedDescriptor, ["family_match_not_found"]);
        }

        return new MerchantResolutionResult(
            MerchantId: best.Id,
            ResolutionConfidence: Math.Round(best.Score, 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.FamilyMatch,
            MatchedAlias: best.CanonicalName,
            IsResolved: true,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes: ["matched_family"]);
    }

    private async Task<MerchantResolutionResult> ResolveThroughUnresolvedLifecycleAsync(
        string rawDescriptor,
        string normalizedDescriptor,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var unresolved = await dbContext.UnresolvedMerchants
            .SingleOrDefaultAsync(x => x.NormalizedDescriptor == normalizedDescriptor, cancellationToken);
        if (unresolved is null)
        {
            unresolved = new UnresolvedMerchant
            {
                Id = Guid.NewGuid(),
                RawDescriptor = normalizer.SanitizeForStorage(rawDescriptor),
                NormalizedDescriptor = normalizedDescriptor,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc,
                OccurrenceCount = 1,
                Status = UnresolvedMerchantStatus.New
            };
            dbContext.UnresolvedMerchants.Add(unresolved);
        }
        else
        {
            unresolved.LastSeenUtc = nowUtc;
            unresolved.OccurrenceCount += 1;
        }

        unresolved.Status = UnresolvedMerchantStatus.Investigating;
        unresolved.LastInvestigationUtc = nowUtc;
        await dbContext.SaveChangesAsync(cancellationToken);

        var investigationResult = await investigationService.InvestigateAsync(
            new MerchantInvestigationRequest(
                RawDescriptor: normalizer.SanitizeForStorage(rawDescriptor),
                NormalizedDescriptor: normalizedDescriptor,
                TriggerSource: "resolution_miss"),
            cancellationToken);

        var decision = acceptancePolicy.Evaluate(investigationResult);
        if (decision.DecisionType is MerchantAcceptanceDecisionType.AcceptedTrusted or MerchantAcceptanceDecisionType.AcceptedCautious
            && decision.SelectedCandidate is not null)
        {
            var resolved = await ApplyAcceptedInvestigationAsync(
                unresolved,
                rawDescriptor,
                normalizedDescriptor,
                decision,
                investigationResult,
                cancellationToken);

            return resolved;
        }

        unresolved.Status = decision.DecisionType == MerchantAcceptanceDecisionType.Rejected
            ? UnresolvedMerchantStatus.AwaitingEvidence
            : UnresolvedMerchantStatus.Investigating;
        unresolved.Notes = decision.ReasonCodes.Count == 0
            ? null
            : string.Join(",", decision.ReasonCodes);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Merchant unresolved normalizedDescriptor={NormalizedDescriptor} unresolvedId={UnresolvedMerchantId} decision={Decision} reasonCodes={ReasonCodes}",
            normalizedDescriptor,
            unresolved.Id,
            decision.DecisionType,
            string.Join(",", decision.ReasonCodes));

        return new MerchantResolutionResult(
            MerchantId: null,
            ResolutionConfidence: Math.Round(decision.Confidence, 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.None,
            MatchedAlias: null,
            IsResolved: false,
            UnresolvedMerchantId: unresolved.Id,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: decision.DecisionType,
            ReasonCodes: decision.ReasonCodes);
    }

    private async Task<MerchantResolutionResult> ApplyAcceptedInvestigationAsync(
        UnresolvedMerchant unresolved,
        string rawDescriptor,
        string normalizedDescriptor,
        MerchantAcceptanceDecision decision,
        MerchantInvestigationResult investigationResult,
        CancellationToken cancellationToken)
    {
        var selectedCandidate = decision.SelectedCandidate!;
        Merchant merchant;
        if (selectedCandidate.ExistingMerchantId.HasValue)
        {
            merchant = await dbContext.Merchants
                          .SingleOrDefaultAsync(x => x.Id == selectedCandidate.ExistingMerchantId.Value, cancellationToken)
                      ?? await merchantRegistryService.CreateMerchantAsync(
                          new MerchantCreateRequest(
                              CanonicalName: selectedCandidate.CanonicalName,
                              DisplayName: selectedCandidate.DisplayName,
                              MerchantStatus: decision.DecisionType == MerchantAcceptanceDecisionType.AcceptedTrusted
                                  ? MerchantStatus.Active
                                  : MerchantStatus.LowConfidence,
                              MerchantType: selectedCandidate.MerchantType,
                              MerchantUsageType: selectedCandidate.MerchantUsageType,
                              PrimaryCountryCode: selectedCandidate.PrimaryCountryCode,
                              OfficialWebsite: selectedCandidate.OfficialWebsite,
                              DescriptionSummary: selectedCandidate.DescriptionSummary,
                              ParentMerchantId: null),
                          cancellationToken);
        }
        else
        {
            merchant = await merchantRegistryService.CreateMerchantAsync(
                new MerchantCreateRequest(
                    CanonicalName: selectedCandidate.CanonicalName,
                    DisplayName: selectedCandidate.DisplayName,
                    MerchantStatus: decision.DecisionType == MerchantAcceptanceDecisionType.AcceptedTrusted
                        ? MerchantStatus.Active
                        : MerchantStatus.LowConfidence,
                    MerchantType: selectedCandidate.MerchantType,
                    MerchantUsageType: selectedCandidate.MerchantUsageType,
                    PrimaryCountryCode: selectedCandidate.PrimaryCountryCode,
                    OfficialWebsite: selectedCandidate.OfficialWebsite,
                    DescriptionSummary: selectedCandidate.DescriptionSummary,
                    ParentMerchantId: null),
                cancellationToken);
        }

        await merchantRegistryService.AttachAliasAsync(
            new MerchantAliasCreateRequest(
                MerchantId: merchant.Id,
                AliasText: rawDescriptor,
                AliasType: MerchantAliasType.BillingDescriptor,
                Confidence: Math.Clamp(decision.Confidence, 0.6d, 1d),
                IsExactMatchPreferred: false,
                Source: $"investigation:{decision.DecisionType}",
                IsActive: true),
            cancellationToken);

        foreach (var evidence in investigationResult.Evidence)
        {
            await merchantRegistryService.AddEvidenceAsync(
                new MerchantEvidenceCreateRequest(
                    MerchantId: merchant.Id,
                    EvidenceType: evidence.EvidenceType,
                    EvidenceSummary: evidence.EvidenceSummary,
                    Confidence: evidence.Confidence,
                    SourceReference: evidence.SourceReference),
                cancellationToken);
        }

        unresolved.Status = UnresolvedMerchantStatus.Resolved;
        unresolved.Notes = $"resolved:{merchant.Id:N}";
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Merchant resolved through investigation normalizedDescriptor={NormalizedDescriptor} merchantId={MerchantId} decision={Decision}",
            normalizedDescriptor,
            merchant.Id,
            decision.DecisionType);

        return new MerchantResolutionResult(
            MerchantId: merchant.Id,
            ResolutionConfidence: Math.Round(decision.Confidence, 4, MidpointRounding.AwayFromZero),
            ResolutionType: MerchantResolutionType.FamilyMatch,
            MatchedAlias: rawDescriptor,
            IsResolved: true,
            UnresolvedMerchantId: unresolved.Id,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: decision.DecisionType,
            ReasonCodes: decision.ReasonCodes);
    }

    private static MerchantResolutionResult UnresolvedResult(string normalizedDescriptor, IReadOnlyList<string> reasonCodes)
    {
        return new MerchantResolutionResult(
            MerchantId: null,
            ResolutionConfidence: 0d,
            ResolutionType: MerchantResolutionType.None,
            MatchedAlias: null,
            IsResolved: false,
            UnresolvedMerchantId: null,
            NormalizedDescriptor: normalizedDescriptor,
            AcceptanceDecisionType: null,
            ReasonCodes: reasonCodes);
    }

    private static double ComputeJaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0d;
        }

        var intersection = left.Count(token => right.Contains(token));
        if (intersection == 0)
        {
            return 0d;
        }

        var union = left.Count + right.Count - intersection;
        return union <= 0 ? 0d : intersection / (double)union;
    }
}
