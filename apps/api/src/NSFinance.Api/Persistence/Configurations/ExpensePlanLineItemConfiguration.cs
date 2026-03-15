using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpensePlanLineItemConfiguration : IEntityTypeConfiguration<ExpensePlanLineItem>
{
    public void Configure(EntityTypeBuilder<ExpensePlanLineItem> builder)
    {
        builder.ToTable("ExpensePlanLineItems");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(x => x.HierarchyPathSnapshot).HasMaxLength(320).IsRequired();
        builder.Property(x => x.ExpectedAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Notes).HasMaxLength(800);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.PlanId, x.SortOrder });
        builder.HasIndex(x => x.TaxonomySubcategoryId);

        builder.HasOne(x => x.Plan)
            .WithMany(x => x.LineItems)
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
