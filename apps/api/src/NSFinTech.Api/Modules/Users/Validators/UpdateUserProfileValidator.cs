using NSFinTech.Api.Modules.Users.DTOs;

namespace NSFinTech.Api.Modules.Users.Validators;

public static class UpdateUserProfileValidator
{
    public static Dictionary<string, string[]> Validate(UpdateUserProfileRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length is < 2 or > 120)
        {
            errors["displayName"] = ["Display name must be between 2 and 120 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Timezone) || request.Timezone.Trim().Length > 64)
        {
            errors["timezone"] = ["Timezone is required and must not exceed 64 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Locale) || request.Locale.Trim().Length > 16)
        {
            errors["locale"] = ["Locale is required and must not exceed 16 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.PreferredCurrency) || request.PreferredCurrency.Trim().Length != 3)
        {
            errors["preferredCurrency"] = ["Preferred currency must be an ISO 3-letter code."];
        }

        if (string.IsNullOrWhiteSpace(request.OnboardingStatus) || request.OnboardingStatus.Trim().Length > 40)
        {
            errors["onboardingStatus"] = ["Onboarding status is required and must not exceed 40 characters."];
        }

        return errors;
    }
}
