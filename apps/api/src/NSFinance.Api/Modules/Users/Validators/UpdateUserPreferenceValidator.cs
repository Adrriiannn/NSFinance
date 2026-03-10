using NSFinance.Api.Modules.Users.DTOs;

namespace NSFinance.Api.Modules.Users.Validators;

public static class UpdateUserPreferenceValidator
{
    public static Dictionary<string, string[]> Validate(UpdateUserPreferenceRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.AdviceTonePreference) || request.AdviceTonePreference.Trim().Length > 40)
        {
            errors["adviceTonePreference"] = ["Advice tone preference is required and must not exceed 40 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.DigestFrequency) || request.DigestFrequency.Trim().Length > 40)
        {
            errors["digestFrequency"] = ["Digest frequency is required and must not exceed 40 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.ReminderPreference) || request.ReminderPreference.Trim().Length > 40)
        {
            errors["reminderPreference"] = ["Reminder preference is required and must not exceed 40 characters."];
        }

        return errors;
    }
}
