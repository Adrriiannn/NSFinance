namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record UserProfileDto(
    Guid Id,
    string PrimaryEmail,
    string FullName,
    string DisplayName,
    string? Handle,
    string? ProfileImageUrl,
    string? ProfileSubtitle,
    string Timezone,
    string Locale,
    string PreferredCurrency,
    string Role,
    bool EmailVerified,
    string OnboardingStatus,
    bool BiometricUnlockEnabled,
    bool TwoFactorEnabled,
    string PlanTier,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc);
