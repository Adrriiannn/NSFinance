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
    private static readonly HashSet<string> DangerousFamilyRoots = new(StringComparer.Ordinal)
    {
        "amazon",
        "google",
        "apple",
        "microsoft",
        "paypal"
    };
    private static readonly HashSet<string> SafeAliasTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BillingDescriptor",
        "MerchantName",
        "Abbreviation",
        "ProcessorDescriptor",
        "Domain"
    };

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
            logger.LogDebug(
                "Merchant resolution short-circuit exact alias; fuzzy/family paths skipped normalizedDescriptor={NormalizedDescriptor}",
                normalizedDescriptor);
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
        var dangerousFamilyDescriptor = IsDangerousFamilyToken(firstToken);

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

        var scoredCandidates = fuzzyCandidates
            .Select(candidate =>
            {
                var aliasTokens = normalizer.Tokenize(candidate.Alias.NormalizedAliasText);
                var tokenSimilarity = ComputeJaccard(descriptorTokens, aliasTokens);
                var startsWith = normalizedDescriptor.StartsWith(candidate.Alias.NormalizedAliasText, StringComparison.Ordinal)
                                 || candidate.Alias.NormalizedAliasText.StartsWith(normalizedDescriptor, StringComparison.Ordinal);
                var aliasRootToken = aliasTokens.FirstOrDefault();
                var dangerousFamilyAlias = IsDangerousFamilyToken(aliasRootToken);
                var score = (tokenSimilarity * 0.72d)
                            + (Math.Clamp(candidate.Alias.Confidence, 0d, 1d) * 0.18d)
                            + (candidate.Alias.IsExactMatchPreferred ? 0.08d : 0d)
                            + (startsWith ? 0.06d : 0d);

                return new
                {
                    candidate.Alias,
                    candidate.MerchantUsageType,
                    Score = Math.Clamp(score, 0d, 1d),
                    TokenSimilarity = tokenSimilarity,
                    StartsWith = startsWith,
                    DangerousFamilyAlias = dangerousFamilyAlias
                };
            })
            .ToList();

        var best = scoredCandidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.TokenSimilarity)
            .FirstOrDefault();

        if (scoredCandidates.Count > 0)
        {
            var topDiagnostics = scoredCandidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.TokenSimilarity)
                .Take(3)
                .Select(x => $"alias={x.Alias.NormalizedAliasText}|score={x.Score:0.000}|token={x.TokenSimilarity:0.000}|startsWith={x.StartsWith}|usage={x.MerchantUsageType}|danger={x.DangerousFamilyAlias}")
                .ToArray();
            logger.LogDebug(
                "Merchant fuzzy candidate diagnostics normalizedDescriptor={NormalizedDescriptor} descriptorDangerous={DescriptorDangerous} candidates={Candidates}",
                normalizedDescriptor,
                dangerousFamilyDescriptor,
                string.Join(" || ", topDiagnostics));
        }

        var minimumScore = FuzzyAcceptanceThreshold;
        var minimumTokenSimilarity = 0.72d;
        if (dangerousFamilyDescriptor)
        {
            minimumScore = 0.90d;
            minimumTokenSimilarity = 0.86d;
        }

        var dangerousFamilyGuardFailed = best is not null
                                         && (dangerousFamilyDescriptor || best.DangerousFamilyAlias)
                                         && (!best.StartsWith
                                             || best.TokenSimilarity < 0.90d
                                             || best.MerchantUsageType == MerchantUsageType.MixedUse);

        if (best is null
            || best.Score < minimumScore
            || best.TokenSimilarity < minimumTokenSimilarity
            || dangerousFamilyGuardFailed)
        {
            var reasons = new List<string> { "fuzzy_alias_not_found" };
            if (dangerousFamilyDescriptor || best?.DangerousFamilyAlias == true)
            {
                reasons.Add("fuzzy_alias_dangerous_family_guard");
            }

            logger.LogInformation(
                "Merchant fuzzy resolution fallback to unresolved normalizedDescriptor={NormalizedDescriptor} bestScore={BestScore} bestTokenSimilarity={BestTokenSimilarity} dangerousGuardFailed={DangerousGuardFailed} minScore={MinimumScore} minToken={MinimumToken}",
                normalizedDescriptor,
                best?.Score,
                best?.TokenSimilarity,
                dangerousFamilyGuardFailed,
                minimumScore,
                minimumTokenSimilarity);

            return UnresolvedResult(normalizedDescriptor, reasons);
        }

        logger.LogDebug(
            "Merchant fuzzy resolution selected merchantId={MerchantId} alias={Alias} score={Score} tokenSimilarity={TokenSimilarity} descriptorDangerous={DescriptorDangerous}",
            best.Alias.MerchantId,
            best.Alias.NormalizedAliasText,
            best.Score,
            best.TokenSimilarity,
            dangerousFamilyDescriptor);

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
        var dangerousFamilyDescriptor = IsDangerousFamilyToken(firstToken);

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

        var scoredFamilyCandidates = familyCandidates
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
                    candidate.NormalizedCanonicalName,
                    Score = Math.Clamp(score, 0d, 1d),
                    Similarity = similarity
                };
            })
            .ToList();

        var best = scoredFamilyCandidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Similarity)
            .FirstOrDefault();

        if (scoredFamilyCandidates.Count > 0)
        {
            var topDiagnostics = scoredFamilyCandidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Similarity)
                .Take(3)
                .Select(x => $"canonical={x.NormalizedCanonicalName}|score={x.Score:0.000}|similarity={x.Similarity:0.000}")
                .ToArray();
            logger.LogDebug(
                "Merchant family candidate diagnostics normalizedDescriptor={NormalizedDescriptor} descriptorDangerous={DescriptorDangerous} candidates={Candidates}",
                normalizedDescriptor,
                dangerousFamilyDescriptor,
                string.Join(" || ", topDiagnostics));
        }

        var dangerousFamilyGuardFailed = dangerousFamilyDescriptor
                                         && best is not null
                                         && !string.Equals(best.NormalizedCanonicalName, normalizedDescriptor, StringComparison.Ordinal);

        if (best is null || best.Score < FamilyAcceptanceThreshold || best.Similarity < 0.75d || dangerousFamilyGuardFailed)
        {
            var reasons = new List<string> { "family_match_not_found" };
            if (dangerousFamilyGuardFailed)
            {
                reasons.Add("family_match_dangerous_family_guard");
            }

            logger.LogInformation(
                "Merchant family resolution fallback to unresolved normalizedDescriptor={NormalizedDescriptor} bestScore={BestScore} bestSimilarity={BestSimilarity} dangerousGuardFailed={DangerousGuardFailed}",
                normalizedDescriptor,
                best?.Score,
                best?.Similarity,
                dangerousFamilyGuardFailed);

            return UnresolvedResult(normalizedDescriptor, reasons);
        }

        logger.LogDebug(
            "Merchant family resolution selected merchantId={MerchantId} canonical={Canonical} score={Score} similarity={Similarity}",
            best.Id,
            best.CanonicalName,
            best.Score,
            best.Similarity);

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
        logger.LogInformation(
            "Merchant investigation decision normalizedDescriptor={NormalizedDescriptor} unresolvedId={UnresolvedMerchantId} recommendation={Recommendation} candidates={CandidateCount} overallConfidence={OverallConfidence} ambiguity={AmbiguityLevel} parserRejected={ParserRejected} decision={Decision} reasonCodes={ReasonCodes}",
            normalizedDescriptor,
            unresolved.Id,
            investigationResult.Recommendation,
            investigationResult.Candidates.Count,
            investigationResult.OverallConfidence,
            investigationResult.AmbiguityLevel,
            investigationResult.ParserRejected,
            decision.DecisionType,
            string.Join(",", decision.ReasonCodes));

        if (decision.DecisionType is MerchantAcceptanceDecisionType.AcceptedTrusted or MerchantAcceptanceDecisionType.AcceptedCautious
            && decision.SelectedCandidate is not null)
        {
            try
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
            catch (Exception ex)
            {
                unresolved.Status = UnresolvedMerchantStatus.AwaitingEvidence;
                unresolved.Notes = "apply_accepted_investigation_failed";
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogError(
                    ex,
                    "Merchant resolution apply-accepted failed normalizedDescriptor={NormalizedDescriptor} unresolvedId={UnresolvedMerchantId} decision={Decision}",
                    normalizedDescriptor,
                    unresolved.Id,
                    decision.DecisionType);

                return new MerchantResolutionResult(
                    MerchantId: null,
                    ResolutionConfidence: Math.Round(decision.Confidence, 4, MidpointRounding.AwayFromZero),
                    ResolutionType: MerchantResolutionType.None,
                    MatchedAlias: null,
                    IsResolved: false,
                    UnresolvedMerchantId: unresolved.Id,
                    NormalizedDescriptor: normalizedDescriptor,
                    AcceptanceDecisionType: MerchantAcceptanceDecisionType.Rejected,
                    ReasonCodes: ["apply_accepted_investigation_failed"]);
            }
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

        var autoAttachedAliases = 0;
        var skippedAliases = 0;
        foreach (var suggestion in investigationResult.AliasSuggestions ?? [])
        {
            if (!IsSafeAliasSuggestion(suggestion))
            {
                skippedAliases += 1;
                continue;
            }

            await merchantRegistryService.AttachAliasAsync(
                new MerchantAliasCreateRequest(
                    MerchantId: merchant.Id,
                    AliasText: suggestion.AliasText,
                    AliasType: MapAliasType(suggestion.AliasType),
                    Confidence: Math.Clamp(suggestion.Confidence, 0.55d, 0.93d),
                    IsExactMatchPreferred: false,
                    Source: $"investigation:{decision.DecisionType}:alias_suggestion",
                    IsActive: true),
                cancellationToken);
            autoAttachedAliases += 1;
        }

        foreach (var evidence in investigationResult.Evidence)
        {
            await merchantRegistryService.AddEvidenceAsync(
                new MerchantEvidenceCreateRequest(
                    MerchantId: merchant.Id,
                    EvidenceType: evidence.EvidenceType,
                    EvidenceSummary: evidence.EvidenceSummary,
                    Confidence: evidence.Confidence,
                    SourceReference: string.IsNullOrWhiteSpace(evidence.SourceClass)
                        ? evidence.SourceReference
                        : $"{evidence.SourceClass}|{evidence.SourceReference}"),
                cancellationToken);
        }

        unresolved.Status = UnresolvedMerchantStatus.Resolved;
        unresolved.Notes = $"resolved:{merchant.Id:N}";
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Merchant resolved through investigation normalizedDescriptor={NormalizedDescriptor} merchantId={MerchantId} decision={Decision} aliasesAutoAttached={AliasesAutoAttached} aliasesSkipped={AliasesSkipped}",
            normalizedDescriptor,
            merchant.Id,
            decision.DecisionType,
            autoAttachedAliases,
            skippedAliases);

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

    private static bool IsDangerousFamilyToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return DangerousFamilyRoots.Contains(token.Trim().ToLowerInvariant());
    }

    private static MerchantAliasType MapAliasType(string aliasType)
    {
        return Enum.TryParse<MerchantAliasType>(aliasType, ignoreCase: true, out var parsed)
            ? parsed
            : MerchantAliasType.BillingDescriptor;
    }

    private static bool IsSafeAliasSuggestion(MerchantInvestigationAliasSuggestion suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion.AliasText)
            || suggestion.AliasText.Length < 6
            || suggestion.Confidence < 0.80d)
        {
            return false;
        }

        if (!SafeAliasTypes.Contains(suggestion.AliasType))
        {
            return false;
        }

        return !IsBroadDangerousAliasText(suggestion.AliasText);
    }

    private static bool IsBroadDangerousAliasText(string aliasText)
    {
        var normalized = aliasText.Trim().ToLowerInvariant();
        if (DangerousFamilyRoots.Contains(normalized))
        {
            return true;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 1 && DangerousFamilyRoots.Contains(tokens[0]);
    }
}
