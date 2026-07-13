namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record AuthFlowResponse(
    string Status,
    AuthTokenResponse? Session,
    MfaLoginChallengeResponse? MfaChallenge,
    CodeDeliveryResponse? EmailVerification)
{
    public static AuthFlowResponse Authenticated(AuthTokenResponse session) =>
        new("authenticated", session, null, null);

    public static AuthFlowResponse MfaRequired(MfaLoginChallengeResponse challenge) =>
        new("mfa_required", null, challenge, null);

    public static AuthFlowResponse EmailVerificationRequired(CodeDeliveryResponse challenge) =>
        new("email_verification_required", null, null, challenge);
}

public sealed record MfaLoginChallengeResponse(
    Guid ChallengeId,
    string ChallengeToken,
    DateTime ExpiresUtc,
    string[] Methods,
    string AccountHint);
