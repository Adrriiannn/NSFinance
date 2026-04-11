using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ConversationThreadConfiguration : IEntityTypeConfiguration<ConversationThread>
{
    public void Configure(EntityTypeBuilder<ConversationThread> builder)
    {
        builder.ToTable("ConversationThreads");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(160);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ActiveSummaryVersion).HasDefaultValue(0);
        builder.Property(x => x.StartedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastMessageUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.UserId, x.Status });
        builder.HasIndex(x => x.LastMessageUtc);
        builder.HasIndex(x => x.UpdatedUtc);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
