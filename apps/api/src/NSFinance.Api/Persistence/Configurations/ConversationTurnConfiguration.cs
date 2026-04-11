using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ConversationTurnConfiguration : IEntityTypeConfiguration<ConversationTurn>
{
    public void Configure(EntityTypeBuilder<ConversationTurn> builder)
    {
        builder.ToTable("ConversationTurns");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ClientRequestId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TaskType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ModelClass).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ModelUsed).HasMaxLength(80);
        builder.Property(x => x.ModelDeployment).HasMaxLength(120);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ContextSource).HasMaxLength(32).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(80);
        builder.Property(x => x.FailureReason).HasMaxLength(512);
        builder.Property(x => x.AttemptCount).HasDefaultValue(1);
        builder.Property(x => x.WasDeduplicated).HasDefaultValue(false);
        builder.Property(x => x.StartedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.ConversationThreadId);
        builder.HasIndex(x => new { x.ConversationThreadId, x.ClientRequestId }).IsUnique();
        builder.HasIndex(x => new { x.ConversationThreadId, x.Status });
        builder.HasIndex(x => x.UpdatedUtc);

        builder.HasOne(x => x.ConversationThread)
            .WithMany(x => x.Turns)
            .HasForeignKey(x => x.ConversationThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
