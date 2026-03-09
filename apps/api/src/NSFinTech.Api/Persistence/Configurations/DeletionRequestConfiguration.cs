using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class DeletionRequestConfiguration : IEntityTypeConfiguration<DeletionRequest>
{
    public void Configure(EntityTypeBuilder<DeletionRequest> builder)
    {
        builder.ToTable("DeletionRequests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.RequestedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasOne(x => x.User)
            .WithMany(x => x.DeletionRequests)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.RequestedUtc });
    }
}
