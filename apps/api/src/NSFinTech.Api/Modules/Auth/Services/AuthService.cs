using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Infrastructure.RequestContext;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Auth.DTOs;
using NSFinTech.Api.Modules.Users.Services;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Auth.Services;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    SessionService sessionService,
    TokenSecretService tokenSecretService,
    ICurrentUserProvider currentUserProvider,
    AuthAbuseService authAbuseService,
    IAuditService auditService,
    IRequestContextAccessor requestContext,
    IHostEnvironment hostEnvironment,
    IOptions<JwtOptions> options,
    ILogger<AuthService> logger)
{
    private readonly JwtOptions _options = options.Value;

    private const string ProviderTypeLocalPassword = "local_password";
    private const string ProviderTypeGoogleOidc = "google_oidc";
    private const string PurposePasswordReset = "password_reset";
    private const string PurposeEmailVerification = "email_verification";

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
        var user = new User
        {
            Id = Guid.NewGuid(),
            PrimaryEmail = normalizedEmail,
            NormalizedEmail = normalizedEmail,
            DisplayName = NormalizeDisplayName(request.DisplayName),
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
            BiometricUnlockEnabled = false
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
            metadata: new { provider = ProviderTypeLocalPassword },
            cancellationToken);

        var emailToken = await IssueEmailActionTokenAsync(
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

        if (hostEnvironment.IsDevelopment())
        {
            logger.LogInformation("Email verification token issued for local development userId={UserId}", user.Id);
        }

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

        var localProvider = user?.AuthProviders.FirstOrDefault(x => x.ProviderType == ProviderTypeLocalPassword && x.IsActive);
        var passwordCredential = user?.PasswordCredential;

        if (user is null || localProvider is null || passwordCredential is null || !passwordHasher.VerifyPassword(request.Password, passwordCredential.PasswordHash))
        {
            await authAbuseService.RecordAttemptAsync(normalizedEmail, user?.Id, requestContext.IpAddress, succeeded: false, "invalid_credentials", cancellationToken);
            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "login_failure",
                targetEntityType: "user",
                targetEntityId: user?.Id.ToString(),
                actorId: user?.Id,
                actorType: user is null ? "anonymous" : "user",
                metadata: new { reason = "invalid_credentials" },
                cancellationToken);

            return ServiceResult<AuthTokenResponse>.Fail("Invalid email or password.", "invalid_credentials", StatusCodes.Status401Unauthorized);
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

        string? debugToken = null;
        if (user is not null && !user.IsDisabled && !user.IsSuspended)
        {
            debugToken = await IssueEmailActionTokenAsync(
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
            "If your email is registered, a password reset link will be sent shortly.",
            hostEnvironment.IsDevelopment() ? debugToken : null);

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

        string? debugToken = null;
        if (user is not null && !user.EmailVerified)
        {
            debugToken = await IssueEmailActionTokenAsync(
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
            "If your account requires verification, an email verification link will be sent.",
            hostEnvironment.IsDevelopment() ? debugToken : null);

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

    public GoogleAuthOptionsDto GetGoogleAuthOptions()
    {
        return new GoogleAuthOptionsDto(
            IsConfigured: false,
            ProviderType: ProviderTypeGoogleOidc,
            AuthorizationUrl: null,
            CallbackPath: "/api/auth/providers/google/callback",
            Message: "Google OIDC scaffolding is in place. Configure client ID/secret and token validation in a secure secret store to enable.");
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
            "Google sign-in callback is scaffolded but not active in this environment.",
            "google_sign_in_not_configured",
            StatusCodes.Status501NotImplemented);
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

    private static UserProfileDto MapUserProfile(User user)
    {
        return new UserProfileDto(
            user.Id,
            user.PrimaryEmail,
            user.DisplayName,
            user.Timezone,
            user.Locale,
            user.PreferredCurrency,
            user.Role,
            user.EmailVerified,
            user.OnboardingStatus,
            user.BiometricUnlockEnabled,
            user.PlanTier,
            user.CreatedUtc,
            user.LastLoginUtc);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "NSFinTech User";
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
