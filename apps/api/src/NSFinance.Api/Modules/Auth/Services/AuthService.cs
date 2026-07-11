using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Users;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    SessionService sessionService,
    GoogleAuthService googleAuthService,
    TokenSecretService tokenSecretService,
    ICurrentUserProvider currentUserProvider,
    AuthAbuseService authAbuseService,
    IAuditService auditService,
    IRequestContextAccessor requestContext,
    IOptions<JwtOptions> options,
    ILogger<AuthService> logger)
{
    private readonly JwtOptions _options = options.Value;

    private const string ProviderTypeLocalPassword = "local_password";
    private const string ProviderTypeGoogleOidc = "google_oidc";
    private const string PurposePasswordReset = "password_reset";
    private const string PurposePasswordChange = "password_change";
    private const string PurposeEmailVerification = "email_verification";
    private const string PurposeAccountDeletion = "account_deletion";

    public async Task<ServiceResult<AuthTokenResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var emailExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return ServiceResult<AuthTokenResponse>.Fail("Unable to register account.", "register_failed", StatusCodes.Status409Conflict);
        }

        var utcNow = DateTime.UtcNow;
        var fullName = NormalizeFullName(request.DisplayName);
        var nsTag = await GenerateUniqueNsTagAsync(fullName, normalizedEmail, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PrimaryEmail = normalizedEmail,
            NormalizedEmail = normalizedEmail,
            DisplayName = nsTag,
            FullName = fullName,
            Handle = nsTag,
            ProfileSubtitle = null,
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow,
            LastLoginUtc = utcNow,
            EmailVerified = false,
            IsDisabled = false,
            IsSuspended = false,
            DeletionRequested = false,
            Timezone = NormalizeOrDefault(request.Timezone, "UTC"),
            Locale = NormalizeOrDefault(request.Locale, "en-US"),
            PreferredCurrency = NormalizeCurrency(request.PreferredCurrency),
            PlanTier = "standard",
            BiometricUnlockEnabled = false,
            TwoFactorEnabled = false
        };

        dbContext.Users.Add(user);
        dbContext.UserAuthProviders.Add(new UserAuthProvider
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ProviderType = ProviderTypeLocalPassword,
            ProviderSubject = null,
            LinkedAtUtc = utcNow,
            LastUsedAtUtc = utcNow,
            IsActive = true
        });
        dbContext.PasswordCredentials.Add(new PasswordCredential
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            HashAlgorithm = "pbkdf2-sha256",
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow,
            RequiresRehash = false
        });
        dbContext.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            UpdatedUtc = utcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var tokenResponse = await sessionService.CreateSessionAsync(user, request.DeviceContext, cancellationToken);

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "user_registered",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new { provider = ProviderTypeLocalPassword, nsTag },
            cancellationToken);

        await IssueEmailActionTokenAsync(
            user.Id,
            PurposeEmailVerification,
            _options.EmailVerificationTokenMinutes,
            cancellationToken);

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "email_verification_requested",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new { flow = "post_register" },
            cancellationToken);

        return ServiceResult<AuthTokenResponse>.Ok(tokenResponse);
    }

    public async Task<ServiceResult<AuthTokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var (lockedOut, retryAfterUtc) = await authAbuseService.IsLockedOutAsync(normalizedEmail, cancellationToken);
        if (lockedOut)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                $"Too many attempts. Try again after {retryAfterUtc:O}.",
                "auth_locked",
                StatusCodes.Status429TooManyRequests);
        }

        var user = await dbContext.Users
            .Include(x => x.PasswordCredential)
            .Include(x => x.AuthProviders)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            await authAbuseService.RecordAttemptAsync(normalizedEmail, null, requestContext.IpAddress, succeeded: false, "account_not_found", cancellationToken);
            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "login_failure",
                targetEntityType: "user",
                targetEntityId: null,
                actorId: null,
                actorType: "anonymous",
                metadata: new { reason = "account_not_found" },
                cancellationToken);

            return ServiceResult<AuthTokenResponse>.Fail("No account was found for this email.", "account_not_found", StatusCodes.Status404NotFound);
        }

        var localProvider = user.AuthProviders.FirstOrDefault(x => x.ProviderType == ProviderTypeLocalPassword && x.IsActive);
        var passwordCredential = user.PasswordCredential;

        if (localProvider is null || passwordCredential is null)
        {
            await authAbuseService.RecordAttemptAsync(normalizedEmail, user.Id, requestContext.IpAddress, succeeded: false, "password_login_unavailable", cancellationToken);
            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "login_failure",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new { reason = "password_login_unavailable" },
                cancellationToken);

            return ServiceResult<AuthTokenResponse>.Fail(
                "This account uses Google sign-in. Continue with Google to log in.",
                "password_login_unavailable",
                StatusCodes.Status400BadRequest);
        }

        if (!passwordHasher.VerifyPassword(request.Password, passwordCredential.PasswordHash))
        {
            await authAbuseService.RecordAttemptAsync(normalizedEmail, user.Id, requestContext.IpAddress, succeeded: false, "invalid_password", cancellationToken);
            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "login_failure",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new { reason = "invalid_password" },
                cancellationToken);

            return ServiceResult<AuthTokenResponse>.Fail("The password is incorrect.", "invalid_password", StatusCodes.Status401Unauthorized);
        }

        if (user.IsDisabled || user.IsSuspended)
        {
            await authAbuseService.RecordAttemptAsync(normalizedEmail, user.Id, requestContext.IpAddress, succeeded: false, "account_restricted", cancellationToken);
            return ServiceResult<AuthTokenResponse>.Fail("Account access is restricted.", "account_restricted", StatusCodes.Status403Forbidden);
        }

        if (passwordCredential.RequiresRehash || passwordHasher.NeedsRehash(passwordCredential.PasswordHash))
        {
            passwordCredential.PasswordHash = passwordHasher.HashPassword(request.Password);
            passwordCredential.RequiresRehash = false;
            passwordCredential.UpdatedUtc = DateTime.UtcNow;
        }

        user.LastLoginUtc = DateTime.UtcNow;
        user.UpdatedUtc = DateTime.UtcNow;
        localProvider.LastUsedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await authAbuseService.RecordAttemptAsync(normalizedEmail, user.Id, requestContext.IpAddress, succeeded: true, failureReason: null, cancellationToken);
        var tokenResponse = await sessionService.CreateSessionAsync(user, request.DeviceContext, cancellationToken);

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "login_success",
            targetEntityType: "session",
            targetEntityId: tokenResponse.SessionId.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new { provider = ProviderTypeLocalPassword },
            cancellationToken);

        return ServiceResult<AuthTokenResponse>.Ok(tokenResponse);
    }

    public async Task<ServiceResult<AuthTokenResponse>> LoginWithGoogleAsync(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var totalStartedTimestamp = Stopwatch.GetTimestamp();
        var verification = await googleAuthService.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        var verificationDurationMs = Stopwatch.GetElapsedTime(totalStartedTimestamp).TotalMilliseconds;
        if (!verification.Succeeded)
        {
            await WriteGoogleLoginFailureAuditAsync(null, verification.Error!.Code, cancellationToken);
            return ServiceResult<AuthTokenResponse>.Fail(
                verification.Error.Message,
                verification.Error.Code,
                verification.Error.StatusCode);
        }

        var identity = verification.Value!;
        if (!identity.EmailVerified)
        {
            await WriteGoogleLoginFailureAuditAsync(null, "google_email_not_verified", cancellationToken);
            return ServiceResult<AuthTokenResponse>.Fail(
                "Google account email must be verified before sign-in is allowed.",
                "google_email_not_verified",
                StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(identity.Email))
        {
            await WriteGoogleLoginFailureAuditAsync(null, "google_email_missing", cancellationToken);
            return ServiceResult<AuthTokenResponse>.Fail(
                "Google account email is required.",
                "google_email_missing",
                StatusCodes.Status400BadRequest);
        }

        var normalizedEmail = NormalizeEmail(identity.Email);
        var utcNow = DateTime.UtcNow;
        var accountResolutionStartedTimestamp = Stopwatch.GetTimestamp();
        var connectionOpenDurationMs = 0d;
        var holdsConnectionOpen = dbContext.Database.IsRelational();
        if (holdsConnectionOpen)
        {
            var connectionOpenStartedTimestamp = Stopwatch.GetTimestamp();
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            connectionOpenDurationMs = Stopwatch.GetElapsedTime(connectionOpenStartedTimestamp).TotalMilliseconds;
        }

        var accountLookupStartedTimestamp = Stopwatch.GetTimestamp();
        var accountMatches = await dbContext.UserAuthProviders
            .Include(x => x.User)
            .ThenInclude(x => x!.AuthProviders)
            .Where(x =>
                (x.ProviderType == ProviderTypeGoogleOidc && x.ProviderSubject == identity.Subject)
                || (x.User != null && x.User.NormalizedEmail == normalizedEmail))
            .ToListAsync(cancellationToken);
        var accountLookupDurationMs = Stopwatch.GetElapsedTime(accountLookupStartedTimestamp).TotalMilliseconds;
        var nsTagGenerationDurationMs = 0d;

        var existingProviderLink = accountMatches.SingleOrDefault(
            x => x.ProviderType == ProviderTypeGoogleOidc
                 && x.ProviderSubject == identity.Subject);

        User user;
        UserAuthProvider providerLink;
        var providerLinkCreated = false;
        var createdViaGoogle = false;
        var linkedToExistingByEmail = false;

        if (existingProviderLink is not null)
        {
            if (existingProviderLink.User is null)
            {
                await WriteGoogleLoginFailureAuditAsync(null, "google_provider_user_not_found", cancellationToken);
                return ServiceResult<AuthTokenResponse>.Fail(
                    "Google account link is invalid. Please contact support.",
                    "google_provider_user_not_found",
                    StatusCodes.Status409Conflict);
            }

            user = existingProviderLink.User;
            providerLink = existingProviderLink;
        }
        else
        {
            user = accountMatches
                .Where(x => x.User?.NormalizedEmail == normalizedEmail)
                .Select(x => x.User!)
                .DistinctBy(x => x.Id)
                .SingleOrDefault()
                ?? new User();

            if (user.Id != Guid.Empty)
            {
                var existingUserGoogleProvider = user.AuthProviders
                    .FirstOrDefault(x => x.ProviderType == ProviderTypeGoogleOidc);

                if (existingUserGoogleProvider is not null
                    && !string.IsNullOrWhiteSpace(existingUserGoogleProvider.ProviderSubject)
                    && !string.Equals(existingUserGoogleProvider.ProviderSubject, identity.Subject, StringComparison.Ordinal))
                {
                    await WriteGoogleLoginFailureAuditAsync(user.Id, "google_provider_conflict", cancellationToken);
                    return ServiceResult<AuthTokenResponse>.Fail(
                        "Google provider identity conflict detected. Please contact support.",
                        "google_provider_conflict",
                        StatusCodes.Status409Conflict);
                }

                if (existingUserGoogleProvider is not null)
                {
                    providerLink = existingUserGoogleProvider;
                }
                else
                {
                    providerLink = new UserAuthProvider
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        ProviderType = ProviderTypeGoogleOidc,
                        ProviderSubject = identity.Subject,
                        LinkedAtUtc = utcNow,
                        LastUsedAtUtc = utcNow,
                        IsActive = true
                    };
                    dbContext.UserAuthProviders.Add(providerLink);
                    providerLinkCreated = true;
                    linkedToExistingByEmail = true;
                }
            }
            else
            {
                var fullName = NormalizeFullName(identity.Name ?? $"{identity.GivenName} {identity.FamilyName}".Trim());
                var nsTagGenerationStartedTimestamp = Stopwatch.GetTimestamp();
                var nsTag = await GenerateUniqueNsTagAsync(fullName, normalizedEmail, cancellationToken);
                nsTagGenerationDurationMs = Stopwatch.GetElapsedTime(nsTagGenerationStartedTimestamp).TotalMilliseconds;

                user = new User
                {
                    Id = Guid.NewGuid(),
                    PrimaryEmail = normalizedEmail,
                    NormalizedEmail = normalizedEmail,
                    DisplayName = nsTag,
                    FullName = fullName,
                    Handle = nsTag,
                    ProfileImageUrl = string.IsNullOrWhiteSpace(identity.PictureUrl) ? null : identity.PictureUrl,
                    ProfileSubtitle = null,
                    Status = "active",
                    OnboardingStatus = "profile_created",
                    Role = "user",
                    CreatedUtc = utcNow,
                    UpdatedUtc = utcNow,
                    LastLoginUtc = utcNow,
                    EmailVerified = true,
                    IsDisabled = false,
                    IsSuspended = false,
                    DeletionRequested = false,
                    Timezone = "UTC",
                    Locale = "en-US",
                    PreferredCurrency = "EUR",
                    PlanTier = "standard",
                    BiometricUnlockEnabled = false,
                    TwoFactorEnabled = false
                };

                providerLink = new UserAuthProvider
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    ProviderType = ProviderTypeGoogleOidc,
                    ProviderSubject = identity.Subject,
                    LinkedAtUtc = utcNow,
                    LastUsedAtUtc = utcNow,
                    IsActive = true
                };

                dbContext.Users.Add(user);
                dbContext.UserAuthProviders.Add(providerLink);
                dbContext.UserPreferences.Add(new UserPreference
                {
                    UserId = user.Id,
                    UpdatedUtc = utcNow
                });

                providerLinkCreated = true;
                createdViaGoogle = true;
            }
        }

        if (user.IsDisabled || user.IsSuspended)
        {
            await WriteGoogleLoginFailureAuditAsync(user.Id, "account_restricted", cancellationToken);
            return ServiceResult<AuthTokenResponse>.Fail(
                "Account access is restricted.",
                "account_restricted",
                StatusCodes.Status403Forbidden);
        }

        providerLink.ProviderSubject = identity.Subject;
        providerLink.IsActive = true;
        providerLink.LastUsedAtUtc = utcNow;

        if (providerLink.LinkedAtUtc == default)
        {
            providerLink.LinkedAtUtc = utcNow;
        }

        if (string.IsNullOrWhiteSpace(user.FullName) || string.Equals(user.FullName, "NSFinance User", StringComparison.OrdinalIgnoreCase))
        {
            user.FullName = NormalizeFullName(identity.Name ?? $"{identity.GivenName} {identity.FamilyName}".Trim());
        }

        if (string.IsNullOrWhiteSpace(user.ProfileImageUrl) && !string.IsNullOrWhiteSpace(identity.PictureUrl))
        {
            user.ProfileImageUrl = identity.PictureUrl;
        }

        user.EmailVerified = true;
        user.LastLoginUtc = utcNow;
        user.UpdatedUtc = utcNow;

        var accountResolutionDurationMs = Stopwatch.GetElapsedTime(accountResolutionStartedTimestamp).TotalMilliseconds;
        var sessionPersistenceStartedTimestamp = Stopwatch.GetTimestamp();

        if (providerLinkCreated)
        {
            StageAuditEvent(
                category: "auth",
                eventName: "google_provider_link_created",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new
                {
                    provider = ProviderTypeGoogleOidc,
                    providerSubject = identity.Subject,
                    linkedByEmail = linkedToExistingByEmail
                });
        }

        if (createdViaGoogle)
        {
            StageAuditEvent(
                category: "auth",
                eventName: "google_user_created",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new { provider = ProviderTypeGoogleOidc });
        }

        var sessionCreationStartedTimestamp = Stopwatch.GetTimestamp();
        var tokenResponse = await sessionService.CreateSessionAsync(
            user,
            request.DeviceContext,
            cancellationToken,
            saveImmediately: false,
            userIsNew: createdViaGoogle);
        var sessionCreationDurationMs = Stopwatch.GetElapsedTime(sessionCreationStartedTimestamp).TotalMilliseconds;

        StageAuditEvent(
            category: "auth",
            eventName: "google_login_success",
            targetEntityType: "session",
            targetEntityId: tokenResponse.SessionId.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new
            {
                provider = ProviderTypeGoogleOidc,
                linkedByEmail = linkedToExistingByEmail,
                createdViaGoogle
            });

        var saveChangesStartedTimestamp = Stopwatch.GetTimestamp();
        await dbContext.SaveChangesAsync(cancellationToken);
        var saveChangesDurationMs = Stopwatch.GetElapsedTime(saveChangesStartedTimestamp).TotalMilliseconds;
        if (holdsConnectionOpen)
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        var sessionPersistenceDurationMs = Stopwatch.GetElapsedTime(sessionPersistenceStartedTimestamp).TotalMilliseconds;
        var totalDurationMs = Stopwatch.GetElapsedTime(totalStartedTimestamp).TotalMilliseconds;
        logger.LogInformation(
            "Google login completed createdViaGoogle={CreatedViaGoogle} linkedByEmail={LinkedByEmail} " +
            "verificationDurationMs={VerificationDurationMs} connectionOpenDurationMs={ConnectionOpenDurationMs} " +
            "accountLookupDurationMs={AccountLookupDurationMs} nsTagGenerationDurationMs={NsTagGenerationDurationMs} " +
            "accountResolutionDurationMs={AccountResolutionDurationMs} sessionCreationDurationMs={SessionCreationDurationMs} " +
            "saveChangesDurationMs={SaveChangesDurationMs} sessionPersistenceDurationMs={SessionPersistenceDurationMs} " +
            "totalDurationMs={TotalDurationMs}",
            createdViaGoogle,
            linkedToExistingByEmail,
            Math.Round(verificationDurationMs),
            Math.Round(connectionOpenDurationMs),
            Math.Round(accountLookupDurationMs),
            Math.Round(nsTagGenerationDurationMs),
            Math.Round(accountResolutionDurationMs),
            Math.Round(sessionCreationDurationMs),
            Math.Round(saveChangesDurationMs),
            Math.Round(sessionPersistenceDurationMs),
            Math.Round(totalDurationMs));

        return ServiceResult<AuthTokenResponse>.Ok(tokenResponse);
    }

    public Task<ServiceResult<AuthTokenResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        return sessionService.RefreshAsync(request.RefreshToken, request.DeviceContext, cancellationToken);
    }

    public async Task<ServiceResult<UserProfileDto>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<UserProfileDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return ServiceResult<UserProfileDto>.Fail("Current user was not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        return ServiceResult<UserProfileDto>.Ok(MapUserProfile(user));
    }

    public async Task<ServiceResult<IReadOnlyList<SessionDto>>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<IReadOnlyList<SessionDto>>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        currentUserProvider.TryGetSessionId(out var sessionId);
        var sessions = await sessionService.ListSessionsAsync(userId, sessionId == Guid.Empty ? null : sessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<SessionDto>>.Ok(sessions);
    }

    public async Task<ServiceResult> RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var affected = await sessionService.RevokeSessionAsync(userId, sessionId, "user_revoked_session", cancellationToken);
        if (affected == 0)
        {
            return ServiceResult.Fail("Session not found.", "session_not_found", StatusCodes.Status404NotFound);
        }

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "session_revoked",
            targetEntityType: "session",
            targetEntityId: sessionId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<int>> LogoutAllAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<int>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        currentUserProvider.TryGetSessionId(out var currentSessionId);
        var revokedCount = await sessionService.RevokeAllSessionsAsync(
            userId,
            "user_logout_all",
            currentSessionId == Guid.Empty ? null : currentSessionId,
            cancellationToken);

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "all_sessions_revoked",
            targetEntityType: "user",
            targetEntityId: userId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new { revokedCount },
            cancellationToken);

        return ServiceResult<int>.Ok(revokedCount);
    }

    public async Task<ServiceResult> LogoutCurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        if (!currentUserProvider.TryGetSessionId(out var sessionId))
        {
            return ServiceResult.Fail("Session claim missing.", "session_claim_missing", StatusCodes.Status400BadRequest);
        }

        await sessionService.RevokeSessionAsync(userId, sessionId, "user_logout", cancellationToken);
        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "logout",
            targetEntityType: "session",
            targetEntityId: sessionId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<AuthActionResponse>> RequestPasswordResetAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is not null && !user.IsDisabled && !user.IsSuspended)
        {
            await IssueEmailActionTokenAsync(
                user.Id,
                PurposePasswordReset,
                _options.PasswordResetTokenMinutes,
                cancellationToken);

            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "password_reset_requested",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: null,
                actorType: "anonymous",
                metadata: null,
                cancellationToken);
        }

        var response = new AuthActionResponse(
            "If your email is registered, a password reset link will be sent shortly.");

        return ServiceResult<AuthActionResponse>.Ok(response);
    }

    public async Task<ServiceResult<AuthActionResponse>> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var tokenHash = tokenSecretService.HashToken(request.Token);
        var token = await dbContext.EmailActionTokens
            .SingleOrDefaultAsync(
                x => x.TokenHash == tokenHash && x.Purpose == PurposePasswordReset,
                cancellationToken);

        if (token is null || token.ExpiresUtc <= now)
        {
            return ServiceResult<AuthActionResponse>.Fail("Reset token is invalid or expired.", "reset_token_invalid", StatusCodes.Status400BadRequest);
        }

        if (token.UsedUtc is not null)
        {
            return ServiceResult<AuthActionResponse>.Fail("Reset token has already been used.", "reset_token_reused", StatusCodes.Status400BadRequest);
        }

        var user = await dbContext.Users
            .Include(x => x.PasswordCredential)
            .SingleOrDefaultAsync(x => x.Id == token.UserId, cancellationToken);

        if (user is null || user.PasswordCredential is null)
        {
            return ServiceResult<AuthActionResponse>.Fail("Reset token is invalid.", "reset_token_invalid", StatusCodes.Status400BadRequest);
        }

        user.PasswordCredential.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        user.PasswordCredential.RequiresRehash = false;
        user.PasswordCredential.UpdatedUtc = now;
        user.UpdatedUtc = now;
        token.UsedUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await sessionService.RevokeAllSessionsForUserAsync(user.Id, "password_reset", cancellationToken);

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "password_reset_completed",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<AuthActionResponse>.Ok(new AuthActionResponse("Password has been updated."));
    }

    public async Task<ServiceResult<AuthActionResponse>> RequestEmailVerificationAsync(
        RequestEmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is not null && !user.EmailVerified)
        {
            await IssueEmailActionTokenAsync(
                user.Id,
                PurposeEmailVerification,
                _options.EmailVerificationTokenMinutes,
                cancellationToken);

            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "email_verification_requested",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: null,
                cancellationToken);
        }

        var response = new AuthActionResponse(
            "If your account requires verification, an email verification link will be sent.");

        return ServiceResult<AuthActionResponse>.Ok(response);
    }

    public async Task<ServiceResult<AuthActionResponse>> ConfirmEmailVerificationAsync(
        ConfirmEmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var tokenHash = tokenSecretService.HashToken(request.Token);
        var token = await dbContext.EmailActionTokens
            .SingleOrDefaultAsync(
                x => x.TokenHash == tokenHash && x.Purpose == PurposeEmailVerification,
                cancellationToken);

        if (token is null || token.ExpiresUtc <= now)
        {
            return ServiceResult<AuthActionResponse>.Fail("Verification token is invalid or expired.", "email_verification_invalid", StatusCodes.Status400BadRequest);
        }

        if (token.UsedUtc is not null)
        {
            return ServiceResult<AuthActionResponse>.Fail("Verification token has already been used.", "email_verification_reused", StatusCodes.Status400BadRequest);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == token.UserId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<AuthActionResponse>.Fail("Verification token is invalid.", "email_verification_invalid", StatusCodes.Status400BadRequest);
        }

        token.UsedUtc = now;
        user.EmailVerified = true;
        user.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "email_verified",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<AuthActionResponse>.Ok(new AuthActionResponse("Email has been verified."));
    }

    public async Task<ServiceResult<AuthActionResponse>> ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<AuthActionResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users
            .Include(x => x.PasswordCredential)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null || user.PasswordCredential is null)
        {
            return ServiceResult<AuthActionResponse>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        if (!passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordCredential.PasswordHash))
        {
            return ServiceResult<AuthActionResponse>.Fail("Current password is invalid.", "invalid_current_password", StatusCodes.Status400BadRequest);
        }

        user.PasswordCredential.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        user.PasswordCredential.RequiresRehash = false;
        user.PasswordCredential.UpdatedUtc = DateTime.UtcNow;
        user.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await sessionService.RevokeAllSessionsForUserAsync(user.Id, "password_changed", cancellationToken);

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "password_changed",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<AuthActionResponse>.Ok(new AuthActionResponse("Password updated. Please sign in again on your devices."));
    }

    public async Task<ServiceResult<AuthActionResponse>> RequestPasswordChangeCodeAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<AuthActionResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<AuthActionResponse>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        await IssueEmailActionTokenAsync(
            userId,
            PurposePasswordChange,
            _options.PasswordResetTokenMinutes,
            cancellationToken);

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "password_change_code_requested",
            targetEntityType: "user",
            targetEntityId: userId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<AuthActionResponse>.Ok(new AuthActionResponse(
            "A password change code was requested. Check your email to continue."));
    }

    public async Task<ServiceResult<AuthActionResponse>> VerifyPasswordChangeCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<AuthActionResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var validation = await ValidateEmailActionTokenForUserAsync(
            userId,
            code,
            PurposePasswordChange,
            consumeToken: false,
            cancellationToken);
        if (!validation.Succeeded)
        {
            return ServiceResult<AuthActionResponse>.Fail(
                validation.Error!.Message,
                validation.Error.Code,
                validation.Error.StatusCode);
        }

        return ServiceResult<AuthActionResponse>.Ok(new AuthActionResponse("Code verified."));
    }

    public async Task<ServiceResult<AuthActionResponse>> ConfirmPasswordChangeWithCodeAsync(
        string code,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<AuthActionResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var validation = await ValidateEmailActionTokenForUserAsync(
            userId,
            code,
            PurposePasswordChange,
            consumeToken: true,
            cancellationToken);
        if (!validation.Succeeded)
        {
            return ServiceResult<AuthActionResponse>.Fail(
                validation.Error!.Message,
                validation.Error.Code,
                validation.Error.StatusCode);
        }

        var user = await dbContext.Users
            .Include(x => x.PasswordCredential)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || user.PasswordCredential is null)
        {
            return ServiceResult<AuthActionResponse>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        user.PasswordCredential.PasswordHash = passwordHasher.HashPassword(newPassword);
        user.PasswordCredential.RequiresRehash = false;
        user.PasswordCredential.UpdatedUtc = DateTime.UtcNow;
        user.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await sessionService.RevokeAllSessionsForUserAsync(user.Id, "password_changed", cancellationToken);

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "password_changed_with_code",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<AuthActionResponse>.Ok(new AuthActionResponse("Password updated. Please sign in again on your devices."));
    }

    public async Task<ServiceResult<AuthActionResponse>> RequestAccountDeletionCodeAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<AuthActionResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        await IssueEmailActionTokenAsync(
            userId,
            PurposeAccountDeletion,
            _options.PasswordResetTokenMinutes,
            cancellationToken);

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "account_deletion_code_requested",
            targetEntityType: "user",
            targetEntityId: userId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<AuthActionResponse>.Ok(new AuthActionResponse(
            "A deletion verification code was requested. Check your email to continue."));
    }

    public GoogleAuthOptionsDto GetGoogleAuthOptions()
    {
        return new GoogleAuthOptionsDto(
            IsConfigured: googleAuthService.IsConfigured,
            ProviderType: googleAuthService.ProviderType,
            AuthorizationUrl: null,
            CallbackPath: "/api/auth/providers/google/callback",
            Message: googleAuthService.IsConfigured
                ? "Google sign-in is configured for backend token verification."
                : "Google sign-in is not configured. Set GoogleAuth:WebClientId and GoogleAuth:AndroidClientIdProd to enable it.");
    }

    public async Task<ServiceResult<AuthActionResponse>> ScaffoldGoogleCallbackAsync(CancellationToken cancellationToken)
    {
        if (currentUserProvider.TryGetUserId(out var userId))
        {
            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "auth_provider_link_attempted",
                targetEntityType: "user",
                targetEntityId: userId.ToString(),
                actorId: userId,
                actorType: "user",
                metadata: new { provider = ProviderTypeGoogleOidc, status = "not_implemented" },
                cancellationToken);
        }

        return ServiceResult<AuthActionResponse>.Fail(
            "Google sign-in callback is scaffolded but not active.",
            "google_sign_in_not_configured",
            StatusCodes.Status501NotImplemented);
    }

    private void StageAuditEvent(
        string category,
        string eventName,
        string targetEntityType,
        string? targetEntityId,
        Guid? actorId,
        string actorType,
        object? metadata)
    {
        dbContext.AuditEvents.Add(AuditEventFactory.Create(
            requestContext,
            category,
            eventName,
            targetEntityType,
            targetEntityId,
            actorId,
            actorType,
            metadata));
    }

    private Task WriteGoogleLoginFailureAuditAsync(
        Guid? actorId,
        string reason,
        CancellationToken cancellationToken)
    {
        return auditService.WriteEventAsync(
            category: "auth",
            eventName: "google_login_failed",
            targetEntityType: "user",
            targetEntityId: actorId?.ToString(),
            actorId: actorId,
            actorType: actorId.HasValue ? "user" : "anonymous",
            metadata: new { provider = ProviderTypeGoogleOidc, reason },
            cancellationToken);
    }

    private async Task<string> IssueEmailActionTokenAsync(
        Guid userId,
        string purpose,
        int expiryMinutes,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var activeTokens = await dbContext.EmailActionTokens
            .Where(x => x.UserId == userId && x.Purpose == purpose && x.UsedUtc == null && x.ExpiresUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.UsedUtc = now;
        }

        var rawToken = tokenSecretService.CreateToken();
        dbContext.EmailActionTokens.Add(new EmailActionToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenSecretService.HashToken(rawToken),
            CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(expiryMinutes),
            RequestedByIp = requestContext.IpAddress
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return rawToken;
    }

    private async Task<ServiceResult<EmailActionToken>> ValidateEmailActionTokenForUserAsync(
        Guid userId,
        string rawToken,
        string purpose,
        bool consumeToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return ServiceResult<EmailActionToken>.Fail(
                "Verification code is required.",
                "verification_code_required",
                StatusCodes.Status400BadRequest);
        }

        var tokenHash = tokenSecretService.HashToken(rawToken.Trim());
        var now = DateTime.UtcNow;
        var token = await dbContext.EmailActionTokens
            .SingleOrDefaultAsync(
                x => x.UserId == userId
                     && x.Purpose == purpose
                     && x.TokenHash == tokenHash,
                cancellationToken);

        if (token is null || token.ExpiresUtc <= now)
        {
            return ServiceResult<EmailActionToken>.Fail(
                "Verification code is invalid or expired.",
                "verification_code_invalid",
                StatusCodes.Status400BadRequest);
        }

        if (token.UsedUtc is not null)
        {
            return ServiceResult<EmailActionToken>.Fail(
                "Verification code has already been used.",
                "verification_code_reused",
                StatusCodes.Status400BadRequest);
        }

        if (consumeToken)
        {
            token.UsedUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<EmailActionToken>.Ok(token);
    }

    private static UserProfileDto MapUserProfile(User user)
    {
        return new UserProfileDto(
            user.Id,
            user.PrimaryEmail,
            user.FullName,
            user.DisplayName,
            user.Handle,
            user.ProfileImageUrl,
            user.ProfileSubtitle,
            user.Timezone,
            user.Locale,
            user.PreferredCurrency,
            user.Role,
            user.EmailVerified,
            user.OnboardingStatus,
            user.BiometricUnlockEnabled,
            user.TwoFactorEnabled,
            user.PlanTier,
            user.CreatedUtc,
            user.LastLoginUtc);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private async Task<string> GenerateUniqueNsTagAsync(
        string fullName,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var tagBase = BuildTagBase(fullName, normalizedEmail);
        if (string.IsNullOrWhiteSpace(tagBase))
        {
            tagBase = "member";
        }

        var primaryBaseLength = Math.Max(1, NsTagPolicy.MaxLength - 2);
        var primaryBase = tagBase[..Math.Min(tagBase.Length, primaryBaseLength)];

        for (var attempt = 0; attempt < 120; attempt++)
        {
            var suffix = RandomNumberGenerator.GetInt32(0, 100).ToString("D2");
            var candidate = NsTagPolicy.Normalize($"{primaryBase}{suffix}");
            if (await IsNsTagAvailableAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        var fallbackBaseLength = Math.Max(1, NsTagPolicy.MaxLength - 4);
        var fallbackBase = tagBase[..Math.Min(tagBase.Length, fallbackBaseLength)];

        for (var attempt = 0; attempt < 500; attempt++)
        {
            var suffix = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
            var candidate = NsTagPolicy.Normalize($"{fallbackBase}{suffix}");
            if (await IsNsTagAvailableAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        var deterministicFallback = NsTagPolicy.Normalize($"member{RandomNumberGenerator.GetInt32(1000, 9999)}");
        if (await IsNsTagAvailableAsync(deterministicFallback, cancellationToken))
        {
            return deterministicFallback;
        }

        throw new InvalidOperationException("Unable to generate a unique NS Tag for the new account.");
    }

    private async Task<bool> IsNsTagAvailableAsync(string normalizedTag, CancellationToken cancellationToken)
    {
        var lowered = normalizedTag.ToLower();
        return !await dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.DisplayName.ToLower() == lowered, cancellationToken);
    }

    private static string BuildTagBase(string fullName, string normalizedEmail)
    {
        var nameParts = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTagToken)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (nameParts.Length >= 2)
        {
            return $"{nameParts[0]}{nameParts[^1]}";
        }

        if (nameParts.Length == 1)
        {
            return nameParts[0];
        }

        var emailLocalPart = normalizedEmail.Split('@', StringSplitOptions.TrimEntries)[0];
        return NormalizeTagToken(emailLocalPart);
    }

    private static string NormalizeTagToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(ch => (ch >= 'a' && ch <= 'z') || char.IsDigit(ch))
            .ToArray());
    }

    private static string NormalizeFullName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "NSFinance User";
        }

        return displayName.Trim();
    }

    private static string NormalizeOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }

    private static string NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return "EUR";
        }

        return currency.Trim().ToUpperInvariant();
    }
}
