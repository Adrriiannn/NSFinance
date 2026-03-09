namespace NSFinTech.Api.Persistence.Entities;

public class UserPreference
{
    public Guid UserId { get; set; }
    public string AdviceTonePreference { get; set; } = "balanced";
    public string DigestFrequency { get; set; } = "weekly";
    public string ReminderPreference { get; set; } = "important_only";
    public string NotificationPreferencesJson { get; set; } = "{}";
    public string PrivacyPreferencesJson { get; set; } = "{}";
    public string EssentialCategoryPreferencesJson { get; set; } = "{}";
    public string FutureGoalConfigurationJson { get; set; } = "{}";
    public DateTime UpdatedUtc { get; set; }

    public User? User { get; set; }
}
