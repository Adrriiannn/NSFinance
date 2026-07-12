namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record ConfirmEmailVerificationRequest(
    Guid ChallengeId,
    string Code,
    DeviceContextDto? DeviceContext);
