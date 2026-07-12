using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed record IdentityChallengeCreated(
    Guid ChallengeId,
    DateTime ExpiresUtc,
    int ResendAfterSeconds,
    string DeliveryState);

public sealed record IdentityChallengeVerified(
    IdentityChallenge Challenge,
    string? GrantToken);

public static class IdentityChallengePurposes
{
    public const string EmailVerification = "email_verification";
    public const string PasswordReset = "password_reset";
    public const string PasswordChange = "password_change";
    public const string AccountDeletion = "account_deletion";
    public const string MfaLogin = "mfa_login";
    public const string MfaSessionResume = "mfa_session_resume";
    public const string PhoneVerification = "phone_verification";
}

public static class IdentityChannels
{
    public const string Email = "email";
    public const string Sms = "sms";
    public const string Authenticator = "authenticator";
}
