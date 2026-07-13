using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Google.Apis.Auth;
using OtpNet;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Auth.DTOs;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Modules.Auth.Services;
using NSFinance.Api.Modules.Auth.Validators;
using NSFinance.Api.Modules.Policies.DTOs;
using NSFinance.Api.Modules.Policies.Services;
using NSFinance.Api.Modules.Support.DTOs;
using NSFinance.Api.Modules.Support.Services;
using NSFinance.Api.Modules.Users.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Integration;

public class AuthAndTrustIntegrationTests
{
    [Fact]
    public async Task RegistrationFlow_RequiresEmailVerificationBeforeCreatingSession()
    {
        await using var harness = new TestHarness();

        var result = await harness.AuthService.RegisterAsync(
            new RegisterRequest(
                "register.flow@test.local",
                "ValidPassword123",
                "Integration User",
                "UTC",
                "en-US",
                "EUR",
                new DeviceContextDto("device-a", "Phone", "ios", "18", "1.0.0"),
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("email_verification_required", result.Value!.Status);
        Assert.Empty(await harness.DbContext.Sessions.ToListAsync());

        var confirmed = await harness.AuthService.ConfirmEmailVerificationAsync(
            new ConfirmEmailVerificationRequest(
                result.Value.ChallengeId,
                harness.IdentityCodeService.LastCreatedCode,
                new DeviceContextDto("device-a", "Phone", "ios", "18", "1.0.0")),
            CancellationToken.None);

        Assert.True(confirmed.Succeeded);
        Assert.NotEqual(Guid.Empty, confirmed.Value!.SessionId);
        Assert.Single(await harness.DbContext.Users.ToListAsync());
        Assert.Equal(2, await harness.DbContext.PolicyAcceptances.CountAsync());
        var queuedMessages = await harness.DbContext.TransactionalMessages.ToListAsync();
        Assert.Contains(queuedMessages, x => x.TemplateKey == IdentityEmailRenderer.EmailVerificationTemplate);
        Assert.Contains(queuedMessages, x => x.TemplateKey == IdentityEmailRenderer.AccountCreatedTemplate);
    }


    [Fact]
    public async Task RegistrationFlow_DefaultsProfileBioToEmpty()
    {
        await using var harness = new TestHarness();

        var result = await harness.AuthService.RegisterAsync(
            new RegisterRequest(
                "register.empty-bio@test.local",
                "ValidPassword123",
                "Empty Bio User",
                "UTC",
                "en-US",
                "EUR",
                new DeviceContextDto("device-b", "Phone", "ios", "18", "1.0.0"),
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);

        Assert.True(result.Succeeded);

        var user = await harness.DbContext.Users.SingleAsync(x => x.NormalizedEmail == "register.empty-bio@test.local");
        Assert.Null(user.ProfileSubtitle);
    }

    [Fact]
    public async Task LoginFlow_HandlesValidAndInvalidCredentials()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("login.flow@test.local", "ValidPassword123");

        var validLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("login.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.True(validLogin.Succeeded);

        var invalidLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("login.flow@test.local", "WrongPassword123", null),
            CancellationToken.None);
        Assert.False(invalidLogin.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, invalidLogin.Error?.StatusCode);
    }

    [Fact]
    public async Task GoogleLogin_ValidToken_CreatesUserProviderAndSession()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-valid-token",
            CreateGooglePayload("sub-google-1", "google.new.user@test.local", emailVerified: true, "Google User"));
        var saveCountBeforeLogin = harness.SaveChangesCount;

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest(
                "google-valid-token",
                new DeviceContextDto(null, "Android device", "android", "14", "1.0.0"),
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);

        var users = await harness.DbContext.Users.AsNoTracking().ToListAsync();
        Assert.Single(users);
        Assert.Equal("google.new.user@test.local", users[0].NormalizedEmail);
        Assert.True(users[0].EmailVerified);

        var providerLinks = await harness.DbContext.UserAuthProviders
            .AsNoTracking()
            .Where(x => x.ProviderType == "google_oidc")
            .ToListAsync();
        Assert.Single(providerLinks);
        Assert.Equal("sub-google-1", providerLinks[0].ProviderSubject);
        Assert.Equal(2, await harness.DbContext.PolicyAcceptances.CountAsync());

        var session = await harness.DbContext.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == result.Value!.Session!.SessionId);
        Assert.NotNull(session);

