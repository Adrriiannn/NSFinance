using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExportRequestConfiguration : IEntityTypeConfiguration<ExportRequest>
{
    public void Configure(EntityTypeBuilder<ExportRequest> builder)
    {
        builder.ToTable("ExportRequests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.ArtifactReference).HasMaxLength(512);
        builder.Property(x => x.RequestedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasOne(x => x.User)
            .WithMany(x => x.ExportRequests)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.RequestedUtc });
    }
}
