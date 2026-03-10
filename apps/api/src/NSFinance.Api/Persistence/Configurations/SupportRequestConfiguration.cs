using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class SupportRequestConfiguration : IEntityTypeConfiguration<SupportRequest>
{
    public void Configure(EntityTypeBuilder<SupportRequest> builder)
    {
        builder.ToTable("SupportRequests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Category).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Subcategory).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.ScreenshotReference).HasMaxLength(1024);
        builder.Property(x => x.DiagnosticsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasOne(x => x.User)
            .WithMany(x => x.SupportRequests)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.UserId, x.CreatedUtc });
    }
}
