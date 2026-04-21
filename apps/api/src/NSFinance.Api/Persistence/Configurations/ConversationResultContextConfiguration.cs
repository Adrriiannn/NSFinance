using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class ConversationResultContextConfiguration : IEntityTypeConfiguration<ConversationResultContext>
{
    public void Configure(EntityTypeBuilder<ConversationResultContext> builder)
    {
        builder.ToTable("ConversationResultContexts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SnapshotJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.ActiveUntilUtc).IsRequired();
        builder.Property(x => x.ExpiresUtc).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();

        builder.HasIndex(x => new { x.ConversationThreadId, x.CreatedUtc });
        builder.HasIndex(x => new { x.ConversationThreadId, x.ExpiresUtc });

        builder.HasOne(x => x.ConversationThread)
            .WithMany(x => x.ResultContexts)
            .HasForeignKey(x => x.ConversationThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
