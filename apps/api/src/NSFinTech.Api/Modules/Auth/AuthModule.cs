using Microsoft.AspNetCore.Authorization;
using NSFinTech.Api.Modules.Auth.Endpoints;

namespace NSFinTech.Api.Modules.Auth;

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

        group.MapGet("/providers/google/callback", GoogleCallbackEndpoint.HandleAsync)
            .WithName("GoogleAuthCallback")
            .RequireRateLimiting("provider-callback");

        return app;
    }
}
