namespace NSFinTech.Api.Modules.Users.DTOs;

public sealed record UserProfileDetailsDto(
    Guid Id,
    string PrimaryEmail,
    string DisplayName,
    string Timezone,
    string Locale,
    string PreferredCurrency,
    string OnboardingStatus,
    bool BiometricUnlockEnabled,
    bool EmailVerified,
    string PlanTier,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc);