        var auditEvents = await harness.DbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.EventName.StartsWith("google_", StringComparison.Ordinal))
            .Select(x => x.EventName)
            .ToListAsync();
        Assert.Contains("google_user_created", auditEvents);
        Assert.Contains("google_provider_link_created", auditEvents);
        Assert.Contains("google_login_success", auditEvents);
        Assert.Equal(saveCountBeforeLogin + 1, harness.SaveChangesCount);
    }

    [Fact]
    public async Task GoogleLogin_ExistingLinkedUser_LogsInWithoutDuplicates()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-token-first",
            CreateGooglePayload("sub-google-2", "existing.linked@test.local", emailVerified: true, "Linked User"));

        var first = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest(
                "google-token-first",
                null,
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);
        Assert.True(first.Succeeded);

        harness.ConfigureGoogleToken(
            "google-token-second",
            CreateGooglePayload("sub-google-2", "email.changed@test.local", emailVerified: true, "Linked User"));

        var second = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-token-second", null),
            CancellationToken.None);
        Assert.True(second.Succeeded);

        var users = await harness.DbContext.Users.AsNoTracking().ToListAsync();
        Assert.Single(users);

        var providers = await harness.DbContext.UserAuthProviders
            .AsNoTracking()
            .Where(x => x.ProviderType == "google_oidc")
            .ToListAsync();
        Assert.Single(providers);
    }

    [Fact]
    public async Task GoogleLogin_ExistingProviderSubject_TakesPrecedenceOverAnotherUsersMatchingEmail()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-subject-owner-token",
            CreateGooglePayload("sub-google-subject-owner", "subject.owner@test.local", emailVerified: true, "Subject Owner"));

        var subjectOwner = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest(
                "google-subject-owner-token",
                null,
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);
        Assert.True(subjectOwner.Succeeded);

        await harness.RegisterAsync("google.changed.email@test.local", "ValidPassword123");
        harness.ConfigureGoogleToken(
            "google-subject-owner-changed-email-token",
            CreateGooglePayload("sub-google-subject-owner", "google.changed.email@test.local", emailVerified: true, "Subject Owner"));

        var secondLogin = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-subject-owner-changed-email-token", null),
            CancellationToken.None);

        Assert.True(secondLogin.Succeeded);
        Assert.Equal(subjectOwner.Value!.Session!.User.Id, secondLogin.Value!.Session!.User.Id);
    }

    [Fact]
    public async Task GoogleLogin_ExistingLocalUserWithVerifiedEmail_LinksProvider()
    {
        await using var harness = new TestHarness();
        var local = await harness.RegisterAsync("local.link@test.local", "ValidPassword123");

        harness.ConfigureGoogleToken(
            "google-link-token",
            CreateGooglePayload("sub-google-3", "local.link@test.local", emailVerified: true, "Local Linked"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-link-token", null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(local.User.Id, result.Value!.Session!.User.Id);

        var provider = await harness.DbContext.UserAuthProviders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == local.User.Id && x.ProviderType == "google_oidc");
        Assert.NotNull(provider);
        Assert.Equal("sub-google-3", provider!.ProviderSubject);
    }

    [Fact]
    public async Task GoogleLogin_InvalidToken_IsRejected()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleTokenFailure("google-invalid-token", new InvalidJwtException("invalid token"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-invalid-token", null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_id_token_invalid", result.Error?.Code);
    }

    [Fact]
    public async Task GoogleLogin_UnverifiedEmail_IsRejected()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-unverified-token",
            CreateGooglePayload("sub-google-4", "unverified@test.local", emailVerified: false, "Unverified User"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-unverified-token", null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_email_not_verified", result.Error?.Code);

        var users = await harness.DbContext.Users.AsNoTracking().ToListAsync();
        Assert.Empty(users);
    }

    [Fact]
    public async Task GoogleLogin_NewAccountWithoutPolicyAcceptance_IsRejected()
    {
        await using var harness = new TestHarness();
        harness.ConfigureGoogleToken(
            "google-policy-required-token",
            CreateGooglePayload("sub-google-policy", "google.policy@test.local", emailVerified: true, "Policy User"));

        var result = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-policy-required-token", null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("registration_policy_acceptance_required", result.Error?.Code);
        Assert.Empty(await harness.DbContext.Users.ToListAsync());
        Assert.Empty(await harness.DbContext.Sessions.ToListAsync());
    }

    [Fact]
    public async Task PasswordResetFlow_Works_EndToEnd()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("reset.flow@test.local", "ValidPassword123");

        var requestReset = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("reset.flow@test.local"),
            CancellationToken.None);
        Assert.True(requestReset.Succeeded);
        var verified = await harness.AuthService.VerifyPasswordRecoveryCodeAsync(
            new VerifyPasswordRecoveryCodeRequest(
                requestReset.Value!.ChallengeId,
                harness.IdentityCodeService.LastCreatedCode),
            CancellationToken.None);
        Assert.True(verified.Succeeded);

        var reset = await harness.AuthService.ResetPasswordAsync(
            new ResetPasswordRequest(
                verified.Value!.ChallengeId,
                verified.Value.RecoveryToken,
                "NewValidPassword123"),
            CancellationToken.None);
        Assert.True(reset.Succeeded);

        var oldPasswordLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("reset.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.False(oldPasswordLogin.Succeeded);

        var newPasswordLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("reset.flow@test.local", "NewValidPassword123", null),
            CancellationToken.None);
        Assert.True(newPasswordLogin.Succeeded);
    }

    [Fact]
    public async Task LogoutCurrentAndAllSessions_RevokeSessions()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("logout.flow@test.local", "ValidPassword123");
        var secondLogin = await harness.AuthService.LoginAsync(
            new LoginRequest("logout.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.True(secondLogin.Succeeded);

        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);
        var logoutCurrent = await harness.AuthService.LogoutCurrentSessionAsync(CancellationToken.None);
        Assert.True(logoutCurrent.Succeeded);

        harness.CurrentUserProvider.Set(register.User.Id, secondLogin.Value!.Session!.SessionId);
        var logoutAll = await harness.AuthService.LogoutAllAsync(CancellationToken.None);
        Assert.True(logoutAll.Succeeded);

        var sessions = await harness.DbContext.Sessions
            .Where(x => x.UserId == register.User.Id)
            .ToListAsync();

        Assert.Equal(2, sessions.Count);
        Assert.NotNull(sessions.Single(x => x.Id == register.SessionId).RevokedUtc);
        Assert.Null(sessions.Single(x => x.Id == secondLogin.Value!.Session!.SessionId).RevokedUtc);
    }

    [Fact]
    public async Task ProfileUpdate_PolicyAcceptance_AndDeletionRequest_AreRecorded()
    {
        await using var harness = new TestHarness();
        harness.SeedPolicy("terms_of_service", "1.0.0");
        var register = await harness.RegisterAsync("account.flow@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var profileUpdate = await harness.UserService.UpdateProfileAsync(
            new UpdateUserProfileRequest(
                "Updated Integration User",
                "updated-user",
                "updated-user",
                null,
                "Focused on: tracking subscriptions",
                "Europe/London",
                "en-GB",
                "GBP",
                "completed",
                null,
                "Ireland",
                ["Track subscriptions"],
                "employed",
                "stable",
                "saving"),
            CancellationToken.None);
        Assert.True(
            profileUpdate.Succeeded,
            $"Profile update failed with code '{profileUpdate.Error?.Code}' and message '{profileUpdate.Error?.Message}'.");
        Assert.Equal("updated-user", profileUpdate.Value!.DisplayName);

        var acceptPolicy = await harness.PolicyService.AcceptPolicyAsync(
            new AcceptPolicyRequest("terms_of_service", "1.0.0", "integration_test", "mobile", "1.0.0"),
            CancellationToken.None);
        Assert.True(acceptPolicy.Succeeded);

        var deletionCodeRequest = await harness.AuthService.RequestAccountDeletionCodeAsync(CancellationToken.None);
        Assert.True(deletionCodeRequest.Succeeded);
        var deletionCode = harness.IdentityCodeService.LastCreatedCode;
        Assert.False(string.IsNullOrWhiteSpace(deletionCode));

        var deleteRequest = await harness.SupportService.CreateDeletionRequestAsync(
            new CreateDeletionRequestRequest(
                deletionCodeRequest.Value!.ChallengeId,
                deletionCode,
                "integration test deletion"),
            CancellationToken.None);
        Assert.True(deleteRequest.Succeeded);

        Assert.Equal(2, await harness.DbContext.PolicyAcceptances.CountAsync());
        Assert.Single(await harness.DbContext.DeletionRequests.ToListAsync());
    }

    [Fact]
    public async Task SecurityNegativeCases_AreHandled()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("negative.flow@test.local", "ValidPassword123");

        var requestReset = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("negative.flow@test.local"),
            CancellationToken.None);
        Assert.True(requestReset.Succeeded);
        var resetRequest = Assert.IsType<CodeDeliveryResponse>(requestReset.Value);
        var expiredCode = harness.IdentityCodeService.LastCreatedCode;
        var challenge = await harness.DbContext.IdentityChallenges.SingleAsync(
            x => x.Id == resetRequest.ChallengeId);
        challenge.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        challenge.CreatedUtc = DateTime.UtcNow.AddMinutes(-2);
        await harness.DbContext.SaveChangesAsync();

        var expiredReset = await harness.AuthService.VerifyPasswordRecoveryCodeAsync(
            new VerifyPasswordRecoveryCodeRequest(resetRequest.ChallengeId, expiredCode),
            CancellationToken.None);
        Assert.False(expiredReset.Succeeded);
        Assert.Equal("identity_code_invalid", expiredReset.Error?.Code);

        var secondResetRequest = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("negative.flow@test.local"),
            CancellationToken.None);
        Assert.True(secondResetRequest.Succeeded);
        var secondCode = harness.IdentityCodeService.LastCreatedCode;
        var secondVerification = await harness.AuthService.VerifyPasswordRecoveryCodeAsync(
            new VerifyPasswordRecoveryCodeRequest(secondResetRequest.Value!.ChallengeId, secondCode),
            CancellationToken.None);
        Assert.True(secondVerification.Succeeded);

        var firstUse = await harness.AuthService.ResetPasswordAsync(
            new ResetPasswordRequest(
                secondVerification.Value!.ChallengeId,
                secondVerification.Value.RecoveryToken,
                "AnotherValidPassword123"),
            CancellationToken.None);
        Assert.True(firstUse.Succeeded);

        var reused = await harness.AuthService.ResetPasswordAsync(
            new ResetPasswordRequest(
                secondVerification.Value.ChallengeId,
                secondVerification.Value.RecoveryToken,
                "ThirdValidPassword123"),
            CancellationToken.None);
        Assert.False(reused.Succeeded);
        Assert.Equal("recovery_grant_invalid", reused.Error?.Code);

        var revokedRefresh = await harness.SessionService.RefreshAsync(register.RefreshToken, null, CancellationToken.None);
        Assert.False(revokedRefresh.Succeeded);

        harness.CurrentUserProvider.Clear();
        var unauthorizedProfile = await harness.UserService.GetProfileAsync(CancellationToken.None);
        Assert.False(unauthorizedProfile.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedProfile.Error?.StatusCode);

        var malformedPayloadErrors = LoginRequestValidator.Validate(new LoginRequest("", "", null));
        Assert.NotEmpty(malformedPayloadErrors);
    }

    [Fact]
    public async Task PasswordRecovery_UnknownAccountIsNeutralAndDoesNotQueueEmail()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("recovery.known@test.local", "ValidPassword123");

        var known = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("recovery.known@test.local"),
            CancellationToken.None);
        var queuedAfterKnownRequest = await harness.DbContext.TransactionalMessages.CountAsync();

        var unknown = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("recovery.unknown@test.local"),
            CancellationToken.None);

        Assert.True(known.Succeeded);
        Assert.True(unknown.Succeeded);
        Assert.Equal(known.Value!.Message, unknown.Value!.Message);
        Assert.Equal(queuedAfterKnownRequest, await harness.DbContext.TransactionalMessages.CountAsync());
        var dummyChallenge = await harness.DbContext.IdentityChallenges.SingleAsync(
            x => x.Id == unknown.Value.ChallengeId);
        Assert.Null(dummyChallenge.UserId);
    }

    [Fact]
    public async Task IdentityCode_LocksAfterFiveFailuresAndRejectsTheCorrectCode()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("recovery.lockout@test.local", "ValidPassword123");
        var request = await harness.AuthService.RequestPasswordResetAsync(
            new ForgotPasswordRequest("recovery.lockout@test.local"),
            CancellationToken.None);
        Assert.True(request.Succeeded);
        var correctCode = harness.IdentityCodeService.LastCreatedCode;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var rejected = await harness.AuthService.VerifyPasswordRecoveryCodeAsync(
                new VerifyPasswordRecoveryCodeRequest(request.Value!.ChallengeId, "000000"),
                CancellationToken.None);
            Assert.False(rejected.Succeeded);
            Assert.Equal("identity_code_invalid", rejected.Error?.Code);
        }

        var afterLockout = await harness.AuthService.VerifyPasswordRecoveryCodeAsync(
            new VerifyPasswordRecoveryCodeRequest(request.Value!.ChallengeId, correctCode),
            CancellationToken.None);
        Assert.False(afterLockout.Succeeded);

        var challenge = await harness.DbContext.IdentityChallenges.SingleAsync(
            x => x.Id == request.Value.ChallengeId);
        Assert.Equal(5, challenge.FailedAttempts);
        Assert.NotNull(challenge.ConsumedUtc);
    }

    [Fact]
    public async Task MicrosoftLogin_UsesTenantObjectIdentityAndRequiresEmailConfirmation()
    {
        await using var harness = new TestHarness();
        harness.ConfigureMicrosoftToken(
            "microsoft-new-user",
            CreateMicrosoftPrincipal(
                tenantId: "tenant-a",
                objectId: "object-a",
                subject: "subject-a",
                email: "microsoft.new@test.local",
                name: "Microsoft User"));

        var result = await harness.AuthService.LoginWithMicrosoftAsync(
            new MicrosoftLoginRequest(
                "microsoft-new-user",
                null,
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("email_verification_required", result.Value!.Status);
        Assert.Null(result.Value.Session);
        Assert.Empty(await harness.DbContext.Sessions.ToListAsync());
        var provider = await harness.DbContext.UserAuthProviders.SingleAsync();
        Assert.Equal("microsoft_oidc", provider.ProviderType);
        Assert.Equal("tenant-a:object-a", provider.ProviderSubject);
        Assert.Equal(2, await harness.DbContext.PolicyAcceptances.CountAsync());

        var confirmed = await harness.AuthService.ConfirmEmailVerificationAsync(
            new ConfirmEmailVerificationRequest(
                result.Value.EmailVerification!.ChallengeId,
                harness.IdentityCodeService.LastCreatedCode,
                null),
            CancellationToken.None);
        Assert.True(confirmed.Succeeded);
        Assert.Single(await harness.DbContext.Sessions.ToListAsync());
    }

    [Fact]
    public async Task MicrosoftLogin_RejectsTokenWithoutDelegatedApiScope()
    {
        await using var harness = new TestHarness();
        var principal = CreateMicrosoftPrincipal(
            tenantId: "tenant-a",
            objectId: "object-a",
            subject: "subject-a",
            email: "microsoft.scope@test.local",
            name: "Microsoft User");
        ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst("scp")!);
        harness.ConfigureMicrosoftToken("microsoft-missing-scope", principal);

        var result = await harness.AuthService.LoginWithMicrosoftAsync(
            new MicrosoftLoginRequest(
                "microsoft-missing-scope",
                null,
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("microsoft_token_invalid", result.Error!.Code);
        Assert.Empty(await harness.DbContext.Users.ToListAsync());
        Assert.Empty(await harness.DbContext.Sessions.ToListAsync());
    }

    [Fact]
    public async Task TotpMfa_BlocksSessionUntilSecondFactorAndConsumesRecoveryCodesOnce()
    {
        await using var harness = new TestHarness();
        var registered = await harness.RegisterAsync("mfa.flow@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(registered.User.Id, registered.SessionId);

        var enrollment = await harness.MfaService.BeginEnrollmentAsync(CancellationToken.None);
        Assert.True(enrollment.Succeeded);
        Assert.False((await harness.DbContext.Users.SingleAsync(x => x.Id == registered.User.Id)).TwoFactorEnabled);

        var totp = new Totp(Base32Encoding.ToBytes(enrollment.Value!.Secret));
        var confirmed = await harness.MfaService.ConfirmEnrollmentAsync(
            new ConfirmTotpEnrollmentRequest(enrollment.Value.AuthenticatorId, totp.ComputeTotp()),
            CancellationToken.None);
        Assert.True(confirmed.Succeeded);
        Assert.Equal(10, confirmed.Value!.RecoveryCodes.Length);

        harness.CurrentUserProvider.Clear();
        var sessionCountBeforeFirstFactor = await harness.DbContext.Sessions.CountAsync();
        var firstFactor = await harness.AuthService.LoginAsync(
            new LoginRequest("mfa.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.True(firstFactor.Succeeded);
        Assert.Equal("mfa_required", firstFactor.Value!.Status);
        Assert.Null(firstFactor.Value.Session);
        Assert.Equal("mfa****@test.local", firstFactor.Value.MfaChallenge!.AccountHint);
        Assert.Equal(sessionCountBeforeFirstFactor, await harness.DbContext.Sessions.CountAsync());

        var invalidCode = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                firstFactor.Value.MfaChallenge!.ChallengeId,
                firstFactor.Value.MfaChallenge.ChallengeToken,
                "not-a-code",
                "totp",
                null),
            CancellationToken.None);
        Assert.False(invalidCode.Succeeded);
        Assert.Equal("mfa_code_invalid", invalidCode.Error?.Code);

        var nextTimeStepCode = totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30));
        var secondFactor = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                firstFactor.Value.MfaChallenge!.ChallengeId,
                firstFactor.Value.MfaChallenge.ChallengeToken,
                nextTimeStepCode,
                "totp",
                null),
            CancellationToken.None);
        Assert.True(secondFactor.Succeeded);

        var recoveryCode = confirmed.Value.RecoveryCodes[0];
        var recoveryFirstFactor = await harness.AuthService.LoginAsync(
            new LoginRequest("mfa.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        var recoveryResult = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                recoveryFirstFactor.Value!.MfaChallenge!.ChallengeId,
                recoveryFirstFactor.Value.MfaChallenge.ChallengeToken,
                recoveryCode,
                "recovery_code",
                new DeviceContextDto("recovery-device", "Recovery Phone", "android", "16", "1.0.3"),
                RememberDevice: true),
            CancellationToken.None);
        Assert.True(recoveryResult.Succeeded);
        Assert.Null(recoveryResult.Value!.MfaTrustedDevice);

        var reuseFirstFactor = await harness.AuthService.LoginAsync(
            new LoginRequest("mfa.flow@test.local", "ValidPassword123", null),
            CancellationToken.None);
        var reusedRecoveryCode = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                reuseFirstFactor.Value!.MfaChallenge!.ChallengeId,
                reuseFirstFactor.Value.MfaChallenge.ChallengeToken,
                recoveryCode,
                "recovery_code",
                null),
            CancellationToken.None);
        Assert.False(reusedRecoveryCode.Succeeded);
        Assert.Equal("mfa_code_invalid", reusedRecoveryCode.Error?.Code);
    }

    [Fact]
    public async Task TotpMfa_ExpiredLoginChallengeRequiresFreshFirstFactor()
    {
        await using var harness = new TestHarness();
        var registered = await harness.RegisterAsync("mfa.expired@test.local", "ValidPassword123");
        await EnableTotpAsync(harness, registered.Value);

        var firstFactor = await harness.AuthService.LoginAsync(
            new LoginRequest("mfa.expired@test.local", "ValidPassword123", null),
            CancellationToken.None);
        Assert.True(firstFactor.Succeeded);
        Assert.Equal("mfa_required", firstFactor.Value!.Status);
        var mfaChallenge = firstFactor.Value.MfaChallenge!;

        var challenge = await harness.DbContext.IdentityChallenges.SingleAsync(
            x => x.Id == mfaChallenge.ChallengeId);
        challenge.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
        await harness.DbContext.SaveChangesAsync();
        var sessionCountBeforeVerification = await harness.DbContext.Sessions.CountAsync();

        var result = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                mfaChallenge.ChallengeId,
                mfaChallenge.ChallengeToken,
                "123456",
                "totp",
                null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("mfa_challenge_expired", result.Error?.Code);
        Assert.Equal(sessionCountBeforeVerification, await harness.DbContext.Sessions.CountAsync());
    }

    [Fact]
    public async Task RememberedSessionMfa_RequiresEnrollmentAndRotatesTheSameSession()
    {
        await using var harness = new TestHarness();
        var withoutMfa = await harness.RegisterAsync(
            "remembered.no-mfa@test.local",
            "ValidPassword123");

        var unavailable = await harness.AuthService.BeginRememberedSessionMfaAsync(
            new RefreshTokenRequest(withoutMfa.RefreshToken, null),
            CancellationToken.None);
        Assert.False(unavailable.Succeeded);
        Assert.Equal("mfa_not_enabled", unavailable.Error?.Code);

        var registered = await harness.RegisterAsync(
            "remembered.mfa@test.local",
            "ValidPassword123");
        var totp = await EnableTotpAsync(harness, registered.Value);
        var sessionCountBeforeResume = await harness.DbContext.Sessions.CountAsync();

        var challenge = await harness.AuthService.BeginRememberedSessionMfaAsync(
            new RefreshTokenRequest(registered.RefreshToken, null),
            CancellationToken.None);
        Assert.True(challenge.Succeeded);
        var mfaChallenge = challenge.Value!;
        Assert.Equal(
            IdentityChallengePurposes.MfaSessionResume,
            (await harness.DbContext.IdentityChallenges.SingleAsync(
                x => x.Id == mfaChallenge.ChallengeId)).Purpose);

        var nextCode = totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30));
        var wrongEndpoint = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                mfaChallenge.ChallengeId,
                mfaChallenge.ChallengeToken,
                nextCode,
                "totp",
                null),
            CancellationToken.None);
        Assert.False(wrongEndpoint.Succeeded);
        Assert.Equal("mfa_challenge_invalid", wrongEndpoint.Error?.Code);

        var resumed = await harness.AuthService.VerifyRememberedSessionMfaAsync(
            new VerifyRememberedSessionMfaRequest(
                mfaChallenge.ChallengeId,
                mfaChallenge.ChallengeToken,
                nextCode,
                "totp",
                registered.RefreshToken,
                null),
            CancellationToken.None);

        Assert.True(resumed.Succeeded);
        Assert.Equal(registered.SessionId, resumed.Value!.SessionId);
        Assert.NotEqual(registered.RefreshToken, resumed.Value.RefreshToken);
        Assert.Equal(sessionCountBeforeResume, await harness.DbContext.Sessions.CountAsync());
        Assert.NotNull((await harness.DbContext.SessionRefreshTokens.SingleAsync(
            x => x.TokenHash == harness.TokenSecretService.HashToken(registered.RefreshToken))).UsedUtc);
    }

    [Fact]
    public async Task RememberedSessionMfa_CannotRotateAnotherUsersRefreshToken()
    {
        await using var harness = new TestHarness();
        var protectedAccount = await harness.RegisterAsync(
            "remembered.protected@test.local",
            "ValidPassword123");
        var totp = await EnableTotpAsync(harness, protectedAccount.Value);
        var otherAccount = await harness.RegisterAsync(
            "remembered.other@test.local",
            "ValidPassword123");

        var challenge = await harness.AuthService.BeginRememberedSessionMfaAsync(
            new RefreshTokenRequest(protectedAccount.RefreshToken, null),
            CancellationToken.None);
        Assert.True(challenge.Succeeded);

        var result = await harness.AuthService.VerifyRememberedSessionMfaAsync(
            new VerifyRememberedSessionMfaRequest(
                challenge.Value!.ChallengeId,
                challenge.Value.ChallengeToken,
                totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30)),
                "totp",
                otherAccount.RefreshToken,
                null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("remembered_session_mismatch", result.Error?.Code);
        Assert.Null((await harness.DbContext.SessionRefreshTokens.SingleAsync(
            x => x.TokenHash == harness.TokenSecretService.HashToken(otherAccount.RefreshToken))).UsedUtc);
    }

    [Fact]
    public async Task MfaTrustedDevice_SkipsOnlyTheSameAccountsMfaOnTheSameDeviceUntilExpiry()
    {
        await using var harness = new TestHarness();
        var deviceA = new DeviceContextDto("trusted-device-a", "Phone A", "android", "16", "1.0.3");
        var deviceB = new DeviceContextDto("trusted-device-b", "Phone B", "android", "16", "1.0.3");
        var registered = await harness.RegisterAsync("trusted.mfa@test.local", "ValidPassword123");
        var totp = await EnableTotpAsync(harness, registered.Value);

        var firstFactor = await harness.AuthService.LoginAsync(
            new LoginRequest("trusted.mfa@test.local", "ValidPassword123", deviceA),
            CancellationToken.None);
        var verified = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                firstFactor.Value!.MfaChallenge!.ChallengeId,
                firstFactor.Value.MfaChallenge.ChallengeToken,
                totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30)),
                "totp",
                deviceA,
                RememberDevice: true),
            CancellationToken.None);

        Assert.True(verified.Succeeded);
        var trustedCredential = Assert.IsType<MfaTrustedDeviceCredentialResponse>(
            verified.Value!.MfaTrustedDevice);
        var storedCredential = await harness.DbContext.MfaTrustedDevices
            .Include(x => x.Device)
            .SingleAsync();
        Assert.Equal(harness.TokenSecretService.HashToken(trustedCredential.Token), storedCredential.TokenHash);
        Assert.NotEqual(trustedCredential.Token, storedCredential.TokenHash);
        Assert.Equal("trusted-device-a", storedCredential.Device!.DeviceFingerprint);
        Assert.InRange(
            trustedCredential.ExpiresUtc,
            DateTime.UtcNow.AddDays(29.9),
            DateTime.UtcNow.AddDays(30.1));

        var sameAccountAndDevice = await harness.AuthService.LoginAsync(
            new LoginRequest(
                "trusted.mfa@test.local",
                "ValidPassword123",
                deviceA,
                MfaTrustedDeviceToken: trustedCredential.Token),
            CancellationToken.None);
        Assert.True(sameAccountAndDevice.Succeeded);
        Assert.Equal("authenticated", sameAccountAndDevice.Value!.Status);

        var wrongDevice = await harness.AuthService.LoginAsync(
            new LoginRequest(
                "trusted.mfa@test.local",
                "ValidPassword123",
                deviceB,
                MfaTrustedDeviceToken: trustedCredential.Token),
            CancellationToken.None);
        Assert.True(wrongDevice.Succeeded);
        Assert.Equal("mfa_required", wrongDevice.Value!.Status);

        var otherAccount = await harness.RegisterAsync("trusted.other@test.local", "ValidPassword123");
        await EnableTotpAsync(harness, otherAccount.Value);
        var wrongAccount = await harness.AuthService.LoginAsync(
            new LoginRequest(
                "trusted.other@test.local",
                "ValidPassword123",
                deviceA,
                MfaTrustedDeviceToken: trustedCredential.Token),
            CancellationToken.None);
        Assert.True(wrongAccount.Succeeded);
        Assert.Equal("mfa_required", wrongAccount.Value!.Status);

        storedCredential.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
        await harness.DbContext.SaveChangesAsync();
        var expired = await harness.AuthService.LoginAsync(
            new LoginRequest(
                "trusted.mfa@test.local",
                "ValidPassword123",
                deviceA,
                MfaTrustedDeviceToken: trustedCredential.Token),
            CancellationToken.None);
        Assert.True(expired.Succeeded);
        Assert.Equal("mfa_required", expired.Value!.Status);
        Assert.Equal("expired", storedCredential.RevocationReason);
    }

    [Fact]
    public async Task MfaTrustedDevice_ResumesAndRotatesOnlyTheMatchingRememberedSession()
    {
        await using var harness = new TestHarness();
        var device = new DeviceContextDto("trusted-resume", "Trusted Phone", "android", "16", "1.0.3");
        var otherDevice = new DeviceContextDto("trusted-resume-other", "Other Phone", "android", "16", "1.0.3");
        var registered = await harness.RegisterAsync("trusted.resume@test.local", "ValidPassword123");
        var totp = await EnableTotpAsync(harness, registered.Value);
        var firstFactor = await harness.AuthService.LoginAsync(
            new LoginRequest("trusted.resume@test.local", "ValidPassword123", device),
            CancellationToken.None);
        var verified = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                firstFactor.Value!.MfaChallenge!.ChallengeId,
                firstFactor.Value.MfaChallenge.ChallengeToken,
                totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30)),
                "totp",
                device,
                RememberDevice: true),
            CancellationToken.None);
        var credential = verified.Value!.MfaTrustedDevice!;

        var resumed = await harness.AuthService.ResumeRememberedSessionWithTrustedDeviceAsync(
            new ResumeRememberedSessionWithTrustedDeviceRequest(
                verified.Value.RefreshToken,
                credential.Token,
                device),
            CancellationToken.None);
        Assert.True(resumed.Succeeded);
        Assert.Equal(verified.Value.SessionId, resumed.Value!.SessionId);
        Assert.NotEqual(verified.Value.RefreshToken, resumed.Value.RefreshToken);

        var wrongDevice = await harness.AuthService.ResumeRememberedSessionWithTrustedDeviceAsync(
            new ResumeRememberedSessionWithTrustedDeviceRequest(
                resumed.Value.RefreshToken,
                credential.Token,
                otherDevice),
            CancellationToken.None);
        Assert.False(wrongDevice.Succeeded);
        Assert.Equal("mfa_trusted_device_invalid", wrongDevice.Error?.Code);
        Assert.Null((await harness.DbContext.SessionRefreshTokens.SingleAsync(
            x => x.TokenHash == harness.TokenSecretService.HashToken(resumed.Value.RefreshToken))).UsedUtc);
    }

    [Fact]
    public async Task MfaTrustedDevice_DisablingAuthenticatorRevokesDeviceTrust()
    {
        await using var harness = new TestHarness();
        var device = new DeviceContextDto("trusted-disable", "Trusted Phone", "android", "16", "1.0.3");
        var registered = await harness.RegisterAsync("trusted.disable@test.local", "ValidPassword123");
        var totp = await EnableTotpAsync(harness, registered.Value);
        var firstFactor = await harness.AuthService.LoginAsync(
            new LoginRequest("trusted.disable@test.local", "ValidPassword123", device),
            CancellationToken.None);
        var verified = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                firstFactor.Value!.MfaChallenge!.ChallengeId,
                firstFactor.Value.MfaChallenge.ChallengeToken,
                totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30)),
                "totp",
                device,
                RememberDevice: true),
            CancellationToken.None);
        Assert.NotNull(verified.Value!.MfaTrustedDevice);

        var authenticator = await harness.DbContext.TotpAuthenticators.SingleAsync(
            x => x.UserId == registered.User.Id && x.DisabledUtc == null);
        authenticator.LastAcceptedTimeStep = null;
        await harness.DbContext.SaveChangesAsync();
        harness.CurrentUserProvider.Set(registered.User.Id, verified.Value.SessionId);
        var disabled = await harness.MfaService.DisableAsync(
            new DisableMfaRequest(
                totp.ComputeTotp(),
                "totp"),
            CancellationToken.None);

        Assert.True(disabled.Succeeded);
        var credential = await harness.DbContext.MfaTrustedDevices
            .Include(x => x.Device)
            .SingleAsync();
        Assert.NotNull(credential.RevokedUtc);
        Assert.Equal("mfa_disabled", credential.RevocationReason);
        Assert.False(credential.Device!.IsTrusted);
    }

    [Fact]
    public async Task GoogleLogin_EnabledTotp_BlocksFreshProviderLoginBeforeSessionIssuance()
    {
        await using var harness = new TestHarness();
        var device = new DeviceContextDto("google-trusted-device", "Google Phone", "android", "16", "1.0.3");
        var payload = CreateGooglePayload(
            "sub-google-mfa",
            "google.mfa@test.local",
            emailVerified: true,
            "Google MFA User");
        harness.ConfigureGoogleToken("google-mfa-enrollment-token", payload);

        var initialLogin = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest(
                "google-mfa-enrollment-token",
                null,
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);
        Assert.True(initialLogin.Succeeded);
        Assert.Equal("authenticated", initialLogin.Value!.Status);
        var totp = await EnableTotpAsync(harness, initialLogin.Value.Session!);

        var sessionCountBeforeFreshLogin = await harness.DbContext.Sessions.CountAsync();
        harness.ConfigureGoogleToken("google-mfa-fresh-token", payload);
        var freshLogin = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest("google-mfa-fresh-token", device),
            CancellationToken.None);

        Assert.True(freshLogin.Succeeded);
        Assert.Equal("mfa_required", freshLogin.Value!.Status);
        Assert.Null(freshLogin.Value.Session);
        Assert.NotNull(freshLogin.Value.MfaChallenge);
        Assert.Equal(sessionCountBeforeFreshLogin, await harness.DbContext.Sessions.CountAsync());

        var verified = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                freshLogin.Value.MfaChallenge!.ChallengeId,
                freshLogin.Value.MfaChallenge.ChallengeToken,
                totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30)),
                "totp",
                device,
                RememberDevice: true),
            CancellationToken.None);
        var trustedToken = verified.Value!.MfaTrustedDevice!.Token;
        harness.ConfigureGoogleToken("google-mfa-trusted-token", payload);
        var trustedLogin = await harness.AuthService.LoginWithGoogleAsync(
            new GoogleLoginRequest(
                "google-mfa-trusted-token",
                device,
                MfaTrustedDeviceToken: trustedToken),
            CancellationToken.None);
        Assert.True(trustedLogin.Succeeded);
        Assert.Equal("authenticated", trustedLogin.Value!.Status);
    }

    [Fact]
    public async Task MicrosoftLogin_EnabledTotp_BlocksFreshProviderLoginBeforeSessionIssuance()
    {
        await using var harness = new TestHarness();
        var device = new DeviceContextDto("microsoft-trusted-device", "Microsoft Phone", "android", "16", "1.0.3");
        var principal = CreateMicrosoftPrincipal(
            tenantId: "tenant-mfa",
            objectId: "object-mfa",
            subject: "subject-mfa",
            email: "microsoft.mfa@test.local",
            name: "Microsoft MFA User");
        harness.ConfigureMicrosoftToken("microsoft-mfa-enrollment-token", principal);

        var initialLogin = await harness.AuthService.LoginWithMicrosoftAsync(
            new MicrosoftLoginRequest(
                "microsoft-mfa-enrollment-token",
                null,
                AcceptPolicies: true,
                TermsVersion: "1.0.0",
                PrivacyVersion: "1.0.0"),
            CancellationToken.None);
        Assert.True(initialLogin.Succeeded);
        Assert.Equal("email_verification_required", initialLogin.Value!.Status);

        var confirmed = await harness.AuthService.ConfirmEmailVerificationAsync(
            new ConfirmEmailVerificationRequest(
                initialLogin.Value.EmailVerification!.ChallengeId,
                harness.IdentityCodeService.LastCreatedCode,
                null),
            CancellationToken.None);
        Assert.True(confirmed.Succeeded);
        var totp = await EnableTotpAsync(harness, confirmed.Value!);

        var sessionCountBeforeFreshLogin = await harness.DbContext.Sessions.CountAsync();
        harness.ConfigureMicrosoftToken("microsoft-mfa-fresh-token", principal);
        var freshLogin = await harness.AuthService.LoginWithMicrosoftAsync(
            new MicrosoftLoginRequest("microsoft-mfa-fresh-token", device),
            CancellationToken.None);

        Assert.True(freshLogin.Succeeded);
        Assert.Equal("mfa_required", freshLogin.Value!.Status);
        Assert.Null(freshLogin.Value.Session);
        Assert.NotNull(freshLogin.Value.MfaChallenge);
        Assert.Equal(sessionCountBeforeFreshLogin, await harness.DbContext.Sessions.CountAsync());

        var verified = await harness.AuthService.VerifyMfaLoginAsync(
            new VerifyMfaLoginRequest(
                freshLogin.Value.MfaChallenge!.ChallengeId,
                freshLogin.Value.MfaChallenge.ChallengeToken,
                totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30)),
                "totp",
                device,
                RememberDevice: true),
            CancellationToken.None);
        var trustedToken = verified.Value!.MfaTrustedDevice!.Token;
        harness.ConfigureMicrosoftToken("microsoft-mfa-trusted-token", principal);
        var trustedLogin = await harness.AuthService.LoginWithMicrosoftAsync(
            new MicrosoftLoginRequest(
                "microsoft-mfa-trusted-token",
                device,
                MfaTrustedDeviceToken: trustedToken),
            CancellationToken.None);
        Assert.True(trustedLogin.Succeeded);
        Assert.Equal("authenticated", trustedLogin.Value!.Status);
    }

    [Fact]
    public async Task ExportRequest_CreatesReadyPackage_AndSupportsDownload()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("export.flow@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var connectionId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        harness.DbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = register.User.Id,
            ProviderName = "TrueLayer",
            ProviderEnvironment = "live",
            ProviderDisplayName = "Test Bank",
            Status = "connected",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = "provider-account-1",
            DisplayName = "Transaction Account 1",
            Currency = "GBP",
            RawPayloadJson = "{}",
            CreatedUtc = now,
            UpdatedUtc = now
        });
        harness.DbContext.BankBalanceSnapshots.Add(new BankBalanceSnapshot
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            Current = 50.00m,
            Available = 50.00m,
            Currency = "GBP",
            CapturedUtc = now,
            RawPayloadJson = "{}"
        });
        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = register.User.Id,
            Name = "Transaction Account 1",
            Type = "transaction",
            Currency = "GBP",
            CreatedUtc = now
        });
        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = accountId,
            Amount = -9.99m,
            Currency = "GBP",
            Description = "Coffee Shop",
            BookedAtUtc = now,
            CreatedUtc = now
        });

        await harness.DbContext.SaveChangesAsync();

        var result = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("Integration export request"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("ready", result.Value!.Status);

        var stored = await harness.DbContext.ExportRequests.AsNoTracking().SingleAsync(x => x.Id == result.Value.Id);
        Assert.False(string.IsNullOrWhiteSpace(stored.ArtifactReference));
        Assert.True(File.Exists(stored.ArtifactReference));

        var downloadResult = await harness.SupportService.DownloadExportRequestAsync(result.Value.Id, CancellationToken.None);
        Assert.True(downloadResult.Succeeded);
        Assert.NotNull(downloadResult.Value);
        Assert.NotEmpty(downloadResult.Value!.Bytes);
    }

    [Fact]
    public async Task ExportRequest_ReusesSingleRecord_AndRemovesOlderOnes()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("export-single@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var first = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("first export"),
            CancellationToken.None);
        Assert.True(first.Succeeded);

        await Task.Delay(5);

        var second = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("second export"),
            CancellationToken.None);
        Assert.True(second.Succeeded);

        var requestsInDb = await harness.DbContext.ExportRequests
            .AsNoTracking()
            .Where(x => x.UserId == register.User.Id)
            .ToListAsync();

        Assert.Single(requestsInDb);
        Assert.Equal(second.Value!.Id, requestsInDb[0].Id);

        var listResult = await harness.SupportService.GetMyExportRequestsAsync(CancellationToken.None);
        Assert.True(listResult.Succeeded);
        Assert.Single(listResult.Value!);
        Assert.Equal(second.Value!.Id, listResult.Value![0].Id);
    }

    [Fact]
    public async Task ExportRequest_ExpiresAfterRetentionWindow_AndDownloadIsRejected()
    {
        await using var harness = new TestHarness();
        var register = await harness.RegisterAsync("export-expiry@test.local", "ValidPassword123");
        harness.CurrentUserProvider.Set(register.User.Id, register.SessionId);

        var created = await harness.SupportService.CreateExportRequestAsync(
            new CreateExportRequestRequest("expiry test"),
            CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.NotNull(created.Value);

        var request = await harness.DbContext.ExportRequests.SingleAsync(x => x.Id == created.Value!.Id);
        Assert.False(string.IsNullOrWhiteSpace(request.ArtifactReference));
        Assert.True(File.Exists(request.ArtifactReference));

        request.RequestedUtc = DateTime.UtcNow.AddMinutes(-16);
        request.UpdatedUtc = request.RequestedUtc;
        await harness.DbContext.SaveChangesAsync();

        var downloadResult = await harness.SupportService.DownloadExportRequestAsync(request.Id, CancellationToken.None);
        Assert.False(downloadResult.Succeeded);
        Assert.Equal("export_expired", downloadResult.Error?.Code);
        Assert.Equal(StatusCodes.Status410Gone, downloadResult.Error?.StatusCode);

        var reloaded = await harness.DbContext.ExportRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        Assert.Equal("expired", reloaded.Status);
        Assert.Null(reloaded.ArtifactReference);
    }

    [Fact]
    public async Task AbuseProtection_TriggersLockoutAfterRepeatedFailures()
    {
        await using var harness = new TestHarness();
        await harness.RegisterAsync("abuse.flow@test.local", "ValidPassword123");

        for (var i = 0; i < 6; i++)
        {
            await harness.AuthService.LoginAsync(
                new LoginRequest("abuse.flow@test.local", "WrongPassword123", null),
                CancellationToken.None);
        }

        var lockedResult = await harness.AuthService.LoginAsync(
            new LoginRequest("abuse.flow@test.local", "WrongPassword123", null),
            CancellationToken.None);

        Assert.False(lockedResult.Succeeded);
        Assert.Equal(StatusCodes.Status429TooManyRequests, lockedResult.Error?.StatusCode);
    }

    private static GoogleJsonWebSignature.Payload CreateGooglePayload(
        string subject,
        string email,
        bool emailVerified,
        string name)
    {
        return new GoogleJsonWebSignature.Payload
        {
            Subject = subject,
            Email = email,
            EmailVerified = emailVerified,
            Name = name,
            GivenName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            FamilyName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault(),
            Picture = "https://example.com/avatar.png"
        };
    }

    private static ClaimsPrincipal CreateMicrosoftPrincipal(
        string tenantId,
        string objectId,
        string subject,
        string email,
        string name)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("tid", tenantId),
            new Claim("oid", objectId),
            new Claim("sub", subject),
            new Claim("preferred_username", email),
            new Claim("name", name),
            new Claim("scp", MicrosoftAuthOptions.DelegatedScopeName),
            new Claim("azp", "microsoft-client-id")
        ], "microsoft-test");
        return new ClaimsPrincipal(identity);
    }

    private static async Task<Totp> EnableTotpAsync(TestHarness harness, AuthTokenResponse session)
    {
        harness.CurrentUserProvider.Set(session.User.Id, session.SessionId);
        var enrollment = await harness.MfaService.BeginEnrollmentAsync(CancellationToken.None);
        Assert.True(enrollment.Succeeded);

        var totp = new Totp(Base32Encoding.ToBytes(enrollment.Value!.Secret));
        var confirmed = await harness.MfaService.ConfirmEnrollmentAsync(
            new ConfirmTotpEnrollmentRequest(enrollment.Value.AuthenticatorId, totp.ComputeTotp()),
            CancellationToken.None);
        Assert.True(confirmed.Succeeded);
        harness.CurrentUserProvider.Clear();
        return totp;
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly CountingSaveChangesInterceptor _saveChangesInterceptor = new();

        public AppDbContext DbContext { get; }
        public int SaveChangesCount => _saveChangesInterceptor.SaveCount;
        public MutableCurrentUserProvider CurrentUserProvider { get; }
        public DeterministicTokenSecretService TokenSecretService { get; }
        public DeterministicIdentityCodeService IdentityCodeService { get; }
        public SessionService SessionService { get; }
        public StubGoogleIdTokenVerifier GoogleIdTokenVerifier { get; }
        public StubMicrosoftAccessTokenVerifier MicrosoftAccessTokenVerifier { get; }
        public TotpMfaService MfaService { get; }
        public MfaTrustedDeviceService TrustedDeviceService { get; }
        public AuthService AuthService { get; }
        public UserService UserService { get; }
        public PolicyService PolicyService { get; }
        public SupportService SupportService { get; }

        private readonly JwtOptions _jwtOptions;

        public TestHarness()
        {
            DbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"integration-tests-{Guid.NewGuid():N}")
                .AddInterceptors(_saveChangesInterceptor)
                .Options);

            _jwtOptions = new JwtOptions
            {
                Issuer = "NSFinance.Api",
                Audience = "NSFinance.Mobile",
                SigningKey = "Integration_Testing_Jwt_Signing_Key_Change_Me_123456789",
                AccessTokenMinutes = 15,
                RefreshTokenDays = 30,
                PasswordResetTokenMinutes = 30,
                EmailVerificationTokenMinutes = 60,
                MaxFailedLoginAttempts = 6,
                FailedLoginWindowMinutes = 15,
                LoginLockoutMinutes = 10
            };

            CurrentUserProvider = new MutableCurrentUserProvider();
            TokenSecretService = new DeterministicTokenSecretService();
            GoogleIdTokenVerifier = new StubGoogleIdTokenVerifier();
            var identityOptions = Options.Create(new IdentitySecurityOptions
            {
                CodePepper = "Integration_Test_Identity_Code_Pepper_123456789",
                ChallengeLifetimeMinutes = 10,
                RecoveryGrantLifetimeMinutes = 10,
                MfaChallengeLifetimeMinutes = 10,
                ResendCooldownSeconds = 1,
                MaxCodeAttempts = 5,
                RecoveryCodeCount = 10,
                TotpIssuer = "NSFinance Test"
            });
            IdentityCodeService = new DeterministicIdentityCodeService(new IdentityCodeService(identityOptions));
            var dataProtectionProvider = new EphemeralDataProtectionProvider();
            var emailOptions = Options.Create(new TransactionalEmailOptions
            {
                Enabled = true,
                Endpoint = "https://example.communication.azure.com",
                SenderAddress = "security@test.local",
                MaxAttempts = 3
            });

            var jwtOptions = Options.Create(_jwtOptions);
            var requestContext = new TestRequestContextAccessor();
            var auditService = new AuditService(DbContext, requestContext, NullLogger<AuditService>.Instance);
            var googleAuthService = new GoogleAuthService(
                Options.Create(new GoogleAuthOptions
                {
                    ClientId = "google-client-id"
                }),
                GoogleIdTokenVerifier,
                NullLogger<GoogleAuthService>.Instance);
            MicrosoftAccessTokenVerifier = new StubMicrosoftAccessTokenVerifier();
            var microsoftAuthService = new MicrosoftAuthService(
                Options.Create(new MicrosoftAuthOptions { ClientId = "microsoft-client-id" }),
                MicrosoftAccessTokenVerifier,
                NullLogger<MicrosoftAuthService>.Instance);
            var messageService = new TransactionalMessageService(
                DbContext,
                new IdentityPayloadProtector(dataProtectionProvider),
                new RecordingEmailSender(),
                emailOptions);
            var challengeService = new IdentityChallengeService(
                DbContext,
                IdentityCodeService,
                TokenSecretService,
                messageService,
                requestContext,
                identityOptions);

            SessionService = new SessionService(
                DbContext,
                new JwtTokenService(jwtOptions),
                TokenSecretService,
                jwtOptions,
                requestContext,
                NullLogger<SessionService>.Instance);
            TrustedDeviceService = new MfaTrustedDeviceService(
                DbContext,
                TokenSecretService,
                identityOptions);
            MfaService = new TotpMfaService(
                DbContext,
                CurrentUserProvider,
                new MfaSecretProtector(dataProtectionProvider),
                IdentityCodeService,
                TokenSecretService,
                TrustedDeviceService,
                auditService,
                identityOptions);

            AuthService = new AuthService(
                DbContext,
                new Pbkdf2PasswordHasher(),
                SessionService,
                googleAuthService,
                microsoftAuthService,
                challengeService,
                messageService,
                MfaService,
                TrustedDeviceService,
                CurrentUserProvider,
                new AuthAbuseService(DbContext, jwtOptions),
                auditService,
                requestContext,
                jwtOptions,
                NullLogger<AuthService>.Instance);

            UserService = new UserService(DbContext, CurrentUserProvider, auditService);
            PolicyService = new PolicyService(DbContext, CurrentUserProvider, auditService);
            SupportService = new SupportService(
                DbContext,
                CurrentUserProvider,
                auditService,
                requestContext,
                challengeService,
                NullLogger<SupportService>.Instance);

            SeedPolicy("terms_of_service", "1.0.0");
            SeedPolicy("privacy_policy", "1.0.0");
        }

        public void ConfigureGoogleToken(string idToken, GoogleJsonWebSignature.Payload payload)
        {
            GoogleIdTokenVerifier.ConfigureSuccess(idToken, payload);
        }

        public void ConfigureGoogleTokenFailure(string idToken, Exception exception)
        {
            GoogleIdTokenVerifier.ConfigureFailure(idToken, exception);
        }

        public void ConfigureMicrosoftToken(string accessToken, ClaimsPrincipal principal)
        {
            MicrosoftAccessTokenVerifier.ConfigureSuccess(accessToken, principal);
        }

        public async Task<(AuthTokenResponse Value, UserProfileDto User, Guid SessionId, string RefreshToken)> RegisterAsync(string email, string password)
        {
            var result = await AuthService.RegisterAsync(
                new RegisterRequest(
                    email,
                    password,
                    "Integration User",
                    "UTC",
                    "en-US",
                    "EUR",
                    null,
                    CaptchaToken: null,
                    AcceptPolicies: true,
                    TermsVersion: "1.0.0",
                    PrivacyVersion: "1.0.0"),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Value);

            var confirmed = await AuthService.ConfirmEmailVerificationAsync(
                new ConfirmEmailVerificationRequest(
                    result.Value!.ChallengeId,
                    IdentityCodeService.LastCreatedCode,
                    null),
                CancellationToken.None);
            Assert.True(confirmed.Succeeded);
            Assert.NotNull(confirmed.Value);

            return (confirmed.Value!, confirmed.Value!.User, confirmed.Value.SessionId, confirmed.Value.RefreshToken);
        }

        public void SeedPolicy(string policyType, string version)
        {
            var existing = DbContext.PolicyVersions
                .Include(x => x.PolicyDocument)
                .SingleOrDefault(x => x.PolicyDocument!.PolicyType == policyType && x.Version == version);
            if (existing is not null)
            {
                existing.IsActive = true;
                DbContext.SaveChanges();
                return;
            }

            var document = new PolicyDocument
            {
                Id = Guid.NewGuid(),
                PolicyType = policyType,
                Name = policyType,
                CreatedUtc = DateTime.UtcNow
            };
            DbContext.PolicyDocuments.Add(document);
            DbContext.PolicyVersions.Add(new PolicyVersion
            {
                Id = Guid.NewGuid(),
                PolicyDocumentId = document.Id,
                Version = version,
                EffectiveUtc = DateTime.UtcNow,
                ContentReference = $"legal/{policyType}/{version}",
                ContentMarkdown = $"# {policyType} {version}",
                IsActive = true,
                CreatedUtc = DateTime.UtcNow
            });
            DbContext.SaveChanges();
        }

        public ValueTask DisposeAsync()
        {
            return DbContext.DisposeAsync();
        }
    }

    private sealed class CountingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            SaveCount++;
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class StubGoogleIdTokenVerifier : IGoogleIdTokenVerifier
    {
        private readonly Dictionary<string, GoogleJsonWebSignature.Payload> _successPayloads = [];
        private readonly Dictionary<string, Exception> _failures = [];

        public void ConfigureSuccess(string idToken, GoogleJsonWebSignature.Payload payload)
        {
            _failures.Remove(idToken);
            _successPayloads[idToken] = payload;
        }

        public void ConfigureFailure(string idToken, Exception exception)
        {
            _successPayloads.Remove(idToken);
            _failures[idToken] = exception;
        }

        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(
            string idToken,
            IReadOnlyCollection<string> audiences,
            CancellationToken cancellationToken)
        {
            if (_failures.TryGetValue(idToken, out var failure))
            {
                throw failure;
            }

            if (_successPayloads.TryGetValue(idToken, out var payload))
            {
                return Task.FromResult(payload);
            }

            throw new InvalidJwtException("Token not configured in test verifier.");
        }
    }

    private sealed class MutableCurrentUserProvider : ICurrentUserProvider
    {
        private Guid? _userId;
        private Guid? _sessionId;

        public Guid UserId => _userId ?? throw new InvalidOperationException("No active user.");

        public void Set(Guid userId, Guid sessionId)
        {
            _userId = userId;
            _sessionId = sessionId;
        }

        public void Clear()
        {
            _userId = null;
            _sessionId = null;
        }

        public bool TryGetUserId(out Guid userId)
        {
            userId = _userId ?? Guid.Empty;
            return _userId.HasValue;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = _sessionId ?? Guid.Empty;
            return _sessionId.HasValue;
        }
    }

    private sealed class DeterministicTokenSecretService : TokenSecretService
    {
        private int _counter;

        public string LastCreatedToken { get; private set; } = string.Empty;

        public override string CreateToken(int bytes = 48)
        {
            _counter++;
            LastCreatedToken = $"integration-token-{_counter}";
            return LastCreatedToken;
        }
    }

    private sealed class DeterministicIdentityCodeService(IIdentityCodeService inner) : IIdentityCodeService
    {
        private int _counter = 123455;

        public string LastCreatedCode { get; private set; } = string.Empty;

        public string CreateSixDigitCode()
        {
            LastCreatedCode = Interlocked.Increment(ref _counter).ToString("D6");
            return LastCreatedCode;
        }

        public string HashChallengeSecret(Guid challengeId, string secret) =>
            inner.HashChallengeSecret(challengeId, secret);

        public bool VerifyChallengeSecret(Guid challengeId, string secret, string expectedHash) =>
            inner.VerifyChallengeSecret(challengeId, secret, expectedHash);

        public string HashDestination(string channel, string destination) =>
            inner.HashDestination(channel, destination);

        public string HashRecoveryCode(Guid authenticatorId, string code) =>
            inner.HashRecoveryCode(authenticatorId, code);

        public bool VerifyRecoveryCode(Guid authenticatorId, string code, string expectedHash) =>
            inner.VerifyRecoveryCode(authenticatorId, code, expectedHash);
    }

    private sealed class RecordingEmailSender : ITransactionalEmailSender
    {
        public bool IsConfigured => true;

        public Task<TransactionalEmailSendResult> SendAsync(
            string recipient,
            RenderedIdentityEmail message,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(TransactionalEmailSendResult.Success(Guid.NewGuid().ToString("N")));
        }
    }

    private sealed class StubMicrosoftAccessTokenVerifier : IMicrosoftAccessTokenVerifier
    {
        private readonly Dictionary<string, ClaimsPrincipal> _principals = [];

        public void ConfigureSuccess(string accessToken, ClaimsPrincipal principal)
        {
            _principals[accessToken] = principal;
        }

        public Task<ClaimsPrincipal> ValidateAsync(string accessToken, CancellationToken cancellationToken)
        {
            return _principals.TryGetValue(accessToken, out var principal)
                ? Task.FromResult(principal)
                : throw new InvalidOperationException("Microsoft token was not configured for this test.");
        }
    }

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "integration-correlation-id";
        public string SourceChannel => "api";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "integration-tests";
        public string? Platform => "ios";
        public string? AppVersion => "1.0.0";
    }
}
