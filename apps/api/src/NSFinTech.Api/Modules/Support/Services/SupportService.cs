using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Support.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Support.Services;

public sealed class SupportService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    IAuditService auditService)
{
    public async Task<ServiceResult<SupportRequestDto>> CreateSupportRequestAsync(
        CreateSupportRequestRequest request,
        CancellationToken cancellationToken)
    {
        currentUserProvider.TryGetUserId(out var userId);
        var hasUser = userId != Guid.Empty;
        var now = DateTime.UtcNow;

        var supportRequest = new SupportRequest
        {
            Id = Guid.NewGuid(),
            UserId = hasUser ? userId : null,
            Category = request.Category.Trim(),
            Message = request.Message.Trim(),
            Status = "open",
            CreatedUtc = now,
            UpdatedUtc = now
        };

        dbContext.SupportRequests.Add(supportRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "support",
            eventName: "support_request_submitted",
            targetEntityType: "support_request",
            targetEntityId: supportRequest.Id.ToString(),
            actorId: hasUser ? userId : null,
            actorType: hasUser ? "user" : "anonymous",
            metadata: new { supportRequest.Category },
            cancellationToken);

        return ServiceResult<SupportRequestDto>.Ok(new SupportRequestDto(
            supportRequest.Id,
            supportRequest.UserId,
            supportRequest.Category,
            supportRequest.Message,
            supportRequest.Status,
            supportRequest.CreatedUtc,
            supportRequest.UpdatedUtc));
    }

    public async Task<ServiceResult<IReadOnlyList<SupportRequestDto>>> GetMySupportRequestsAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<IReadOnlyList<SupportRequestDto>>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var requests = await dbContext.SupportRequests
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new SupportRequestDto(
                x.Id,
                x.UserId,
                x.Category,
                x.Message,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<SupportRequestDto>>.Ok(requests);
    }

    public async Task<ServiceResult<DeletionRequestDto>> CreateDeletionRequestAsync(
        CreateDeletionRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<DeletionRequestDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<DeletionRequestDto>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        var now = DateTime.UtcNow;
        user.DeletionRequested = true;
        user.DeletionRequestedUtc = now;
        user.UpdatedUtc = now;

        var deletionRequest = new DeletionRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = "requested",
            RequestedUtc = now,
            UpdatedUtc = now,
            Notes = NormalizeNullable(request.Notes)
        };

        dbContext.DeletionRequests.Add(deletionRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "privacy",
            eventName: "deletion_requested",
            targetEntityType: "deletion_request",
            targetEntityId: deletionRequest.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<DeletionRequestDto>.Ok(new DeletionRequestDto(
            deletionRequest.Id,
            deletionRequest.UserId,
            deletionRequest.Status,
            deletionRequest.RequestedUtc,
            deletionRequest.UpdatedUtc,
            deletionRequest.Notes));
    }

    public async Task<ServiceResult<ExportRequestDto>> CreateExportRequestAsync(
        CreateExportRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<ExportRequestDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var now = DateTime.UtcNow;
        var exportRequest = new ExportRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = "requested",
            RequestedUtc = now,
            UpdatedUtc = now,
            Notes = NormalizeNullable(request.Notes)
        };

        dbContext.ExportRequests.Add(exportRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "privacy",
            eventName: "export_requested",
            targetEntityType: "export_request",
            targetEntityId: exportRequest.Id.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<ExportRequestDto>.Ok(new ExportRequestDto(
            exportRequest.Id,
            exportRequest.UserId,
            exportRequest.Status,
            exportRequest.RequestedUtc,
            exportRequest.UpdatedUtc,
            exportRequest.Notes));
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
