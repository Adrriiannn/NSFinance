using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ConversationContextBuildLogConfiguration : IEntityTypeConfiguration<ConversationContextBuildLog>
{
    public void Configure(EntityTypeBuilder<ConversationContextBuildLog> builder)
    {
        builder.ToTable("ConversationContextBuildLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CorrelationId).HasMaxLength(80);
        builder.Property(x => x.TaskType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ModelClass).HasMaxLength(80).IsRequired();
        builder.Property(x => x.TrimReason).HasMaxLength(512);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.ConversationThreadId);
        builder.HasIndex(x => x.ConversationTurnId);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasIndex(x => x.CorrelationId);

        builder.HasOne(x => x.ConversationThread)
            .WithMany(x => x.ContextBuildLogs)
            .HasForeignKey(x => x.ConversationThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ConversationTurn)
            .WithMany(x => x.ContextBuildLogs)
            .HasForeignKey(x => x.ConversationTurnId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
