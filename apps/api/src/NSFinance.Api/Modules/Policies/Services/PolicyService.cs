using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Policies.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Policies.Services;

public sealed class PolicyService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    IAuditService auditService)
{
    public async Task<IReadOnlyList<PolicyVersionDto>> GetActivePoliciesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.PolicyVersions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.PolicyDocument!.PolicyType)
            .Select(x => new PolicyVersionDto(
                x.PolicyDocument!.PolicyType,
                x.PolicyDocument.Name,
                x.Version,
                x.EffectiveUtc,
                x.ContentReference,
                x.ContentMarkdown,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<ServiceResult<PolicyVersionDto>> GetActivePolicyByTypeAsync(string policyType, CancellationToken cancellationToken)
    {
        var normalizedType = policyType.Trim().ToLowerInvariant();
        var version = await dbContext.PolicyVersions
            .AsNoTracking()
            .Where(x => x.IsActive && x.PolicyDocument!.PolicyType == normalizedType)
            .Select(x => new PolicyVersionDto(
                x.PolicyDocument!.PolicyType,
                x.PolicyDocument.Name,
                x.Version,
                x.EffectiveUtc,
                x.ContentReference,
                x.ContentMarkdown,
                x.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

        return version is null
            ? ServiceResult<PolicyVersionDto>.Fail("Policy not found.", "policy_not_found", StatusCodes.Status404NotFound)
            : ServiceResult<PolicyVersionDto>.Ok(version);
    }

    public async Task<ServiceResult<IReadOnlyList<PolicyAcceptanceDto>>> GetAcceptancesAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<IReadOnlyList<PolicyAcceptanceDto>>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var acceptances = await dbContext.PolicyAcceptances
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.AcceptedUtc)
            .Select(x => new PolicyAcceptanceDto(
                x.PolicyType,
                x.PolicyVersion,
                x.AcceptedUtc,
                x.AcceptanceContext,
                x.Platform,
                x.AppVersion))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<PolicyAcceptanceDto>>.Ok(acceptances);
    }

    public async Task<ServiceResult<PolicyAcceptanceDto>> AcceptPolicyAsync(
        AcceptPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<PolicyAcceptanceDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var normalizedType = request.PolicyType.Trim().ToLowerInvariant();
        var normalizedVersion = request.PolicyVersion.Trim();

        var policyVersion = await dbContext.PolicyVersions
            .Include(x => x.PolicyDocument)
            .SingleOrDefaultAsync(
                x => x.PolicyDocument!.PolicyType == normalizedType && x.Version == normalizedVersion,
                cancellationToken);

        if (policyVersion is null)
        {
            return ServiceResult<PolicyAcceptanceDto>.Fail("Policy version was not found.", "policy_version_not_found", StatusCodes.Status404NotFound);
        }

        var now = DateTime.UtcNow;
        var acceptance = await dbContext.PolicyAcceptances
            .SingleOrDefaultAsync(x => x.UserId == userId && x.PolicyVersionId == policyVersion.Id, cancellationToken);

        if (acceptance is null)
        {
            acceptance = new PolicyAcceptance
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PolicyVersionId = policyVersion.Id,
                PolicyType = normalizedType,
                PolicyVersion = normalizedVersion,
                AcceptedUtc = now,
                AcceptanceContext = request.AcceptanceContext.Trim(),
                Platform = NormalizeNullable(request.Platform),
                AppVersion = NormalizeNullable(request.AppVersion)
            };

            dbContext.PolicyAcceptances.Add(acceptance);
        }
        else
        {
            acceptance.AcceptedUtc = now;
            acceptance.AcceptanceContext = request.AcceptanceContext.Trim();
            acceptance.Platform = NormalizeNullable(request.Platform) ?? acceptance.Platform;
            acceptance.AppVersion = NormalizeNullable(request.AppVersion) ?? acceptance.AppVersion;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "legal",
            eventName: "legal_policy_accepted",
            targetEntityType: "policy_version",
            targetEntityId: policyVersion.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                policyType = normalizedType,
                policyVersion = normalizedVersion,
                acceptanceContext = acceptance.AcceptanceContext
            },
            cancellationToken);

        return ServiceResult<PolicyAcceptanceDto>.Ok(new PolicyAcceptanceDto(
            acceptance.PolicyType,
            acceptance.PolicyVersion,
            acceptance.AcceptedUtc,
            acceptance.AcceptanceContext,
            acceptance.Platform,
            acceptance.AppVersion));
    }

    public async Task<ServiceResult<IReadOnlyList<ConsentRecordDto>>> GetConsentsAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<IReadOnlyList<ConsentRecordDto>>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var consents = await dbContext.ConsentRecords
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.ConsentType)
            .Select(x => new ConsentRecordDto(
                x.ConsentType,
                x.Status,
                x.UpdatedUtc,
                x.GrantedUtc,
                x.RevokedUtc,
                x.Source,
                x.MetadataJson))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<ConsentRecordDto>>.Ok(consents);
    }

    public async Task<ServiceResult<ConsentRecordDto>> UpdateConsentAsync(
        UpdateConsentRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<ConsentRecordDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var now = DateTime.UtcNow;
        var consentType = request.ConsentType.Trim().ToLowerInvariant();
        var status = request.Status.Trim().ToLowerInvariant();
        var source = request.Source.Trim().ToLowerInvariant();

        var consent = await dbContext.ConsentRecords
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ConsentType == consentType, cancellationToken);

        if (consent is null)
        {
            consent = new ConsentRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConsentType = consentType
            };
            dbContext.ConsentRecords.Add(consent);
        }

        consent.Status = status;
        consent.Source = source;
        consent.UpdatedUtc = now;
        consent.MetadataJson = NormalizeNullable(request.MetadataJson);
        consent.GrantedUtc = status == "granted" ? now : consent.GrantedUtc;
        consent.RevokedUtc = status is "revoked" or "denied" ? now : null;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "legal",
            eventName: "consent_updated",
            targetEntityType: "consent",
            targetEntityId: consent.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new { consentType, status, source },
            cancellationToken);

        return ServiceResult<ConsentRecordDto>.Ok(new ConsentRecordDto(
            consent.ConsentType,
            consent.Status,
            consent.UpdatedUtc,
            consent.GrantedUtc,
            consent.RevokedUtc,
            consent.Source,
            consent.MetadataJson));
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
