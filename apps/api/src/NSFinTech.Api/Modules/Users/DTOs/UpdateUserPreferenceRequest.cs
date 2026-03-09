namespace NSFinTech.Api.Modules.Users.DTOs;

public sealed record UpdateUserPreferenceRequest(
    string AdviceTonePreference,
    string DigestFrequency,
    string ReminderPreference,
    string NotificationPreferencesJson,
    string PrivacyPreferencesJson,
    string EssentialCategoryPreferencesJson,
    string FutureGoalConfigurationJson);
