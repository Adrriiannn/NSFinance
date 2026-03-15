using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpensePlanPublicationReportConfiguration : IEntityTypeConfiguration<ExpensePlanPublicationReport>
{
    public void Configure(EntityTypeBuilder<ExpensePlanPublicationReport> builder)
    {
        builder.ToTable("ExpensePlanPublicationReports");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(48).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(1200);
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.HasIndex(x => new { x.PublicationId, x.ReporterUserId, x.Reason });
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });

        builder.HasOne(x => x.Publication)
            .WithMany(x => x.Reports)
            .HasForeignKey(x => x.PublicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ReporterUser)
            .WithMany(x => x.ExpensePlanPublicationReports)
            .HasForeignKey(x => x.ReporterUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
