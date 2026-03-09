namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record UserProfileDto(
    Guid Id,
    string PrimaryEmail,
    string DisplayName,
    string Timezone,
    string Locale,
    string PreferredCurrency,
    string Role,
    bool EmailVerified,
    string OnboardingStatus,
    bool BiometricUnlockEnabled,
    string PlanTier,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc);
