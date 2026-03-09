using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable("UserPreferences");

        builder.HasKey(x => x.UserId);
        builder.Property(x => x.AdviceTonePreference).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DigestFrequency).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ReminderPreference).HasMaxLength(40).IsRequired();
        builder.Property(x => x.NotificationPreferencesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PrivacyPreferencesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.EssentialCategoryPreferencesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FutureGoalConfigurationJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");
    }
}
