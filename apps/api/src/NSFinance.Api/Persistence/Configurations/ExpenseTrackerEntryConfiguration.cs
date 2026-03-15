using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpenseTrackerEntryConfiguration : IEntityTypeConfiguration<ExpenseTrackerEntry>
{
    public void Configure(EntityTypeBuilder<ExpenseTrackerEntry> builder)
    {
        builder.ToTable("ExpenseTrackerEntries");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PaymentSource).HasMaxLength(80).IsRequired();
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(1200);
        builder.Property(x => x.TagsJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.Merchant).HasMaxLength(120);
        builder.Property(x => x.LinkedOriginalOffsetAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        builder.HasIndex(x => x.TaxonomySubcategoryId);
        builder.HasIndex(x => x.LinkedOriginalEntryId);

        builder.HasOne(x => x.User)
            .WithMany(x => x.ExpenseTrackerEntries)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.LinkedOriginalEntry)
            .WithMany(x => x.LinkedAdjustments)
            .HasForeignKey(x => x.LinkedOriginalEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
