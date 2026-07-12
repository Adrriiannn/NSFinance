using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Support.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Support.Services;

public sealed class SupportService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    IAuditService auditService,
    IRequestContextAccessor requestContext,
    IdentityChallengeService identityChallengeService,
    ILogger<SupportService> logger)
{
    private static readonly TimeSpan ExportRetentionWindow = TimeSpan.FromMinutes(15);
    private const string ExportFormatXlsx = "xlsx";

    public async Task<ServiceResult<SupportRequestDto>> CreateSupportRequestAsync(
        CreateSupportRequestRequest request,
        CancellationToken cancellationToken)
    {
        currentUserProvider.TryGetUserId(out var userId);
        currentUserProvider.TryGetSessionId(out var sessionId);
        var hasUser = userId != Guid.Empty;
        var now = DateTime.UtcNow;
        var requestId = Guid.NewGuid();
        var screenshotReference = await StoreSupportAttachmentsAsync(
            requestId,
            request.Screenshots,
            cancellationToken);
        var diagnosticsJson = await BuildDiagnosticsAsync(
            hasUser ? userId : null,
            sessionId == Guid.Empty ? null : sessionId,
            request.ConnectionId,
            request.LinkedBankAccountId,
            cancellationToken);

        var supportRequest = new SupportRequest
        {
            Id = requestId,
            UserId = hasUser ? userId : null,
            Category = request.Category.Trim(),
            Subcategory = request.Subcategory.Trim(),
            Title = request.Title.Trim(),
            Message = request.Message.Trim(),
            ContactEmail = NormalizeNullable(request.ContactEmail),
            ScreenshotReference = screenshotReference,
            ConnectionId = request.ConnectionId,
            LinkedBankAccountId = request.LinkedBankAccountId,
            DiagnosticsJson = diagnosticsJson,
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
            metadata: new { supportRequest.Category, supportRequest.Subcategory },
            cancellationToken);

        return ServiceResult<SupportRequestDto>.Ok(new SupportRequestDto(
            supportRequest.Id,
            supportRequest.UserId,
            supportRequest.Category,
            supportRequest.Subcategory,
            supportRequest.Title,
            supportRequest.Message,
            supportRequest.ContactEmail,
            supportRequest.ScreenshotReference,
            supportRequest.ConnectionId,
            supportRequest.LinkedBankAccountId,
            supportRequest.DiagnosticsJson,
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
                x.Subcategory,
                x.Title,
                x.Message,
                x.ContactEmail,
                x.ScreenshotReference,
                x.ConnectionId,
                x.LinkedBankAccountId,
                x.DiagnosticsJson,
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

        var verificationResult = await ValidateDeletionVerificationCodeAsync(
            userId,
            request.ChallengeId,
            request.Code,
            cancellationToken);
        if (!verificationResult.Succeeded)
        {
            return ServiceResult<DeletionRequestDto>.Fail(
                verificationResult.Error!.Message,
                verificationResult.Error.Code,
                verificationResult.Error.StatusCode);
        }

        var now = DateTime.UtcNow;
        user.DeletionRequested = true;
        user.DeletionRequestedUtc = now;
        user.Status = "deletion_requested";
        user.IsDisabled = true;
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

        await RevokeAllSessionsForDeletionAsync(userId, now, cancellationToken);
        await DisconnectBankingAndRemoveActiveFinancialDataAsync(userId, now, cancellationToken);
        await RemoveSupportAndPreferenceArtifactsAsync(userId, cancellationToken);

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

        await ExpireReadyExportsAsync(userId, cancellationToken);

        var now = DateTime.UtcNow;
        var format = NormalizeExportFormat(request.Format);
        if (!string.Equals(format, ExportFormatXlsx, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<ExportRequestDto>.Fail(
                "Only XLSX export format is currently supported.",
                "unsupported_export_format",
                StatusCodes.Status400BadRequest);
        }

        ServiceResult<ExportArtifactResult> artifactResult;
        try
        {
            artifactResult = await BuildExportPackageAsync(userId, request, format, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create data export package for user {UserId}", userId);
            return ServiceResult<ExportRequestDto>.Fail(
                "We could not generate your data export package right now.",
                "export_generation_failed",
                StatusCodes.Status500InternalServerError);
        }

        if (!artifactResult.Succeeded)
        {
            return ServiceResult<ExportRequestDto>.Fail(
                artifactResult.Error!.Message,
            artifactResult.Error.Code,
            artifactResult.Error.StatusCode);
        }

        var selectedConnectionLabel = await ResolveConnectionLabelAsync(userId, request.ConnectionId, cancellationToken);

        var normalizedDates = NormalizeDateRange(request.StartDate, request.EndDate);

        var existingRequests = await dbContext.ExportRequests
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RequestedUtc)
            .ToListAsync(cancellationToken);

        ExportRequest exportRequest;
        if (existingRequests.Count == 0)
        {
            exportRequest = new ExportRequest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = "ready",
                RequestedUtc = now,
                UpdatedUtc = now,
                Format = format,
                ConnectionId = request.ConnectionId,
                ConnectionLabel = selectedConnectionLabel,
                FinancialAccountId = request.FinancialAccountId,
                StartDate = normalizedDates.StartDate,
                EndDate = normalizedDates.EndDate,
                PeriodPreset = NormalizeNullable(request.PeriodPreset),
                FileSizeBytes = artifactResult.Value!.FileSizeBytes,
                Notes = NormalizeNullable(request.Notes),
                ArtifactReference = artifactResult.Value!.FilePath
            };
            dbContext.ExportRequests.Add(exportRequest);
        }
        else
        {
            exportRequest = existingRequests[0];
            var previousArtifact = exportRequest.ArtifactReference;
            exportRequest.Status = "ready";
            exportRequest.RequestedUtc = now;
            exportRequest.UpdatedUtc = now;
            exportRequest.Format = format;
            exportRequest.ConnectionId = request.ConnectionId;
            exportRequest.ConnectionLabel = selectedConnectionLabel;
            exportRequest.FinancialAccountId = request.FinancialAccountId;
            exportRequest.StartDate = normalizedDates.StartDate;
            exportRequest.EndDate = normalizedDates.EndDate;
            exportRequest.PeriodPreset = NormalizeNullable(request.PeriodPreset);
            exportRequest.FileSizeBytes = artifactResult.Value!.FileSizeBytes;
            exportRequest.Notes = NormalizeNullable(request.Notes);
            exportRequest.ArtifactReference = artifactResult.Value!.FilePath;

            if (!string.IsNullOrWhiteSpace(previousArtifact)
                && !string.Equals(previousArtifact, artifactResult.Value!.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(previousArtifact);
            }

            if (existingRequests.Count > 1)
            {
                foreach (var duplicate in existingRequests.Skip(1))
                {
                    if (!string.IsNullOrWhiteSpace(duplicate.ArtifactReference))
                    {
                        TryDeleteFile(duplicate.ArtifactReference);
                    }
                }

                dbContext.ExportRequests.RemoveRange(existingRequests.Skip(1));
            }
        }

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

        return ServiceResult<ExportRequestDto>.Ok(ToExportRequestDto(exportRequest));
    }

    public async Task<ServiceResult<IReadOnlyList<ExportRequestDto>>> GetMyExportRequestsAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<IReadOnlyList<ExportRequestDto>>.Fail(
                "Unauthorized.",
                "unauthorized",
                StatusCodes.Status401Unauthorized);
        }

        await ExpireReadyExportsAsync(userId, cancellationToken);

        var allRequests = await dbContext.ExportRequests
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RequestedUtc)
            .ToListAsync(cancellationToken);

        if (allRequests.Count > 1)
        {
            foreach (var duplicate in allRequests.Skip(1))
            {
                if (!string.IsNullOrWhiteSpace(duplicate.ArtifactReference))
                {
                    TryDeleteFile(duplicate.ArtifactReference);
                }
            }

            dbContext.ExportRequests.RemoveRange(allRequests.Skip(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            allRequests = allRequests.Take(1).ToList();
        }

        var requests = allRequests
            .Take(1)
            .Select(ToExportRequestDto)
            .ToList();

        return ServiceResult<IReadOnlyList<ExportRequestDto>>.Ok(requests);
    }

    public async Task<ServiceResult<IReadOnlyList<DeletionRequestDto>>> GetMyDeletionRequestsAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<IReadOnlyList<DeletionRequestDto>>.Fail(
                "Unauthorized.",
                "unauthorized",
                StatusCodes.Status401Unauthorized);
        }

        var requests = await dbContext.DeletionRequests
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RequestedUtc)
            .Select(x => new DeletionRequestDto(
                x.Id,
                x.UserId,
                x.Status,
                x.RequestedUtc,
                x.UpdatedUtc,
                x.Notes))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<DeletionRequestDto>>.Ok(requests);
    }

    public async Task<ServiceResult<ExportDownloadPayload>> DownloadExportRequestAsync(
        Guid exportRequestId,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<ExportDownloadPayload>.Fail(
                "Unauthorized.",
                "unauthorized",
                StatusCodes.Status401Unauthorized);
        }

        var request = await dbContext.ExportRequests
            .SingleOrDefaultAsync(
                x => x.Id == exportRequestId && x.UserId == userId,
                cancellationToken);

        if (request is null)
        {
            return ServiceResult<ExportDownloadPayload>.Fail(
                "Export request not found.",
                "export_request_not_found",
                StatusCodes.Status404NotFound);
        }

        var now = DateTime.UtcNow;
        if (IsExpiredReadyExport(request, now))
        {
            ExpireExportRequest(request, now);
            await dbContext.SaveChangesAsync(cancellationToken);

            return ServiceResult<ExportDownloadPayload>.Fail(
                "Export package expired. Request a new export package.",
                "export_expired",
                StatusCodes.Status410Gone);
        }

        if (!string.Equals(request.Status, "ready", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.ArtifactReference))
        {
            return ServiceResult<ExportDownloadPayload>.Fail(
                "Export package is not ready yet.",
                "export_not_ready",
                StatusCodes.Status409Conflict);
        }

        if (!File.Exists(request.ArtifactReference))
        {
            request.Status = "expired";
            request.UpdatedUtc = now;
            request.ArtifactReference = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            return ServiceResult<ExportDownloadPayload>.Fail(
                "Export file no longer exists.",
                "export_artifact_missing",
                StatusCodes.Status410Gone);
        }

        var fileBytes = await File.ReadAllBytesAsync(request.ArtifactReference, cancellationToken);
        var fileName = Path.GetFileName(request.ArtifactReference);
        var requestFormat = NormalizeExportFormat(request.Format);
        request.FileSizeBytes = fileBytes.LongLength;
        request.Format = requestFormat;
        request.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ExportDownloadPayload>.Ok(new ExportDownloadPayload(
            fileName,
            ResolveExportContentType(requestFormat),
            fileBytes));
    }

    private sealed record ExportArtifactResult(string FilePath, long FileSizeBytes);

    private sealed record NormalizedDateRange(
        DateOnly? StartDate,
        DateOnly? EndDate,
        DateTime? StartUtc,
        DateTime? EndUtc);

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeExportFormat(string? requestedFormat)
    {
        if (string.IsNullOrWhiteSpace(requestedFormat))
        {
            return ExportFormatXlsx;
        }

        return requestedFormat.Trim().ToLowerInvariant();
    }

    private static string ResolveExportContentType(string format)
    {
        return string.Equals(format, ExportFormatXlsx, StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/octet-stream";
    }

    private static NormalizedDateRange NormalizeDateRange(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate.HasValue && !endDate.HasValue)
        {
            endDate = startDate;
        }
        else if (!startDate.HasValue && endDate.HasValue)
        {
            startDate = endDate;
        }

        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            (startDate, endDate) = (endDate, startDate);
        }

        var startUtc = startDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = endDate?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        return new NormalizedDateRange(startDate, endDate, startUtc, endUtc);
    }

    private async Task<string?> ResolveConnectionLabelAsync(
        Guid userId,
        Guid? connectionId,
        CancellationToken cancellationToken)
    {
        if (!connectionId.HasValue)
        {
            return null;
        }

        return await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Id == connectionId.Value)
            .Select(x => x.ProviderDisplayName ?? x.ProviderName)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static ExportRequestDto ToExportRequestDto(ExportRequest request)
    {
        var format = NormalizeExportFormat(request.Format);
        return new ExportRequestDto(
            request.Id,
            request.UserId,
            request.Status,
            request.RequestedUtc,
            request.UpdatedUtc,
            request.Notes,
            format,
            request.ConnectionId,
            request.ConnectionLabel,
            request.FinancialAccountId,
            request.StartDate,
            request.EndDate,
            request.PeriodPreset,
            request.FileSizeBytes);
    }

    private static bool IsExpiredReadyExport(ExportRequest request, DateTime nowUtc)
    {
        if (!string.Equals(request.Status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return request.RequestedUtc <= nowUtc.Subtract(ExportRetentionWindow);
    }

    private void ExpireExportRequest(ExportRequest request, DateTime nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(request.ArtifactReference))
        {
            TryDeleteFile(request.ArtifactReference);
        }

        request.Status = "expired";
        request.ArtifactReference = null;
        request.FileSizeBytes = null;
        request.UpdatedUtc = nowUtc;
    }

    private async Task ExpireReadyExportsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var candidates = await dbContext.ExportRequests
            .Where(x => x.UserId == userId && x.Status == "ready")
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var request in candidates)
        {
            if (!IsExpiredReadyExport(request, now))
            {
                continue;
            }

            ExpireExportRequest(request, now);
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete export artifact file {Path}", path);
        }
    }

    private async Task<ServiceResult<ExportArtifactResult>> BuildExportPackageAsync(
        Guid userId,
        CreateExportRequestRequest request,
        string format,
        CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.Id == userId, cancellationToken);

        if (!userExists)
        {
            return ServiceResult<ExportArtifactResult>.Fail(
                "User not found.",
                "user_not_found",
                StatusCodes.Status404NotFound);
        }

        if (request.ConnectionId.HasValue)
        {
            var hasConnection = await dbContext.OpenBankingConnections
                .AsNoTracking()
                .AnyAsync(
                    x => x.UserId == userId && x.Id == request.ConnectionId.Value,
                    cancellationToken);

            if (!hasConnection)
            {
                return ServiceResult<ExportArtifactResult>.Fail(
                    "Selected bank connection was not found.",
                    "export_connection_not_found",
                    StatusCodes.Status400BadRequest);
            }
        }

        if (request.FinancialAccountId.HasValue)
        {
            var hasFinancialAccount = await dbContext.FinancialAccounts
                .AsNoTracking()
                .AnyAsync(
                    x => x.UserId == userId && x.Id == request.FinancialAccountId.Value,
                    cancellationToken);

            if (!hasFinancialAccount)
            {
                return ServiceResult<ExportArtifactResult>.Fail(
                    "Selected account was not found.",
                    "export_account_not_found",
                    StatusCodes.Status400BadRequest);
            }
        }

        var normalizedDates = NormalizeDateRange(request.StartDate, request.EndDate);

        var connectionsQuery = dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (request.ConnectionId.HasValue)
        {
            connectionsQuery = connectionsQuery.Where(x => x.Id == request.ConnectionId.Value);
        }

        var connections = await connectionsQuery
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new
            {
                x.Id,
                Label = x.ProviderDisplayName ?? x.ProviderName
            })
            .ToListAsync(cancellationToken);

        var linkedAccountsQuery = dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => x.Connection != null && x.Connection.UserId == userId);

        if (request.ConnectionId.HasValue)
        {
            linkedAccountsQuery = linkedAccountsQuery.Where(x => x.ConnectionId == request.ConnectionId.Value);
        }

        var linkedAccounts = await linkedAccountsQuery
            .Select(x => new
            {
                x.Id,
                x.ConnectionId,
                x.DisplayName,
                x.FinancialAccountId,
                ConnectionLabel = x.Connection!.ProviderDisplayName ?? x.Connection.ProviderName
            })
            .ToListAsync(cancellationToken);

        var financialAccountsQuery = dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (request.FinancialAccountId.HasValue)
        {
            financialAccountsQuery = financialAccountsQuery.Where(x => x.Id == request.FinancialAccountId.Value);
        }
        else if (request.ConnectionId.HasValue)
        {
            var linkedFinancialAccountIds = linkedAccounts
                .Where(x => x.FinancialAccountId.HasValue)
                .Select(x => x.FinancialAccountId!.Value)
                .Distinct()
                .ToList();

            if (linkedFinancialAccountIds.Count == 0)
            {
                financialAccountsQuery = financialAccountsQuery.Where(_ => false);
            }
            else
            {
                financialAccountsQuery = financialAccountsQuery.Where(x => linkedFinancialAccountIds.Contains(x.Id));
            }
        }

        var financialAccounts = await financialAccountsQuery
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        var financialAccountIds = financialAccounts.Select(x => x.Id).ToList();

        var transactionsQuery = dbContext.Transactions
            .AsNoTracking()
            .Where(x => financialAccountIds.Contains(x.FinancialAccountId));

        if (normalizedDates.StartUtc.HasValue)
        {
            transactionsQuery = transactionsQuery.Where(x => x.BookedAtUtc >= normalizedDates.StartUtc.Value);
        }

        if (normalizedDates.EndUtc.HasValue)
        {
            transactionsQuery = transactionsQuery.Where(x => x.BookedAtUtc <= normalizedDates.EndUtc.Value);
        }

        var transactions = await transactionsQuery
            .OrderByDescending(x => x.BookedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.FinancialAccountId,
                x.Description,
                x.Amount,
                x.Currency,
                x.BookedAtUtc,
                CategoryName = x.Category != null ? x.Category.Name : null,
                x.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        var bankByAccountId = linkedAccounts
            .Where(x => x.FinancialAccountId.HasValue)
            .GroupBy(x => x.FinancialAccountId!.Value)
            .ToDictionary(
                x => x.Key,
                x => x.Select(item => item.ConnectionLabel).FirstOrDefault() ?? "Not linked");

        var accountById = financialAccounts.ToDictionary(x => x.Id, x => x);

        using var workbook = new XLWorkbook();

        var detailsSheet = workbook.Worksheets.Add("Export details");
        detailsSheet.Cell(1, 1).Value = "Generated UTC";
        detailsSheet.Cell(1, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        detailsSheet.Cell(2, 1).Value = "Format";
        detailsSheet.Cell(2, 2).Value = format.ToUpperInvariant();
        detailsSheet.Cell(3, 1).Value = "Bank filter";
        detailsSheet.Cell(3, 2).Value = request.ConnectionId.HasValue
            ? connections.Select(x => x.Label).FirstOrDefault() ?? "Selected bank"
            : "All";
        detailsSheet.Cell(4, 1).Value = "Date range";
        detailsSheet.Cell(4, 2).Value = normalizedDates.StartDate.HasValue && normalizedDates.EndDate.HasValue
            ? $"{normalizedDates.StartDate:yyyy-MM-dd} to {normalizedDates.EndDate:yyyy-MM-dd}"
            : "All time";
        detailsSheet.Cell(5, 1).Value = "Period preset";
        detailsSheet.Cell(5, 2).Value = NormalizeNullable(request.PeriodPreset) ?? "Custom";
        detailsSheet.Cell(6, 1).Value = "Accounts included";
        detailsSheet.Cell(6, 2).Value = financialAccounts.Count;
        detailsSheet.Cell(7, 1).Value = "Transactions included";
        detailsSheet.Cell(7, 2).Value = transactions.Count;
        detailsSheet.Range(1, 1, 7, 1).Style.Font.Bold = true;

        var accountsSheet = workbook.Worksheets.Add("Accounts");
        accountsSheet.Cell(1, 1).Value = "Bank";
        accountsSheet.Cell(1, 2).Value = "Account name";
        accountsSheet.Cell(1, 3).Value = "Type";
        accountsSheet.Cell(1, 4).Value = "Currency";
        accountsSheet.Cell(1, 5).Value = "Created (UTC)";
        accountsSheet.Cell(1, 6).Value = "Account ID";

        for (var index = 0; index < financialAccounts.Count; index++)
        {
            var account = financialAccounts[index];
            var row = index + 2;

            accountsSheet.Cell(row, 1).Value = bankByAccountId.TryGetValue(account.Id, out var bankLabel)
                ? bankLabel
                : "Not linked";
            accountsSheet.Cell(row, 2).Value = account.Name;
            accountsSheet.Cell(row, 3).Value = account.Type;
            accountsSheet.Cell(row, 4).Value = account.Currency;
            accountsSheet.Cell(row, 5).Value = account.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");
            accountsSheet.Cell(row, 6).Value = account.Id.ToString();
        }

        var transactionsSheet = workbook.Worksheets.Add("Transactions");
        transactionsSheet.Cell(1, 1).Value = "Bank";
        transactionsSheet.Cell(1, 2).Value = "Account";
        transactionsSheet.Cell(1, 3).Value = "Description";
        transactionsSheet.Cell(1, 4).Value = "Category";
        transactionsSheet.Cell(1, 5).Value = "Amount";
        transactionsSheet.Cell(1, 6).Value = "Currency";
        transactionsSheet.Cell(1, 7).Value = "Booked at (UTC)";
        transactionsSheet.Cell(1, 8).Value = "Created at (UTC)";
        transactionsSheet.Cell(1, 9).Value = "Transaction ID";

        for (var index = 0; index < transactions.Count; index++)
        {
            var transaction = transactions[index];
            var row = index + 2;
            accountById.TryGetValue(transaction.FinancialAccountId, out var sourceAccount);

            transactionsSheet.Cell(row, 1).Value = bankByAccountId.TryGetValue(transaction.FinancialAccountId, out var bankLabel)
                ? bankLabel
                : "Not linked";
            transactionsSheet.Cell(row, 2).Value = sourceAccount?.Name ?? "Unknown account";
            transactionsSheet.Cell(row, 3).Value = transaction.Description;
            transactionsSheet.Cell(row, 4).Value = transaction.CategoryName ?? "Uncategorized";
            transactionsSheet.Cell(row, 5).Value = Convert.ToDouble(transaction.Amount);
            transactionsSheet.Cell(row, 6).Value = transaction.Currency;
            transactionsSheet.Cell(row, 7).Value = transaction.BookedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
            transactionsSheet.Cell(row, 8).Value = transaction.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");
            transactionsSheet.Cell(row, 9).Value = transaction.Id.ToString();
        }

        detailsSheet.Columns().AdjustToContents();
        accountsSheet.Row(1).Style.Font.Bold = true;
        accountsSheet.Columns().AdjustToContents();
        transactionsSheet.Row(1).Style.Font.Bold = true;
        transactionsSheet.Columns().AdjustToContents();

        cancellationToken.ThrowIfCancellationRequested();

        var artifactRoot = Path.Combine(Path.GetTempPath(), "nsfinance-export-artifacts");
        Directory.CreateDirectory(artifactRoot);
        var fileName = $"nsfinance-statements-{userId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
        var fullPath = Path.Combine(artifactRoot, fileName);

        workbook.SaveAs(fullPath);

        var fileInfo = new FileInfo(fullPath);
        return ServiceResult<ExportArtifactResult>.Ok(new ExportArtifactResult(fullPath, fileInfo.Length));
    }

    private async Task<string?> StoreSupportAttachmentsAsync(
        Guid supportRequestId,
        IReadOnlyList<SupportScreenshotUploadRequest>? screenshots,
        CancellationToken cancellationToken)
    {
        if (screenshots is null || screenshots.Count == 0)
        {
            return null;
        }

        var attachmentsRoot = Path.Combine(AppContext.BaseDirectory, "support-attachments");
        Directory.CreateDirectory(attachmentsRoot);

        var stored = new List<object>(screenshots.Count);
        for (var index = 0; index < screenshots.Count; index++)
        {
            var screenshot = screenshots[index];
            var extension = screenshot.ContentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".bin"
            };

            var safeName = new string((screenshot.FileName ?? $"screenshot-{index + 1}")
                .Where(char.IsLetterOrDigit)
                .Take(60)
                .ToArray());
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = $"screenshot-{index + 1}";
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(screenshot.Base64Data);
            }
            catch
            {
                throw new InvalidOperationException("One of the screenshot attachments was not valid base64.");
            }

            var fileName = $"{supportRequestId:N}-{index + 1}-{safeName}{extension}";
            var fullPath = Path.Combine(attachmentsRoot, fileName);
            await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

            stored.Add(new
            {
                fileName,
                contentType = screenshot.ContentType,
                byteSize = bytes.Length
            });
        }

        return JsonSerializer.Serialize(stored);
    }

    private async Task<ServiceResult> ValidateDeletionVerificationCodeAsync(
        Guid userId,
        Guid challengeId,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        var result = await identityChallengeService.VerifyCodeForCompletionAsync(
            challengeId,
            userId,
            IdentityChallengePurposes.AccountDeletion,
            verificationCode,
            cancellationToken);
        return result.Succeeded
            ? ServiceResult.Ok()
            : ServiceResult.Fail(
                result.Error!.Message,
                result.Error.Code,
                result.Error.StatusCode);
    }

    private async Task<string> BuildDiagnosticsAsync(
        Guid? userId,
        Guid? sessionId,
        Guid? connectionId,
        Guid? linkedBankAccountId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!userId.HasValue)
        {
            var anonymousDiagnostics = new
            {
                generatedUtc = now,
                correlationId = requestContext.CorrelationId,
                sourceChannel = requestContext.SourceChannel,
                ipAddress = requestContext.IpAddress,
                userAgent = requestContext.UserAgent,
                platform = requestContext.Platform,
                appVersion = requestContext.AppVersion
            };
            return JsonSerializer.Serialize(anonymousDiagnostics);
        }

        var connectionsQuery = dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId.Value);

        if (connectionId.HasValue)
        {
            connectionsQuery = connectionsQuery.Where(x => x.Id == connectionId.Value);
        }

        var connections = await connectionsQuery
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new
            {
                x.Id,
                provider = x.ProviderName,
                x.ProviderDisplayName,
                x.Status,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode
            })
            .ToListAsync(cancellationToken);

        object? currentSession = null;
        if (sessionId.HasValue)
        {
            currentSession = await dbContext.Sessions
                .AsNoTracking()
                .Where(x => x.Id == sessionId.Value && x.UserId == userId.Value)
                .Select(x => new
                {
                    x.Id,
                    x.DeviceLabel,
                    x.Platform,
                    x.OsVersion,
                    x.AppVersion,
                    x.LastSeenUtc,
                    x.ExpiresUtc
                })
                .SingleOrDefaultAsync(cancellationToken);
        }

        var linkedAccountsQuery = dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Include(x => x.Connection)
            .Where(x => x.Connection != null && x.Connection.UserId == userId.Value);

        if (linkedBankAccountId.HasValue)
        {
            linkedAccountsQuery = linkedAccountsQuery.Where(x => x.Id == linkedBankAccountId.Value);
        }

        var linkedAccounts = await linkedAccountsQuery
            .OrderBy(x => x.DisplayName)
            .Select(x => new
            {
                x.Id,
                x.ConnectionId,
                x.DisplayName,
                x.ProviderAccountId,
                x.Currency,
                x.CurrentConnectionHealth
            })
            .ToListAsync(cancellationToken);

        var diagnostics = new
        {
            generatedUtc = now,
            userId = userId.Value,
            sessionId,
            correlationId = requestContext.CorrelationId,
            sourceChannel = requestContext.SourceChannel,
            ipAddress = requestContext.IpAddress,
            userAgent = requestContext.UserAgent,
            platform = requestContext.Platform,
            appVersion = requestContext.AppVersion,
            currentSession,
            connectionContext = new
            {
                requestedConnectionId = connectionId,
                requestedLinkedAccountId = linkedBankAccountId
            },
            connections,
            linkedAccounts
        };

        return JsonSerializer.Serialize(diagnostics);
    }

    private async Task RevokeAllSessionsForDeletionAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.Sessions
            .Where(x => x.UserId == userId && x.RevokedUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedUtc = now;
            session.RevocationReason = "deletion_requested";
        }

        var refreshTokens = await dbContext.SessionRefreshTokens
            .Where(x => x.Session != null && x.Session.UserId == userId && x.RevokedUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in refreshTokens)
        {
            token.RevokedUtc = now;
            token.RevocationReason = "deletion_requested";
        }
    }

    private async Task DisconnectBankingAndRemoveActiveFinancialDataAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var connections = await dbContext.OpenBankingConnections
            .Include(x => x.Token)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var connection in connections)
        {
            connection.Status = "revoked";
            connection.UpdatedUtc = now;
            if (connection.Token is not null)
            {
                connection.Token.EncryptedRefreshToken = null;
                connection.Token.AccessTokenExpiresUtc = null;
                connection.Token.IsRevoked = true;
                connection.Token.RevokedUtc = now;
            }
        }

        var linkedAccounts = await dbContext.LinkedBankAccounts
            .Include(x => x.Connection)
            .Where(x => x.Connection != null && x.Connection.UserId == userId)
            .ToListAsync(cancellationToken);

        var linkedAccountIds = linkedAccounts.Select(x => x.Id).ToList();
        var projectedFinancialAccountIds = linkedAccounts
            .Where(x => x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .Distinct()
            .ToList();

        if (linkedAccountIds.Count > 0)
        {
            var balanceSnapshots = await dbContext.BankBalanceSnapshots
                .Where(x => linkedAccountIds.Contains(x.LinkedBankAccountId))
                .ToListAsync(cancellationToken);

            var rawTransactions = await dbContext.RawBankTransactions
                .Where(x => linkedAccountIds.Contains(x.LinkedBankAccountId))
                .ToListAsync(cancellationToken);

            dbContext.BankBalanceSnapshots.RemoveRange(balanceSnapshots);
            dbContext.RawBankTransactions.RemoveRange(rawTransactions);
            dbContext.LinkedBankAccounts.RemoveRange(linkedAccounts);
        }

        if (projectedFinancialAccountIds.Count > 0)
        {
            var transactions = await dbContext.Transactions
                .Where(x => projectedFinancialAccountIds.Contains(x.FinancialAccountId))
                .ToListAsync(cancellationToken);
            var accounts = await dbContext.FinancialAccounts
                .Where(x => projectedFinancialAccountIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

            dbContext.Transactions.RemoveRange(transactions);
            dbContext.FinancialAccounts.RemoveRange(accounts);
        }

        logger.LogInformation(
            "Deletion cleanup removed linkedAccounts={LinkedAccounts} projectedAccounts={ProjectedAccounts} for userId={UserId}",
            linkedAccountIds.Count,
            projectedFinancialAccountIds.Count,
            userId);
    }

    private async Task RemoveSupportAndPreferenceArtifactsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var supportRequests = await dbContext.SupportRequests
            .Where(x => x.UserId == userId && x.Status == "open")
            .ToListAsync(cancellationToken);

        foreach (var request in supportRequests)
        {
            request.Status = "closed_user_deleted";
            request.UpdatedUtc = DateTime.UtcNow;
        }

        var preferences = await dbContext.UserPreferences
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (preferences is not null)
        {
            preferences.NotificationPreferencesJson = "{}";
            preferences.PrivacyPreferencesJson = "{}";
            preferences.EssentialCategoryPreferencesJson = "{}";
            preferences.FutureGoalConfigurationJson = "{}";
            preferences.UpdatedUtc = DateTime.UtcNow;
        }
    }
}
