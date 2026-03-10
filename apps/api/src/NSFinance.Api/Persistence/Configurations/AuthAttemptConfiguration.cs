using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class AuthAttemptConfiguration : IEntityTypeConfiguration<AuthAttempt>
{
    public void Configure(EntityTypeBuilder<AuthAttempt> builder)
    {
        builder.ToTable("AuthAttempts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.FailureReason).HasMaxLength(120);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.NormalizedEmail, x.CreatedUtc });
        builder.HasIndex(x => new { x.IpAddress, x.CreatedUtc });
    }
}
