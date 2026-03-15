using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpensePlanPublicationConfiguration : IEntityTypeConfiguration<ExpensePlanPublication>
{
    public void Configure(EntityTypeBuilder<ExpensePlanPublication> builder)
    {
        builder.ToTable("ExpensePlanPublications");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatorDisplayNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(x => x.CreatorTagSnapshot).HasMaxLength(90).IsRequired();
        builder.Property(x => x.PublicTitle).HasMaxLength(160).IsRequired();
        builder.Property(x => x.PublicDescription).HasMaxLength(2000);
        builder.Property(x => x.TagsJson).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.PublicationStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ModerationStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ModerationSummary).HasMaxLength(500);
        builder.Property(x => x.PlanSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PlanType).HasMaxLength(24).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ExpectedSpendTotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TrendingScore).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.PublicationStatus, x.PublishedAtUtc });
        builder.HasIndex(x => new { x.PlanType, x.PublicationStatus });
        builder.HasIndex(x => new { x.CreatorUserId, x.CreatedAtUtc });
        builder.HasIndex(x => x.SourcePlanId);

        builder.HasOne(x => x.CreatorUser)
            .WithMany(x => x.ExpensePlanPublications)
            .HasForeignKey(x => x.CreatorUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SourcePlan)
            .WithMany(x => x.Publications)
            .HasForeignKey(x => x.SourcePlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
