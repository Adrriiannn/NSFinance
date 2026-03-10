namespace NSFinance.Api.Modules.Users.DTOs;

public sealed record UserPreferenceDto(
    string AdviceTonePreference,
    string DigestFrequency,
    string ReminderPreference,
    string NotificationPreferencesJson,
    string PrivacyPreferencesJson,
    string EssentialCategoryPreferencesJson,
    string FutureGoalConfigurationJson,
    DateTime UpdatedUtc);
