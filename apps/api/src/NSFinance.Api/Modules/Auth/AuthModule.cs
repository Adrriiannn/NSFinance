using Microsoft.AspNetCore.Authorization;
using NSFinance.Api.Modules.Auth.Endpoints;

namespace NSFinance.Api.Modules.Auth;

public static class AuthModule
{
    public static IEndpointRouteBuilder MapAuthModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", RegisterEndpoint.HandleAsync)
            .WithName("Register")
            .RequireRateLimiting("auth-write");

        group.MapPost("/login", LoginEndpoint.HandleAsync)
            .WithName("Login")
            .RequireRateLimiting("auth-write");

        group.MapPost("/password-policy/check", PasswordPolicyCheckEndpoint.HandleAsync)
            .WithName("CheckPasswordPolicy")
            .RequireRateLimiting("password-policy-check");

        group.MapPost("/google", GoogleLoginEndpoint.HandleAsync)
            .WithName("GoogleLogin")
            .RequireRateLimiting("auth-write");

        group.MapPost("/microsoft", MicrosoftLoginEndpoint.HandleAsync)
            .WithName("MicrosoftLogin")
            .RequireRateLimiting("auth-write");

        group.MapPost("/refresh", RefreshTokenEndpoint.HandleAsync)
            .WithName("RefreshToken")
            .RequireRateLimiting("auth-refresh");

        group.MapPost("/logout", LogoutEndpoint.HandleAsync)
            .WithName("Logout")
            .RequireAuthorization();

        group.MapPost("/logout-all", LogoutAllEndpoint.HandleAsync)
            .WithName("LogoutAll")
            .RequireAuthorization();

        group.MapGet("/me", MeEndpoint.HandleAsync)
            .WithName("GetCurrentUser")
            .RequireAuthorization();

        group.MapGet("/sessions", GetSessionsEndpoint.HandleAsync)
            .WithName("GetSessions")
            .RequireAuthorization();

        group.MapDelete("/sessions/{sessionId:guid}", RevokeSessionEndpoint.HandleAsync)
            .WithName("RevokeSession")
            .RequireAuthorization();

        group.MapPost("/forgot-password", ForgotPasswordEndpoint.HandleAsync)
            .WithName("RequestPasswordReset")
            .RequireRateLimiting("auth-reset");

        group.MapPost("/reset-password", ResetPasswordEndpoint.HandleAsync)
            .WithName("ResetPassword")
            .RequireRateLimiting("auth-reset");

        group.MapPost("/password-recovery/verify", VerifyPasswordRecoveryCodeEndpoint.HandleAsync)
            .WithName("VerifyPasswordRecoveryCode")
            .RequireRateLimiting("auth-reset");

        group.MapPost("/mfa/challenge/verify", VerifyMfaLoginEndpoint.HandleAsync)
            .WithName("VerifyMfaLogin")
            .RequireRateLimiting("auth-write");

        group.MapGet("/mfa/status", GetMfaStatusEndpoint.HandleAsync)
            .WithName("GetMfaStatus")
            .RequireAuthorization();

        group.MapPost("/mfa/totp/enroll", BeginTotpEnrollmentEndpoint.HandleAsync)
            .WithName("BeginTotpEnrollment")
            .RequireAuthorization();

        group.MapPost("/mfa/totp/confirm", ConfirmTotpEnrollmentEndpoint.HandleAsync)
            .WithName("ConfirmTotpEnrollment")
            .RequireAuthorization()
            .RequireRateLimiting("auth-write");

        group.MapPost("/mfa/totp/disable", DisableMfaEndpoint.HandleAsync)
            .WithName("DisableMfa")
            .RequireAuthorization()
            .RequireRateLimiting("auth-write");

        group.MapPost("/verify-email/request", RequestEmailVerificationEndpoint.HandleAsync)
            .WithName("RequestEmailVerification")
            .RequireRateLimiting("auth-reset");

        group.MapPost("/verify-email/confirm", ConfirmEmailVerificationEndpoint.HandleAsync)
            .WithName("ConfirmEmailVerification")
            .RequireRateLimiting("auth-reset");

        group.MapPost("/change-password", ChangePasswordEndpoint.HandleAsync)
            .WithName("ChangePassword")
            .RequireAuthorization();

        group.MapPost("/change-password/request-code", RequestPasswordChangeCodeEndpoint.HandleAsync)
            .WithName("RequestPasswordChangeCode")
            .RequireAuthorization();

        group.MapPost("/change-password/verify-code", VerifyPasswordChangeCodeEndpoint.HandleAsync)
            .WithName("VerifyPasswordChangeCode")
            .RequireAuthorization();

        group.MapPost("/change-password/confirm", ConfirmPasswordChangeCodeEndpoint.HandleAsync)
            .WithName("ConfirmPasswordChangeWithCode")
            .RequireAuthorization();

        group.MapPost("/deletion/request-code", RequestAccountDeletionCodeEndpoint.HandleAsync)
            .WithName("RequestAccountDeletionCode")
            .RequireAuthorization();

        group.MapGet("/providers/google", GoogleAuthOptionsEndpoint.HandleAsync)
            .WithName("GetGoogleAuthOptions");

        group.MapGet("/providers/microsoft", MicrosoftAuthOptionsEndpoint.Handle)
            .WithName("GetMicrosoftAuthOptions");

        group.MapGet("/providers/google/callback", GoogleCallbackEndpoint.HandleAsync)
            .WithName("GoogleAuthCallback")
            .RequireRateLimiting("provider-callback");

        app.MapGet("/turnstile/register", TurnstileRegisterPageEndpoint.HandleAsync)
            .WithName("TurnstileRegisterPage")
            .AllowAnonymous();

        return app;
    }
}
