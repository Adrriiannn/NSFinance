using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpensePlanPublicationModerationEventConfiguration : IEntityTypeConfiguration<ExpensePlanPublicationModerationEvent>
{
    public void Configure(EntityTypeBuilder<ExpensePlanPublicationModerationEvent> builder)
    {
        builder.ToTable("ExpensePlanPublicationModerationEvents");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TriggerType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResultStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        builder.Property(x => x.MatchedRulesJson).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.HasIndex(x => new { x.PublicationId, x.CreatedAtUtc });

        builder.HasOne(x => x.Publication)
            .WithMany(x => x.ModerationEvents)
            .HasForeignKey(x => x.PublicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
