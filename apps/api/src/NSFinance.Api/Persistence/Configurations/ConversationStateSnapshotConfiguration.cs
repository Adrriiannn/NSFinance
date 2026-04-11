using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ConversationStateSnapshotConfiguration : IEntityTypeConfiguration<ConversationStateSnapshot>
{
    public void Configure(EntityTypeBuilder<ConversationStateSnapshot> builder)
    {
        builder.ToTable("ConversationStateSnapshots");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.StateJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.StateVersion).IsRequired();
        builder.Property(x => x.Reason).HasConversion<int>();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.ConversationThreadId);
        builder.HasIndex(x => new { x.ConversationThreadId, x.StateVersion }).IsUnique();
        builder.HasIndex(x => x.CreatedUtc);

        builder.HasOne(x => x.ConversationThread)
            .WithMany(x => x.StateSnapshots)
            .HasForeignKey(x => x.ConversationThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
