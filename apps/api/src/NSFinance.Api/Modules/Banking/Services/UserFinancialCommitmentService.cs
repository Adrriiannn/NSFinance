using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class UserFinancialCommitmentService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    TimeProvider timeProvider,
    IRequestContextAccessor requestContext,
    ILogger<UserFinancialCommitmentService> logger)
{
    private const int MaximumStoredRowsPerRead = 500;

    public async Task<IReadOnlyList<FinancialCommitmentDto>> ApplyAsync(
        IReadOnlyList<FinancialCommitmentDto> liveItems,
        bool includeDismissed,
        CancellationToken cancellationToken)
    {
        var decisions = await dbContext.UserFinancialCommitments
            .AsNoTracking()
            .Where(item => item.UserId == currentUserProvider.UserId)
            .OrderBy(item => item.EffectiveNextDateUtc == null)
            .ThenBy(item => item.EffectiveNextDateUtc)
            .ThenByDescending(item => item.UpdatedUtc)
            .ThenBy(item => item.Id)
            .Take(MaximumStoredRowsPerRead + 1)
            .ToListAsync(cancellationToken);
        var decisionByTarget = decisions
            .Where(item => item.TargetCommitmentId != null)
            .ToDictionary(item => item.TargetCommitmentId!, StringComparer.Ordinal);
        var consumedDecisionIds = new HashSet<Guid>();
        var projectedItems = new List<FinancialCommitmentDto>(liveItems.Count + decisions.Count);

        foreach (var liveItem in liveItems)
        {
            if (!decisionByTarget.TryGetValue(liveItem.Id, out var decision))
            {
                projectedItems.Add(liveItem);
                continue;
            }

            consumedDecisionIds.Add(decision.Id);
            if (!TryProject(liveItem, decision, includeDismissed, out var projected))
            {
                continue;
            }

            if (projected is not null)
            {
                projectedItems.Add(projected);
            }
        }

        foreach (var decision in decisions.Where(item => !consumedDecisionIds.Contains(item.Id)))
        {
            if (!TryProject(null, decision, includeDismissed, out var projected))
            {
                continue;
            }

            if (projected is not null)
            {
                projectedItems.Add(projected);
            }
        }

        return projectedItems;
    }

    public async Task<ServiceResult<FinancialCommitmentMutationDto>> CreateManualAsync(
        CreateManualFinancialCommitmentRequest request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var validationError = FinancialCommitmentMutationPolicy.ValidateManualRequest(request, utcNow);
        if (validationError is not null)
        {
            return Fail(validationError);
        }

        var account = await ResolveOwnedAccountAsync(request.AccountId, cancellationToken);
        if (request.AccountId.HasValue && account is null)
        {
            return ServiceResult<FinancialCommitmentMutationDto>.Fail(
                "Account not found.",
                "commitment_account_not_found",
                StatusCodes.Status404NotFound);
        }

        var currency = FinancialCommitmentContractPolicy.NormalizeCurrency(request.Currency)
            ?? account?.Currency;
        if (request.NextAmount.HasValue && currency is null)
        {
            return Invalid("Currency is required when an amount is provided.", "commitment_currency_required");
        }

        if (currency is not null && !FinancialCommitmentMutationPolicy.IsValidCurrency(currency))
        {
            return Invalid("Currency must be a three-letter ISO code.", "commitment_currency_invalid");
        }

        var id = Guid.NewGuid();
        var entity = ManualFinancialCommitmentFactory.Create(
            id,
            currentUserProvider.UserId,
            request,
            account,
            currency,
            utcNow);

        dbContext.UserFinancialCommitments.Add(entity);
        AddAuditEvent(entity, "manual_created", ["manual"]);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<FinancialCommitmentMutationDto>.Ok(ToMutationDto(entity));
    }

    public async Task<ServiceResult<FinancialCommitmentMutationDto>> DecideAsync(
        string commitmentId,
        FinancialCommitmentDto? liveSource,
        FinancialCommitmentDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var action = FinancialCommitmentContractPolicy.NormalizeToken(request.Action);
        if (action is not ("confirm" or "correct" or "dismiss" or "reactivate"))
        {
            return Invalid("Action must be confirm, correct, dismiss, or reactivate.", "commitment_action_invalid");
        }

        if (string.IsNullOrWhiteSpace(commitmentId) || commitmentId.Length > 200)
        {
            return Invalid("Commitment ID is invalid.", "commitment_id_invalid");
        }

        var actionPayloadError = FinancialCommitmentMutationPolicy.ValidateActionPayload(action, request);
        if (actionPayloadError is not null)
        {
            return Fail(actionPayloadError);
        }

        var manualId = FinancialCommitmentMutationPolicy.TryParseManualId(commitmentId);
        var entity = manualId.HasValue
            ? await dbContext.UserFinancialCommitments.SingleOrDefaultAsync(
                item => item.Id == manualId.Value
                    && item.UserId == currentUserProvider.UserId
                    && item.OriginType == "manual",
                cancellationToken)
            : await dbContext.UserFinancialCommitments.SingleOrDefaultAsync(
                item => item.UserId == currentUserProvider.UserId
                    && item.TargetCommitmentId == commitmentId,
                cancellationToken);

        if (manualId.HasValue && entity is null)
        {
            return NotFound();
        }

        if (entity is null && liveSource is null)
        {
            return NotFound();
        }

        if (entity is not null)
        {
            if (!request.ExpectedRevision.HasValue)
            {
                return ServiceResult<FinancialCommitmentMutationDto>.Fail(
                    "Expected revision is required.",
                    "commitment_revision_required",
                    StatusCodes.Status409Conflict);
            }

            if (request.ExpectedRevision.Value != entity.Revision)
            {
                return Conflict();
            }
        }
        else if (request.ExpectedRevision.HasValue)
        {
            return Conflict();
        }

        if (action == "confirm" && (manualId.HasValue || liveSource?.Source != "inferred"))
        {
            return Invalid(
                "Only inferred commitments can be confirmed.",
                "commitment_confirmation_invalid");
        }

        if (action == "reactivate" && entity?.State != "dismissed")
        {
            return Invalid(
                "Only a dismissed commitment can be reactivated.",
                "commitment_reactivation_invalid");
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        entity ??= new UserFinancialCommitment
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            TargetCommitmentId = commitmentId,
            OriginType = "decision",
            State = "active",
            DecisionMode = action switch
            {
                "correct" => "corrected",
                "confirm" => "confirmed",
                _ => "none"
            },
            SnapshotJson = UserFinancialCommitmentProjector.SerializeSnapshot(liveSource!),
            Revision = 0,
            CreatedUtc = utcNow
        };

        if (liveSource is not null)
        {
            entity.SnapshotJson = UserFinancialCommitmentProjector.SerializeSnapshot(liveSource);
        }

        if (action == "correct")
        {
            var overrideAccount = request.AccountId.HasValue
                ? await ResolveOwnedAccountAsync(request.AccountId, cancellationToken)
                : null;
            var overrideResult = FinancialCommitmentMutationPolicy.BuildOverride(
                entity.OverrideJson,
                request,
                utcNow,
                overrideAccount);
            if (overrideResult.Error is not null)
            {
                return Fail(overrideResult.Error);
            }

            entity.OverrideJson = UserFinancialCommitmentProjector.SerializeOverride(overrideResult.Document);
            entity.DecisionMode = "corrected";
            entity.ConfirmedUtc ??= utcNow;
        }
        else if (action == "confirm")
        {
            entity.DecisionMode = "confirmed";
            entity.ConfirmedUtc ??= utcNow;
        }

        entity.State = action == "dismiss" ? "dismissed" : "active";
        entity.DismissedUtc = action == "dismiss" ? utcNow : null;
        entity.LastAction = action;
        entity.Revision++;
        entity.UpdatedUtc = utcNow;

        if (!UserFinancialCommitmentProjector.TryProject(
                liveSource,
                entity,
                true,
                out var effective)
            || effective is null)
        {
            return ServiceResult<FinancialCommitmentMutationDto>.Fail(
                "Stored commitment state is invalid.",
                "commitment_state_invalid",
                StatusCodes.Status500InternalServerError);
        }

        entity.EffectiveAccountId = effective.AccountId;
        entity.EffectiveNextDateUtc = effective.NextDateUtc;
        if (dbContext.Entry(entity).State == EntityState.Detached)
        {
            dbContext.UserFinancialCommitments.Add(entity);
        }

        AddAuditEvent(
            entity,
            $"decision_{action}",
            FinancialCommitmentMutationPolicy.ChangedFields(request, action));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict();
        }

        return ServiceResult<FinancialCommitmentMutationDto>.Ok(ToMutationDto(entity));
    }

    private async Task<FinancialCommitmentOwnedAccount?> ResolveOwnedAccountAsync(
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        if (!accountId.HasValue)
        {
            return null;
        }

        return await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(account => account.Id == accountId.Value
                && account.UserId == currentUserProvider.UserId)
            .Select(account => new FinancialCommitmentOwnedAccount(
                account.Id,
                account.Name,
                account.Currency,
                dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == account.Id)
                    .OrderBy(linked => linked.Id)
                    .Select(linked => (Guid?)linked.Id)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private bool TryProject(
        FinancialCommitmentDto? liveSource,
        UserFinancialCommitment decision,
        bool includeDismissed,
        out FinancialCommitmentDto? projected)
    {
        var succeeded = UserFinancialCommitmentProjector.TryProject(
            liveSource,
            decision,
            includeDismissed,
            out projected);
        if (!succeeded)
        {
            logger.LogWarning(
                "User financial commitment state could not be projected for commitmentRowId={CommitmentRowId}",
                decision.Id);
        }

        return succeeded;
    }

    private void AddAuditEvent(
        UserFinancialCommitment entity,
        string eventName,
        IReadOnlyList<string> changedFields)
    {
        dbContext.AuditEvents.Add(AuditEventFactory.Create(
            requestContext,
            "financial_commitment",
            eventName,
            "user_financial_commitment",
            entity.Id.ToString("N"),
            currentUserProvider.UserId,
            "user",
            new
            {
                entity.OriginType,
                entity.DecisionMode,
                entity.State,
                entity.Revision,
                changedFields
            }));
    }

    private static FinancialCommitmentMutationDto ToMutationDto(UserFinancialCommitment entity)
    {
        return new FinancialCommitmentMutationDto(
            entity.TargetCommitmentId ?? $"user_manual:{entity.Id:N}",
            entity.TargetCommitmentId,
            entity.State,
            entity.DecisionMode,
            entity.LastAction,
            entity.Revision,
            FinancialCommitmentContractPolicy.EnsureUtc(entity.UpdatedUtc));
    }

    private static ServiceResult<FinancialCommitmentMutationDto> Invalid(string message, string code)
    {
        return ServiceResult<FinancialCommitmentMutationDto>.Fail(message, code, StatusCodes.Status400BadRequest);
    }

    private static ServiceResult<FinancialCommitmentMutationDto> NotFound()
    {
        return ServiceResult<FinancialCommitmentMutationDto>.Fail(
            "Commitment not found.",
            "commitment_not_found",
            StatusCodes.Status404NotFound);
    }

    private static ServiceResult<FinancialCommitmentMutationDto> Conflict()
    {
        return ServiceResult<FinancialCommitmentMutationDto>.Fail(
            "Commitment changed since it was read.",
            "commitment_revision_conflict",
            StatusCodes.Status409Conflict);
    }

    private static ServiceResult<FinancialCommitmentMutationDto> Fail(ServiceError error)
    {
        return ServiceResult<FinancialCommitmentMutationDto>.Fail(error.Message, error.Code, error.StatusCode);
    }

}
