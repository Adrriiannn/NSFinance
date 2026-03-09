using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class PolicyVersionConfiguration : IEntityTypeConfiguration<PolicyVersion>
{
    public void Configure(EntityTypeBuilder<PolicyVersion> builder)
    {
        builder.ToTable("PolicyVersions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ContentReference).HasMaxLength(400).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.IsActive).HasDefaultValue(false);

        builder.HasOne(x => x.PolicyDocument)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.PolicyDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.PolicyDocumentId, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.PolicyDocumentId, x.IsActive });
    }
}
