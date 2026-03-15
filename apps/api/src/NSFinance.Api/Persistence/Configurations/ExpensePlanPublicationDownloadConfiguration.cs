using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpensePlanPublicationDownloadConfiguration : IEntityTypeConfiguration<ExpensePlanPublicationDownload>
{
    public void Configure(EntityTypeBuilder<ExpensePlanPublicationDownload> builder)
    {
        builder.ToTable("ExpensePlanPublicationDownloads");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.HasIndex(x => new { x.PublicationId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });

        builder.HasOne(x => x.Publication)
            .WithMany(x => x.Downloads)
            .HasForeignKey(x => x.PublicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.ExpensePlanPublicationDownloads)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
