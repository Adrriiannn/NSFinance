using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class PolicyAcceptanceConfiguration : IEntityTypeConfiguration<PolicyAcceptance>
{
    public void Configure(EntityTypeBuilder<PolicyAcceptance> builder)
    {
        builder.ToTable("PolicyAcceptances");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.PolicyType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PolicyVersion).HasMaxLength(40).IsRequired();
        builder.Property(x => x.AcceptanceContext).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(40);
        builder.Property(x => x.AppVersion).HasMaxLength(32);

        builder.HasOne(x => x.User)
            .WithMany(x => x.PolicyAcceptances)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PolicyVersionEntity)
            .WithMany(x => x.Acceptances)
            .HasForeignKey(x => x.PolicyVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.PolicyVersionId }).IsUnique();
        builder.HasIndex(x => x.AcceptedUtc);
    }
}
