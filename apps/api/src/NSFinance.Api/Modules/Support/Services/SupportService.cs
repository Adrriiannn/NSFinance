using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text;
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
    TokenSecretService tokenSecretService,
    ILogger<SupportService> logger)
{
    private const string PurposeAccountDeletion = "account_deletion";
    private static readonly TimeSpan ExportRetentionWindow = TimeSpan.FromMinutes(15);

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
            request.VerificationCode,
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
        ServiceResult<string> artifactResult;
        try
        {
            artifactResult = await BuildExportPackageAsync(userId, cancellationToken);
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
                Notes = NormalizeNullable(request.Notes),
                ArtifactReference = artifactResult.Value
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
            exportRequest.Notes = NormalizeNullable(request.Notes);
            exportRequest.ArtifactReference = artifactResult.Value;

            if (!string.IsNullOrWhiteSpace(previousArtifact)
                && !string.Equals(previousArtifact, artifactResult.Value, StringComparison.OrdinalIgnoreCase))
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

        return ServiceResult<ExportRequestDto>.Ok(new ExportRequestDto(
            exportRequest.Id,
            exportRequest.UserId,
            exportRequest.Status,
            exportRequest.RequestedUtc,
            exportRequest.UpdatedUtc,
            exportRequest.Notes));
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
            .Select(x => new ExportRequestDto(
                x.Id,
                x.UserId,
                x.Status,
                x.RequestedUtc,
                x.UpdatedUtc,
                x.Notes))
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
        return ServiceResult<ExportDownloadPayload>.Ok(new ExportDownloadPayload(fileName, "application/json", fileBytes));
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    private async Task<ServiceResult<string>> BuildExportPackageAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return ServiceResult<string>.Fail(
                "User not found.",
                "user_not_found",
                StatusCodes.Status404NotFound);
        }

        var preferences = await dbContext.UserPreferences
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.UserId,
                x.AdviceTonePreference,
                x.DigestFrequency,
                x.ReminderPreference,
                x.NotificationPreferencesJson,
                x.PrivacyPreferencesJson,
                x.EssentialCategoryPreferencesJson,
                x.FutureGoalConfigurationJson,
                x.UpdatedUtc
            })
            .SingleOrDefaultAsync(cancellationToken);
        var connections = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new
            {
                x.Id,
                x.ProviderName,
                x.ProviderEnvironment,
                x.ProviderConnectionReference,
                x.ProviderDisplayName,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.LastSuccessfulSyncUtc,
                x.LastSyncAttemptedUtc,
                x.LastErrorCode,
                x.LastErrorReason
            })
            .ToListAsync(cancellationToken);
        var linkedAccounts = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x => x.Connection != null && x.Connection.UserId == userId)
            .Select(x => new
            {
                x.Id,
                x.ConnectionId,
                x.ProviderAccountId,
                x.AccountType,
                x.AccountSubType,
                x.DisplayName,
                x.Currency,
                x.AccountNumberMetadataJson,
                x.CurrentConnectionHealth,
                x.RawPayloadJson,
                x.FinancialAccountId,
                x.CreatedUtc,
                x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);
        var financialAccounts = await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.Name,
                x.Type,
                x.Currency,
                x.CreatedUtc
            })
            .ToListAsync(cancellationToken);
        var financialAccountIds = financialAccounts.Select(x => x.Id).ToList();
        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(x => financialAccountIds.Contains(x.FinancialAccountId))
            .OrderByDescending(x => x.BookedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.FinancialAccountId,
                x.Amount,
                x.Currency,
                x.Description,
                x.BookedAtUtc,
                x.CategoryId,
                x.CreatedUtc
            })
            .ToListAsync(cancellationToken);
        var linkedAccountIds = linkedAccounts.Select(x => x.Id).ToList();
        var balanceSnapshots = await dbContext.BankBalanceSnapshots
            .AsNoTracking()
            .Where(x => linkedAccountIds.Contains(x.LinkedBankAccountId))
            .OrderByDescending(x => x.CapturedUtc)
            .Select(x => new
            {
                x.Id,
                x.LinkedBankAccountId,
                x.Available,
                x.Current,
                x.Overdraft,
                x.Currency,
                x.CapturedUtc,
                x.RawPayloadJson
            })
            .ToListAsync(cancellationToken);
        var supportRequests = await dbContext.SupportRequests
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new
            {
                x.Id,
                x.Category,
                x.Subcategory,
                x.Title,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);
        var policyAcceptances = await dbContext.PolicyAcceptances
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.AcceptedUtc)
            .Select(x => new
            {
                x.PolicyType,
                x.PolicyVersion,
                x.AcceptedUtc,
                x.AcceptanceContext,
                x.Platform,
                x.AppVersion
            })
            .ToListAsync(cancellationToken);

        var payload = new
        {
            generatedUtc = DateTime.UtcNow,
            profile = new
            {
                user.Id,
                user.PrimaryEmail,
                user.FullName,
                user.DisplayName,
                user.ProfileSubtitle,
                user.PhoneNumber,
                user.DateOfBirth,
                user.CountryRegion,
                user.Timezone,
                user.PreferredCurrency,
                user.CreatedUtc,
                user.LastLoginUtc
            },
            preferences,
            openBanking = new
            {
                connections,
                linkedAccounts,
                balanceSnapshots
            },
            accounts = financialAccounts,
            transactions,
            supportRequests,
            policyAcceptances
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var artifactRoot = Path.Combine(Path.GetTempPath(), "nsfinance-export-artifacts");
        Directory.CreateDirectory(artifactRoot);
        var fileName = $"nsfinance-export-{userId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        var fullPath = Path.Combine(artifactRoot, fileName);
        await File.WriteAllTextAsync(fullPath, json, Encoding.UTF8, cancellationToken);

        return ServiceResult<string>.Ok(fullPath);
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
        string verificationCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(verificationCode))
        {
            return ServiceResult.Fail(
                "Verification code is required.",
                "verification_code_required",
                StatusCodes.Status400BadRequest);
        }

        var now = DateTime.UtcNow;
        var tokenHash = tokenSecretService.HashToken(verificationCode.Trim());
        var token = await dbContext.EmailActionTokens
            .SingleOrDefaultAsync(
                x => x.UserId == userId
                     && x.Purpose == PurposeAccountDeletion
                     && x.TokenHash == tokenHash,
                cancellationToken);

        if (token is null || token.ExpiresUtc <= now)
        {
            return ServiceResult.Fail(
                "Verification code is invalid or expired.",
                "deletion_verification_invalid",
                StatusCodes.Status400BadRequest);
        }

        if (token.UsedUtc is not null)
        {
            return ServiceResult.Fail(
                "Verification code has already been used.",
                "deletion_verification_reused",
                StatusCodes.Status400BadRequest);
        }

        token.UsedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult.Ok();
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
