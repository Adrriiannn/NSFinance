using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.PrimaryEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Handle).HasMaxLength(80);
        builder.Property(x => x.ProfileImageUrl).HasMaxLength(512);
        builder.Property(x => x.ProfileSubtitle).HasMaxLength(180);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.OnboardingStatus).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastLoginUtc);
        builder.Property(x => x.Timezone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Locale).HasMaxLength(16).IsRequired();
        builder.Property(x => x.PreferredCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PlanTier).HasMaxLength(40).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(40);
        builder.Property(x => x.CountryRegion).HasMaxLength(80);
        builder.Property(x => x.EmploymentStatus).HasMaxLength(40);
        builder.Property(x => x.IncomeStability).HasMaxLength(40);
        builder.Property(x => x.PrimaryFinancialConcern).HasMaxLength(60);
        builder.Property(x => x.FinancialFocusJson).HasColumnType("jsonb");
        builder.Property(x => x.SupportFlagsJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.HasOne(x => x.PasswordCredential)
            .WithOne(x => x.User)
            .HasForeignKey<PasswordCredential>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Preferences)
            .WithOne(x => x.User)
            .HasForeignKey<UserPreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
