namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record MfaStatusResponse(
    bool Enabled,
    string? Method,
    int RecoveryCodesRemaining);

public sealed record BeginTotpEnrollmentResponse(
    Guid AuthenticatorId,
    string Secret,
    string OtpAuthUri,
    DateTime ExpiresUtc);

public sealed record ConfirmTotpEnrollmentRequest(
    Guid AuthenticatorId,
    string Code);

public sealed record ConfirmTotpEnrollmentResponse(
    bool Enabled,
    string[] RecoveryCodes);

public sealed record VerifyMfaLoginRequest(
    Guid ChallengeId,
    string ChallengeToken,
    string Code,
    string Method,
    DeviceContextDto? DeviceContext);

public sealed record VerifyRememberedSessionMfaRequest(
    Guid ChallengeId,
    string ChallengeToken,
    string Code,
    string Method,
    string RefreshToken,
    DeviceContextDto? DeviceContext);

public sealed record DisableMfaRequest(
    string Code,
    string Method);
