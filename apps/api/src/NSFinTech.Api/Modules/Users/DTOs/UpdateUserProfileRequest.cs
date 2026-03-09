namespace NSFinTech.Api.Modules.Users.DTOs;

public sealed record UpdateUserProfileRequest(
    string DisplayName,
    string Timezone,
    string Locale,
    string PreferredCurrency,
    string OnboardingStatus,
    bool BiometricUnlockEnabled);
