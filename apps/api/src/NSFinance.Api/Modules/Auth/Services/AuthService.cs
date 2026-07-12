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
    MicrosoftAuthService microsoftAuthService,
    IdentityChallengeService identityChallengeService,
    TransactionalMessageService transactionalMessageService,
    TotpMfaService totpMfaService,
    MfaTrustedDeviceService mfaTrustedDeviceService,
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
    private const string ProviderTypeMicrosoftOidc = "microsoft_oidc";

    private sealed record RegistrationPolicySet(PolicyVersion Terms, PolicyVersion Privacy);

    public async Task<ServiceResult<RegistrationResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var emailExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return ServiceResult<RegistrationResponse>.Fail("Unable to register account.", "register_failed", StatusCodes.Status409Conflict);
        }

        var policyResult = await ResolveRegistrationPoliciesAsync(
            request.AcceptPolicies,
            request.TermsVersion,
            request.PrivacyVersion,
            cancellationToken);
        if (!policyResult.Succeeded)
        {
            return ServiceResult<RegistrationResponse>.Fail(
                policyResult.Error!.Message,
                policyResult.Error.Code,
                policyResult.Error.StatusCode);
        }

        var termsPolicy = policyResult.Value!.Terms;
        var privacyPolicy = policyResult.Value.Privacy;

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
            OnboardingStatus = "pending_email_verification",
            Role = "user",
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow,
            LastLoginUtc = null,
            EmailVerified = false,
            IsDisabled = false,
            IsSuspended = false,
            DeletionRequested = false,
            Timezone = NormalizeOrDefault(request.Timezone, "UTC"),
            Locale = NormalizeOrDefault(request.Locale, "en-US"),
            PreferredCurrency = NormalizeCurrency(request.PreferredCurrency),
            PlanTier = "standard",
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
        dbContext.PolicyAcceptances.AddRange(
            CreateRegistrationPolicyAcceptance(user.Id, termsPolicy, "terms_of_service", request.DeviceContext, utcNow),
            CreateRegistrationPolicyAcceptance(user.Id, privacyPolicy, "privacy_policy", request.DeviceContext, utcNow));

        StageAuditEvent(
            category: "auth",
            eventName: "user_registered_pending_verification",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new
            {
                provider = ProviderTypeLocalPassword,
                nsTag,
                termsVersion = termsPolicy.Version,
                privacyVersion = privacyPolicy.Version
            });

        var challengeResult = await identityChallengeService.CreateEmailCodeAsync(
            user,
            user.PrimaryEmail,
            IdentityChallengePurposes.EmailVerification,
            IdentityEmailRenderer.EmailVerificationTemplate,
            cancellationToken);
        if (!challengeResult.Succeeded)
        {
            return ServiceResult<RegistrationResponse>.Fail(
                challengeResult.Error!.Message,
                challengeResult.Error.Code,
                challengeResult.Error.StatusCode);
        }

        var challenge = challengeResult.Value!;
        return ServiceResult<RegistrationResponse>.Ok(new RegistrationResponse(
            "email_verification_required",
            challenge.ChallengeId,
            challenge.ExpiresUtc,
            challenge.ResendAfterSeconds,
            "Enter the six-digit code sent to your email."));
    }

    public async Task<ServiceResult<AuthFlowResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var (lockedOut, retryAfterUtc) = await authAbuseService.IsLockedOutAsync(normalizedEmail, cancellationToken);
        if (lockedOut)
        {
            return ServiceResult<AuthFlowResponse>.Fail(
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
            passwordHasher.PerformDummyVerification(request.Password);
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

            return InvalidCredentialsResult();
        }

        var localProvider = user.AuthProviders.FirstOrDefault(x => x.ProviderType == ProviderTypeLocalPassword && x.IsActive);
        var passwordCredential = user.PasswordCredential;

        if (localProvider is null || passwordCredential is null)
        {
            passwordHasher.PerformDummyVerification(request.Password);
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

            return InvalidCredentialsResult();
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

            return InvalidCredentialsResult();
        }

        if (user.IsDisabled || user.IsSuspended)
        {
            await authAbuseService.RecordAttemptAsync(normalizedEmail, user.Id, requestContext.IpAddress, succeeded: false, "account_restricted", cancellationToken);
            return ServiceResult<AuthFlowResponse>.Fail(
                "Account access is restricted.",
                "account_restricted",
                StatusCodes.Status403Forbidden);
        }

        if (!user.EmailVerified)
        {
            return ServiceResult<AuthFlowResponse>.Fail(
                "Confirm your email before signing in.",
                "email_verification_required",
                StatusCodes.Status403Forbidden);
        }

        if (passwordCredential.RequiresRehash || passwordHasher.NeedsRehash(passwordCredential.PasswordHash))
        {
            passwordCredential.PasswordHash = passwordHasher.HashPassword(request.Password);
            passwordCredential.RequiresRehash = false;
            passwordCredential.UpdatedUtc = DateTime.UtcNow;
        }

        var now = DateTime.UtcNow;
        var requiresMfa = await totpMfaService.IsEnabledAsync(user.Id, cancellationToken);
        var trustedDeviceAccepted = requiresMfa && await mfaTrustedDeviceService.ValidateAsync(
            user.Id,
            request.MfaTrustedDeviceToken,
            request.DeviceContext,
            cancellationToken);
        if (!requiresMfa || trustedDeviceAccepted)
        {
            user.LastLoginUtc = now;
        }
        user.UpdatedUtc = now;
        localProvider.LastUsedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        await authAbuseService.RecordAttemptAsync(normalizedEmail, user.Id, requestContext.IpAddress, succeeded: true, failureReason: null, cancellationToken);
        if (requiresMfa && !trustedDeviceAccepted)
        {
            var mfaChallenge = await totpMfaService.CreateLoginChallengeAsync(user, cancellationToken);
            if (!mfaChallenge.Succeeded)
            {
                return ServiceResult<AuthFlowResponse>.Fail(
                    mfaChallenge.Error!.Message,
                    mfaChallenge.Error.Code,
                    mfaChallenge.Error.StatusCode);
            }

            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "login_first_factor_succeeded",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new { provider = ProviderTypeLocalPassword, mfaRequired = true },
                cancellationToken);

            return ServiceResult<AuthFlowResponse>.Ok(AuthFlowResponse.MfaRequired(mfaChallenge.Value!));
        }

        var tokenResponse = await sessionService.CreateSessionAsync(user, request.DeviceContext, cancellationToken);

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "login_success",
            targetEntityType: "session",
            targetEntityId: tokenResponse.SessionId.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new { provider = ProviderTypeLocalPassword, mfaTrustedDevice = trustedDeviceAccepted },
            cancellationToken);

        return ServiceResult<AuthFlowResponse>.Ok(AuthFlowResponse.Authenticated(tokenResponse));
    }

    public async Task<ServiceResult<AuthTokenResponse>> VerifyMfaLoginAsync(
        VerifyMfaLoginRequest request,
        CancellationToken cancellationToken)
    {
        var verification = await totpMfaService.VerifyLoginChallengeAsync(request, cancellationToken);
        if (!verification.Succeeded)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                verification.Error!.Message,
                verification.Error.Code,
                verification.Error.StatusCode);
        }

        var user = verification.Value!;
        if (user.IsDisabled || user.IsSuspended)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                "Account access is restricted.",
                "account_restricted",
                StatusCodes.Status403Forbidden);
        }

        var tokenResponse = await sessionService.CreateSessionAsync(user, request.DeviceContext, cancellationToken);
        if (request.RememberDevice && request.Method == "totp")
        {
            tokenResponse = tokenResponse with
            {
                MfaTrustedDevice = await mfaTrustedDeviceService.IssueAsync(
                    user.Id,
                    request.DeviceContext,
                    cancellationToken)
            };
        }
        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "mfa_login_success",
            targetEntityType: "session",
            targetEntityId: tokenResponse.SessionId.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new
            {
                method = request.Method,
                rememberedDevice = tokenResponse.MfaTrustedDevice is not null
            },
            cancellationToken);

        return ServiceResult<AuthTokenResponse>.Ok(tokenResponse);
    }

    public async Task<ServiceResult<MfaLoginChallengeResponse>> BeginRememberedSessionMfaAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var rememberedSession = await sessionService.ValidateRememberedSessionAsync(
            request.RefreshToken,
            cancellationToken);
        if (!rememberedSession.Succeeded)
        {
            return ServiceResult<MfaLoginChallengeResponse>.Fail(
                rememberedSession.Error!.Message,
                rememberedSession.Error.Code,
                rememberedSession.Error.StatusCode);
        }

        var user = rememberedSession.Value!;
        if (!await totpMfaService.IsEnabledAsync(user.Id, cancellationToken))
        {
            return ServiceResult<MfaLoginChallengeResponse>.Fail(
                "Authenticator is not enabled for this account.",
                "mfa_not_enabled",
                StatusCodes.Status409Conflict);
        }

        var challenge = await totpMfaService.CreateSessionResumeChallengeAsync(user, cancellationToken);
        if (!challenge.Succeeded)
        {
            return challenge;
        }

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "remembered_session_mfa_started",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return challenge;
    }

    public async Task<ServiceResult<AuthTokenResponse>> VerifyRememberedSessionMfaAsync(
        VerifyRememberedSessionMfaRequest request,
        CancellationToken cancellationToken)
    {
        var verification = await totpMfaService.VerifySessionResumeChallengeAsync(
            new VerifyMfaLoginRequest(
                request.ChallengeId,
                request.ChallengeToken,
                request.Code,
                request.Method,
                request.DeviceContext),
            cancellationToken);
        if (!verification.Succeeded)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                verification.Error!.Message,
                verification.Error.Code,
                verification.Error.StatusCode);
        }

        var user = verification.Value!;
        var refreshed = await sessionService.RefreshRememberedSessionAsync(
            request.RefreshToken,
            user.Id,
            request.DeviceContext,
            cancellationToken);
        if (!refreshed.Succeeded)
        {
            return refreshed;
        }

        if (request.RememberDevice && request.Method == "totp")
        {
            refreshed = ServiceResult<AuthTokenResponse>.Ok(refreshed.Value! with
            {
                MfaTrustedDevice = await mfaTrustedDeviceService.IssueAsync(
                    user.Id,
                    request.DeviceContext,
                    cancellationToken)
            });
        }

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "remembered_session_mfa_succeeded",
            targetEntityType: "session",
            targetEntityId: refreshed.Value!.SessionId.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new
            {
                method = request.Method,
                rememberedDevice = refreshed.Value!.MfaTrustedDevice is not null
            },
            cancellationToken);

        return refreshed;
    }

    public async Task<ServiceResult<AuthTokenResponse>> ResumeRememberedSessionWithTrustedDeviceAsync(
        ResumeRememberedSessionWithTrustedDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var rememberedSession = await sessionService.ValidateRememberedSessionAsync(
            request.RefreshToken,
            cancellationToken);
        if (!rememberedSession.Succeeded)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                rememberedSession.Error!.Message,
                rememberedSession.Error.Code,
                rememberedSession.Error.StatusCode);
        }

        var user = rememberedSession.Value!;
        var trusted = await mfaTrustedDeviceService.ValidateAsync(
            user.Id,
            request.TrustedDeviceToken,
            request.DeviceContext,
            cancellationToken);
        if (!trusted || !await totpMfaService.IsEnabledAsync(user.Id, cancellationToken))
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                "This device must complete an Authenticator check.",
                "mfa_trusted_device_invalid",
                StatusCodes.Status401Unauthorized);
        }

        var refreshed = await sessionService.RefreshRememberedSessionAsync(
            request.RefreshToken,
            user.Id,
            request.DeviceContext,
            cancellationToken);
        if (!refreshed.Succeeded)
        {
            return refreshed;
        }

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "remembered_session_trusted_device_succeeded",
            targetEntityType: "session",
            targetEntityId: refreshed.Value!.SessionId.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return refreshed;
    }

    public async Task<ServiceResult<AuthFlowResponse>> LoginWithGoogleAsync(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var totalStartedTimestamp = Stopwatch.GetTimestamp();
        var verification = await googleAuthService.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        var verificationDurationMs = Stopwatch.GetElapsedTime(totalStartedTimestamp).TotalMilliseconds;
        if (!verification.Succeeded)
        {
            await WriteGoogleLoginFailureAuditAsync(null, verification.Error!.Code, cancellationToken);
            return ServiceResult<AuthFlowResponse>.Fail(
                verification.Error.Message,
                verification.Error.Code,
                verification.Error.StatusCode);
        }

        var identity = verification.Value!;
        if (!identity.EmailVerified)
        {
            await WriteGoogleLoginFailureAuditAsync(null, "google_email_not_verified", cancellationToken);
            return ServiceResult<AuthFlowResponse>.Fail(
                "Google account email must be verified before sign-in is allowed.",
                "google_email_not_verified",
                StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(identity.Email))
        {
            await WriteGoogleLoginFailureAuditAsync(null, "google_email_missing", cancellationToken);
            return ServiceResult<AuthFlowResponse>.Fail(
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
                return ServiceResult<AuthFlowResponse>.Fail(
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
                    return ServiceResult<AuthFlowResponse>.Fail(
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
                var policyResult = await ResolveRegistrationPoliciesAsync(
                    request.AcceptPolicies,
                    request.TermsVersion,
                    request.PrivacyVersion,
                    cancellationToken);
                if (!policyResult.Succeeded)
                {
                    return ServiceResult<AuthFlowResponse>.Fail(
                        policyResult.Error!.Message,
                        policyResult.Error.Code,
                        policyResult.Error.StatusCode);
                }

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
                dbContext.PolicyAcceptances.AddRange(
                    CreateRegistrationPolicyAcceptance(user.Id, policyResult.Value!.Terms, "terms_of_service", request.DeviceContext, utcNow),
                    CreateRegistrationPolicyAcceptance(user.Id, policyResult.Value.Privacy, "privacy_policy", request.DeviceContext, utcNow));

                providerLinkCreated = true;
                createdViaGoogle = true;
            }
        }

        if (user.IsDisabled || user.IsSuspended)
        {
            await WriteGoogleLoginFailureAuditAsync(user.Id, "account_restricted", cancellationToken);
            return ServiceResult<AuthFlowResponse>.Fail(
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

        var requiresMfa = await totpMfaService.IsEnabledAsync(user.Id, cancellationToken);
        var trustedDeviceAccepted = requiresMfa && await mfaTrustedDeviceService.ValidateAsync(
            user.Id,
            request.MfaTrustedDeviceToken,
            request.DeviceContext,
            cancellationToken);
        user.EmailVerified = true;
        if (!requiresMfa || trustedDeviceAccepted)
        {
            user.LastLoginUtc = utcNow;
        }
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

        if (requiresMfa && !trustedDeviceAccepted)
        {
            StageAuditEvent(
                category: "auth",
                eventName: "login_first_factor_succeeded",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new { provider = ProviderTypeGoogleOidc, mfaRequired = true });

            await dbContext.SaveChangesAsync(cancellationToken);
            if (holdsConnectionOpen)
            {
                await dbContext.Database.CloseConnectionAsync();
            }

            var mfaChallenge = await totpMfaService.CreateLoginChallengeAsync(user, cancellationToken);
            if (!mfaChallenge.Succeeded)
            {
                return ServiceResult<AuthFlowResponse>.Fail(
                    mfaChallenge.Error!.Message,
                    mfaChallenge.Error.Code,
                    mfaChallenge.Error.StatusCode);
            }

            logger.LogInformation(
                "Google first factor completed and requires MFA createdViaGoogle={CreatedViaGoogle} linkedByEmail={LinkedByEmail} totalDurationMs={TotalDurationMs}",
                createdViaGoogle,
                linkedToExistingByEmail,
                Math.Round(Stopwatch.GetElapsedTime(totalStartedTimestamp).TotalMilliseconds));

            return ServiceResult<AuthFlowResponse>.Ok(AuthFlowResponse.MfaRequired(mfaChallenge.Value!));
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
                createdViaGoogle,
                mfaTrustedDevice = trustedDeviceAccepted
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

        return ServiceResult<AuthFlowResponse>.Ok(AuthFlowResponse.Authenticated(tokenResponse));
    }

    public async Task<ServiceResult<AuthFlowResponse>> LoginWithMicrosoftAsync(
        MicrosoftLoginRequest request,
        CancellationToken cancellationToken)
    {
        var verification = await microsoftAuthService.VerifyAccessTokenAsync(request.AccessToken, cancellationToken);
        if (!verification.Succeeded)
        {
            await WriteMicrosoftLoginFailureAuditAsync(null, verification.Error!.Code, cancellationToken);
            return ServiceResult<AuthFlowResponse>.Fail(
                verification.Error.Message,
                verification.Error.Code,
                verification.Error.StatusCode);
        }

        var identity = verification.Value!;
        var normalizedEmail = NormalizeEmail(identity.Email);
        var matches = await dbContext.UserAuthProviders
            .Include(x => x.User)
            .ThenInclude(x => x!.AuthProviders)
            .Where(x =>
                (x.ProviderType == ProviderTypeMicrosoftOidc && x.ProviderSubject == identity.ProviderSubject)
                || (x.User != null && x.User.NormalizedEmail == normalizedEmail))
            .ToListAsync(cancellationToken);

        var providerLink = matches.SingleOrDefault(x =>
            x.ProviderType == ProviderTypeMicrosoftOidc
            && x.ProviderSubject == identity.ProviderSubject);
        var emailUser = matches
            .Where(x => x.User?.NormalizedEmail == normalizedEmail)
            .Select(x => x.User!)
            .DistinctBy(x => x.Id)
            .SingleOrDefault();
        var now = DateTime.UtcNow;
        var createdViaMicrosoft = false;
        User user;

        if (providerLink is not null)
        {
            if (providerLink.User is null)
            {
                await WriteMicrosoftLoginFailureAuditAsync(null, "microsoft_provider_user_not_found", cancellationToken);
                return ServiceResult<AuthFlowResponse>.Fail(
                    "Microsoft account link is invalid. Contact support.",
                    "microsoft_provider_user_not_found",
                    StatusCodes.Status409Conflict);
            }

            user = providerLink.User;
        }
        else
        {
            if (emailUser is not null)
            {
                await WriteMicrosoftLoginFailureAuditAsync(emailUser.Id, "microsoft_account_link_required", cancellationToken);
                return ServiceResult<AuthFlowResponse>.Fail(
                    "An NSFinance account already uses this email. Sign in to that account and link Microsoft from Security settings.",
                    "microsoft_account_link_required",
                    StatusCodes.Status409Conflict);
            }

            var policyResult = await ResolveRegistrationPoliciesAsync(
                request.AcceptPolicies,
                request.TermsVersion,
                request.PrivacyVersion,
                cancellationToken);
            if (!policyResult.Succeeded)
            {
                return ServiceResult<AuthFlowResponse>.Fail(
                    policyResult.Error!.Message,
                    policyResult.Error.Code,
                    policyResult.Error.StatusCode);
            }

            var fullName = NormalizeFullName(identity.Name ?? $"{identity.GivenName} {identity.FamilyName}".Trim());
            var nsTag = await GenerateUniqueNsTagAsync(fullName, normalizedEmail, cancellationToken);
            user = new User
            {
                Id = Guid.NewGuid(),
                PrimaryEmail = normalizedEmail,
                NormalizedEmail = normalizedEmail,
                DisplayName = nsTag,
                FullName = fullName,
                Handle = nsTag,
                Status = "active",
                OnboardingStatus = "pending_email_verification",
                Role = "user",
                CreatedUtc = now,
                UpdatedUtc = now,
                EmailVerified = false,
                Timezone = "UTC",
                Locale = "en-US",
                PreferredCurrency = "EUR",
                PlanTier = "standard"
            };
            providerLink = new UserAuthProvider
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderType = ProviderTypeMicrosoftOidc,
                ProviderSubject = identity.ProviderSubject,
                LinkedAtUtc = now,
                LastUsedAtUtc = now,
                IsActive = true
            };
            dbContext.Users.Add(user);
            dbContext.UserAuthProviders.Add(providerLink);
            dbContext.UserPreferences.Add(new UserPreference
            {
                UserId = user.Id,
                UpdatedUtc = now
            });
            dbContext.PolicyAcceptances.AddRange(
                CreateRegistrationPolicyAcceptance(user.Id, policyResult.Value!.Terms, "terms_of_service", request.DeviceContext, now),
                CreateRegistrationPolicyAcceptance(user.Id, policyResult.Value.Privacy, "privacy_policy", request.DeviceContext, now));
            createdViaMicrosoft = true;
        }

        if (user.IsDisabled || user.IsSuspended)
        {
            await WriteMicrosoftLoginFailureAuditAsync(user.Id, "account_restricted", cancellationToken);
            return ServiceResult<AuthFlowResponse>.Fail(
                "Account access is restricted.",
                "account_restricted",
                StatusCodes.Status403Forbidden);
        }

        providerLink.ProviderSubject = identity.ProviderSubject;
        providerLink.IsActive = true;
        providerLink.LastUsedAtUtc = now;
        user.UpdatedUtc = now;

        if (string.IsNullOrWhiteSpace(user.FullName)
            || string.Equals(user.FullName, "NSFinance User", StringComparison.OrdinalIgnoreCase))
        {
            user.FullName = NormalizeFullName(identity.Name ?? $"{identity.GivenName} {identity.FamilyName}".Trim());
        }

        if (!user.EmailVerified)
        {
            StageAuditEvent(
                category: "auth",
                eventName: createdViaMicrosoft
                    ? "microsoft_user_created_pending_verification"
                    : "microsoft_email_verification_required",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new { provider = ProviderTypeMicrosoftOidc });

            var challengeResult = await identityChallengeService.CreateEmailCodeAsync(
                user,
                user.PrimaryEmail,
                IdentityChallengePurposes.EmailVerification,
                IdentityEmailRenderer.EmailVerificationTemplate,
                cancellationToken);
            if (!challengeResult.Succeeded)
            {
                return ServiceResult<AuthFlowResponse>.Fail(
                    challengeResult.Error!.Message,
                    challengeResult.Error.Code,
                    challengeResult.Error.StatusCode);
            }

            var challenge = challengeResult.Value!;
            return ServiceResult<AuthFlowResponse>.Ok(AuthFlowResponse.EmailVerificationRequired(
                new CodeDeliveryResponse(
                    challenge.ChallengeId,
                    challenge.ExpiresUtc,
                    challenge.ResendAfterSeconds,
                    "Confirm the email supplied by Microsoft to finish signing in.")));
        }

        var requiresMfa = await totpMfaService.IsEnabledAsync(user.Id, cancellationToken);
        var trustedDeviceAccepted = requiresMfa && await mfaTrustedDeviceService.ValidateAsync(
            user.Id,
            request.MfaTrustedDeviceToken,
            request.DeviceContext,
            cancellationToken);
        if (requiresMfa && !trustedDeviceAccepted)
        {
            StageAuditEvent(
                category: "auth",
                eventName: "login_first_factor_succeeded",
                targetEntityType: "user",
                targetEntityId: user.Id.ToString(),
                actorId: user.Id,
                actorType: "user",
                metadata: new { provider = ProviderTypeMicrosoftOidc, mfaRequired = true });
            await dbContext.SaveChangesAsync(cancellationToken);

            var mfaChallenge = await totpMfaService.CreateLoginChallengeAsync(user, cancellationToken);
            if (!mfaChallenge.Succeeded)
            {
                return ServiceResult<AuthFlowResponse>.Fail(
                    mfaChallenge.Error!.Message,
                    mfaChallenge.Error.Code,
                    mfaChallenge.Error.StatusCode);
            }

            return ServiceResult<AuthFlowResponse>.Ok(AuthFlowResponse.MfaRequired(mfaChallenge.Value!));
        }

        user.LastLoginUtc = now;
        var tokenResponse = await sessionService.CreateSessionAsync(
            user,
            request.DeviceContext,
            cancellationToken,
            saveImmediately: false,
            userIsNew: createdViaMicrosoft);
        StageAuditEvent(
            category: "auth",
            eventName: "microsoft_login_success",
            targetEntityType: "session",
            targetEntityId: tokenResponse.SessionId.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: new
            {
                provider = ProviderTypeMicrosoftOidc,
                createdViaMicrosoft,
                mfaTrustedDevice = trustedDeviceAccepted
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<AuthFlowResponse>.Ok(AuthFlowResponse.Authenticated(tokenResponse));
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

    public async Task<ServiceResult<CodeDeliveryResponse>> RequestPasswordResetAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var identity = request.Identity.Trim();
        if (identity.StartsWith('+'))
        {
            return ServiceResult<CodeDeliveryResponse>.Fail(
                "Phone recovery is not available until SMS delivery is configured.",
                "sms_recovery_unavailable",
                StatusCodes.Status503ServiceUnavailable);
        }

        var normalizedEmail = NormalizeEmail(identity);
        var user = await dbContext.Users
            .Include(x => x.PasswordCredential)
            .Include(x => x.AuthProviders)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        var eligibleUser = user is not null
            && !user.IsDisabled
            && !user.IsSuspended
            && user.PasswordCredential is not null
            && user.AuthProviders.Any(x => x.ProviderType == ProviderTypeLocalPassword && x.IsActive)
                ? user
                : null;

        var challengeResult = await identityChallengeService.CreateEmailCodeAsync(
            eligibleUser,
            normalizedEmail,
            IdentityChallengePurposes.PasswordReset,
            IdentityEmailRenderer.PasswordResetTemplate,
            cancellationToken);
        if (!challengeResult.Succeeded)
        {
            return ServiceResult<CodeDeliveryResponse>.Fail(
                challengeResult.Error!.Message,
                challengeResult.Error.Code,
                challengeResult.Error.StatusCode);
        }

        if (eligibleUser is not null)
        {
            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "password_reset_code_requested",
                targetEntityType: "user",
                targetEntityId: eligibleUser.Id.ToString(),
                actorId: null,
                actorType: "anonymous",
                metadata: new { channel = IdentityChannels.Email },
                cancellationToken);
        }

        var challenge = challengeResult.Value!;
        return ServiceResult<CodeDeliveryResponse>.Ok(new CodeDeliveryResponse(
            challenge.ChallengeId,
            challenge.ExpiresUtc,
            challenge.ResendAfterSeconds,
            "If an eligible account matches, a six-digit code will arrive shortly."));
    }

    public async Task<ServiceResult<PasswordRecoveryGrantResponse>> VerifyPasswordRecoveryCodeAsync(
        VerifyPasswordRecoveryCodeRequest request,
        CancellationToken cancellationToken)
    {
        var verification = await identityChallengeService.VerifyCodeAsync(
            request.ChallengeId,
            IdentityChallengePurposes.PasswordReset,
            request.Code,
            issueGrant: true,
            cancellationToken);
        if (!verification.Succeeded)
        {
            return ServiceResult<PasswordRecoveryGrantResponse>.Fail(
                verification.Error!.Message,
                verification.Error.Code,
                verification.Error.StatusCode);
        }

        var verified = verification.Value!;
        return ServiceResult<PasswordRecoveryGrantResponse>.Ok(new PasswordRecoveryGrantResponse(
            verified.Challenge.Id,
            verified.GrantToken!,
            verified.Challenge.GrantExpiresUtc!.Value));
    }

    public async Task<ServiceResult<AuthActionResponse>> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var grantResult = await identityChallengeService.ConsumeGrantAsync(
            request.ChallengeId,
            IdentityChallengePurposes.PasswordReset,
            request.RecoveryToken,
            cancellationToken);
        if (!grantResult.Succeeded)
        {
            return ServiceResult<AuthActionResponse>.Fail(
                grantResult.Error!.Message,
                grantResult.Error.Code,
                grantResult.Error.StatusCode);
        }

        var challenge = grantResult.Value!;
        var user = await dbContext.Users
            .Include(x => x.PasswordCredential)
            .SingleOrDefaultAsync(x => x.Id == challenge.UserId, cancellationToken);

        if (user is null || user.PasswordCredential is null)
        {
            return ServiceResult<AuthActionResponse>.Fail(
                "Recovery authorization is invalid or expired.",
                "recovery_grant_invalid",
                StatusCodes.Status400BadRequest);
        }

        user.PasswordCredential.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        user.PasswordCredential.RequiresRehash = false;
        user.PasswordCredential.UpdatedUtc = now;
        user.UpdatedUtc = now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<AuthActionResponse>.Fail(
                "Recovery authorization is invalid or expired.",
                "recovery_grant_invalid",
                StatusCodes.Status400BadRequest);
        }
        await sessionService.RevokeAllSessionsForUserAsync(user.Id, "password_reset", cancellationToken);
        await mfaTrustedDeviceService.RevokeAllForUserAsync(
            user.Id,
            "password_reset",
            cancellationToken);

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

    public async Task<ServiceResult<CodeDeliveryResponse>> RequestEmailVerificationAsync(
        RequestEmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        var eligibleUser = user is not null
            && !user.EmailVerified
            && !user.IsDisabled
            && !user.IsSuspended
                ? user
                : null;
        var challengeResult = await identityChallengeService.CreateEmailCodeAsync(
            eligibleUser,
            normalizedEmail,
            IdentityChallengePurposes.EmailVerification,
            IdentityEmailRenderer.EmailVerificationTemplate,
            cancellationToken);
        if (!challengeResult.Succeeded)
        {
            return ServiceResult<CodeDeliveryResponse>.Fail(
                challengeResult.Error!.Message,
                challengeResult.Error.Code,
                challengeResult.Error.StatusCode);
        }

        if (eligibleUser is not null)
        {
            await auditService.WriteEventAsync(
                category: "auth",
                eventName: "email_verification_code_requested",
                targetEntityType: "user",
                targetEntityId: eligibleUser.Id.ToString(),
                actorId: eligibleUser.Id,
                actorType: "user",
                metadata: new { channel = IdentityChannels.Email },
                cancellationToken);
        }

        var challenge = challengeResult.Value!;
        return ServiceResult<CodeDeliveryResponse>.Ok(new CodeDeliveryResponse(
            challenge.ChallengeId,
            challenge.ExpiresUtc,
            challenge.ResendAfterSeconds,
            "If the account needs verification, a six-digit code will arrive shortly."));
    }

    public async Task<ServiceResult<AuthTokenResponse>> ConfirmEmailVerificationAsync(
        ConfirmEmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var verification = await identityChallengeService.VerifyCodeAsync(
            request.ChallengeId,
            IdentityChallengePurposes.EmailVerification,
            request.Code,
            issueGrant: true,
            cancellationToken);
        if (!verification.Succeeded)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                verification.Error!.Message,
                verification.Error.Code,
                verification.Error.StatusCode);
        }

        var verified = verification.Value!;
        var grantResult = await identityChallengeService.ConsumeGrantAsync(
            verified.Challenge.Id,
            IdentityChallengePurposes.EmailVerification,
            verified.GrantToken!,
            cancellationToken);
        if (!grantResult.Succeeded)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                grantResult.Error!.Message,
                grantResult.Error.Code,
                grantResult.Error.StatusCode);
        }

        var now = DateTime.UtcNow;
        var user = await dbContext.Users.SingleOrDefaultAsync(
            x => x.Id == grantResult.Value!.UserId,
            cancellationToken);
        if (user is null)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                "The code is invalid or expired.",
                "identity_code_invalid",
                StatusCodes.Status400BadRequest);
        }

        user.EmailVerified = true;
        user.OnboardingStatus = "profile_created";
        user.LastLoginUtc = now;
        user.UpdatedUtc = now;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<AuthTokenResponse>.Fail(
                "The code is invalid or expired.",
                "identity_code_invalid",
                StatusCodes.Status400BadRequest);
        }

        var tokenResponse = await sessionService.CreateSessionAsync(user, request.DeviceContext, cancellationToken);

        if (transactionalMessageService.IsEmailConfigured)
        {
            transactionalMessageService.QueueEmail(
                user.Id,
                challengeId: null,
                user.PrimaryEmail,
                IdentityEmailRenderer.AccountCreatedTemplate,
                new IdentityEmailPayload(
                    user.DisplayName ?? user.FullName,
                    Code: null,
                    ExpiresInMinutes: null,
                    OccurredUtc: now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditService.WriteEventAsync(
            category: "auth",
            eventName: "email_verified",
            targetEntityType: "user",
            targetEntityId: user.Id.ToString(),
            actorId: user.Id,
            actorType: "user",
            metadata: null,
            cancellationToken);

        return ServiceResult<AuthTokenResponse>.Ok(tokenResponse);
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
        await mfaTrustedDeviceService.RevokeAllForUserAsync(
            user.Id,
            "password_changed",
            cancellationToken);

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

    public async Task<ServiceResult<CodeDeliveryResponse>> RequestPasswordChangeCodeAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<CodeDeliveryResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<CodeDeliveryResponse>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        var challengeResult = await identityChallengeService.CreateEmailCodeAsync(
            user,
            user.PrimaryEmail,
            IdentityChallengePurposes.PasswordChange,
            IdentityEmailRenderer.PasswordChangeTemplate,
            cancellationToken);
        if (!challengeResult.Succeeded)
        {
            return ServiceResult<CodeDeliveryResponse>.Fail(
                challengeResult.Error!.Message,
                challengeResult.Error.Code,
                challengeResult.Error.StatusCode);
        }

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "password_change_code_requested",
            targetEntityType: "user",
            targetEntityId: userId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        var challenge = challengeResult.Value!;
        return ServiceResult<CodeDeliveryResponse>.Ok(new CodeDeliveryResponse(
            challenge.ChallengeId,
            challenge.ExpiresUtc,
            challenge.ResendAfterSeconds,
            "Enter the six-digit code sent to your email."));
    }

    public async Task<ServiceResult<PasswordRecoveryGrantResponse>> VerifyPasswordChangeCodeAsync(
        VerifyPasswordChangeCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<PasswordRecoveryGrantResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var verification = await identityChallengeService.VerifyCodeAsync(
            request.ChallengeId,
            IdentityChallengePurposes.PasswordChange,
            request.Code,
            issueGrant: true,
            cancellationToken);
        if (!verification.Succeeded || verification.Value!.Challenge.UserId != userId)
        {
            return ServiceResult<PasswordRecoveryGrantResponse>.Fail(
                "The code is invalid or expired.",
                "identity_code_invalid",
                StatusCodes.Status400BadRequest);
        }

        var verified = verification.Value;
        return ServiceResult<PasswordRecoveryGrantResponse>.Ok(new PasswordRecoveryGrantResponse(
            verified.Challenge.Id,
            verified.GrantToken!,
            verified.Challenge.GrantExpiresUtc!.Value));
    }

    public async Task<ServiceResult<AuthActionResponse>> ConfirmPasswordChangeWithCodeAsync(
        Guid challengeId,
        string grantToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<AuthActionResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var grantResult = await identityChallengeService.ConsumeGrantAsync(
            challengeId,
            IdentityChallengePurposes.PasswordChange,
            grantToken,
            cancellationToken);
        if (!grantResult.Succeeded || grantResult.Value!.UserId != userId)
        {
            return ServiceResult<AuthActionResponse>.Fail(
                "Password-change authorization is invalid or expired.",
                "password_change_grant_invalid",
                StatusCodes.Status400BadRequest);
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
        await mfaTrustedDeviceService.RevokeAllForUserAsync(
            user.Id,
            "password_changed",
            cancellationToken);

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

    public async Task<ServiceResult<CodeDeliveryResponse>> RequestAccountDeletionCodeAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<CodeDeliveryResponse>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<CodeDeliveryResponse>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        var challengeResult = await identityChallengeService.CreateEmailCodeAsync(
            user,
            user.PrimaryEmail,
            IdentityChallengePurposes.AccountDeletion,
            IdentityEmailRenderer.AccountDeletionTemplate,
            cancellationToken);
        if (!challengeResult.Succeeded)
        {
            return ServiceResult<CodeDeliveryResponse>.Fail(
                challengeResult.Error!.Message,
                challengeResult.Error.Code,
                challengeResult.Error.StatusCode);
        }

        await auditService.WriteEventAsync(
            category: "security",
            eventName: "account_deletion_code_requested",
            targetEntityType: "user",
            targetEntityId: userId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: null,
            cancellationToken);

        var challenge = challengeResult.Value!;
        return ServiceResult<CodeDeliveryResponse>.Ok(new CodeDeliveryResponse(
            challenge.ChallengeId,
            challenge.ExpiresUtc,
            challenge.ResendAfterSeconds,
            "Enter the six-digit code sent to your email."));
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

    private Task WriteMicrosoftLoginFailureAuditAsync(
        Guid? actorId,
        string reason,
        CancellationToken cancellationToken)
    {
        return auditService.WriteEventAsync(
            category: "auth",
            eventName: "microsoft_login_failed",
            targetEntityType: "user",
            targetEntityId: actorId?.ToString(),
            actorId: actorId,
            actorType: actorId.HasValue ? "user" : "anonymous",
            metadata: new { provider = ProviderTypeMicrosoftOidc, reason },
            cancellationToken);
    }

    private static ServiceResult<AuthFlowResponse> InvalidCredentialsResult()
    {
        return ServiceResult<AuthFlowResponse>.Fail(
            "Email or password is incorrect.",
            "invalid_credentials",
            StatusCodes.Status401Unauthorized);
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
            user.TwoFactorEnabled,
            user.PlanTier,
            user.CreatedUtc,
            user.LastLoginUtc);
    }

    private static PolicyAcceptance CreateRegistrationPolicyAcceptance(
        Guid userId,
        PolicyVersion policyVersion,
        string policyType,
        DeviceContextDto? deviceContext,
        DateTime acceptedUtc)
    {
        return new PolicyAcceptance
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PolicyVersionId = policyVersion.Id,
            PolicyType = policyType,
            PolicyVersion = policyVersion.Version,
            AcceptedUtc = acceptedUtc,
            AcceptanceContext = "registration",
            Platform = deviceContext?.Platform,
            AppVersion = deviceContext?.AppVersion
        };
    }

    private async Task<ServiceResult<RegistrationPolicySet>> ResolveRegistrationPoliciesAsync(
        bool acceptPolicies,
        string? termsVersion,
        string? privacyVersion,
        CancellationToken cancellationToken)
    {
        if (!acceptPolicies)
        {
            return ServiceResult<RegistrationPolicySet>.Fail(
                "Accept the Terms of Service and Privacy Policy to create an account.",
                "registration_policy_acceptance_required",
                StatusCodes.Status400BadRequest);
        }

        var requiredPolicyTypes = new[] { "terms_of_service", "privacy_policy" };
        var activePolicies = await dbContext.PolicyVersions
            .AsNoTracking()
            .Include(x => x.PolicyDocument)
            .Where(x => x.IsActive && requiredPolicyTypes.Contains(x.PolicyDocument!.PolicyType))
            .ToListAsync(cancellationToken);
        var termsPolicy = activePolicies.SingleOrDefault(x => x.PolicyDocument!.PolicyType == "terms_of_service");
        var privacyPolicy = activePolicies.SingleOrDefault(x => x.PolicyDocument!.PolicyType == "privacy_policy");
        if (termsPolicy is null
            || privacyPolicy is null
            || !string.Equals(termsPolicy.Version, termsVersion?.Trim(), StringComparison.Ordinal)
            || !string.Equals(privacyPolicy.Version, privacyVersion?.Trim(), StringComparison.Ordinal))
        {
            return ServiceResult<RegistrationPolicySet>.Fail(
                "The Terms or Privacy Policy changed. Review the current versions and try again.",
                "registration_policy_version_changed",
                StatusCodes.Status409Conflict);
        }

        return ServiceResult<RegistrationPolicySet>.Ok(new RegistrationPolicySet(termsPolicy, privacyPolicy));
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
