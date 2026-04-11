using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("ConversationMessages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).HasConversion<int>();
        builder.Property(x => x.Content).HasMaxLength(6000).IsRequired();
        builder.Property(x => x.Topic).HasMaxLength(160);
        builder.Property(x => x.ModelUsed).HasMaxLength(80);
        builder.Property(x => x.TaskType).HasMaxLength(80);
        builder.Property(x => x.CorrelationId).HasMaxLength(80);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.WasTrimEligible).HasDefaultValue(true);
        builder.Property(x => x.WasSummaryDerived).HasDefaultValue(false);
        builder.Property(x => x.IsResolved).HasDefaultValue(false);

        builder.HasIndex(x => x.ConversationThreadId);
        builder.HasIndex(x => x.ConversationTurnId);
        builder.HasIndex(x => new { x.ConversationThreadId, x.MessageOrder }).IsUnique();
        builder.HasIndex(x => x.CreatedUtc);

        builder.HasOne(x => x.ConversationThread)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ConversationTurn)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationTurnId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
