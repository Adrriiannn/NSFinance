using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ConversationSummaryConfiguration : IEntityTypeConfiguration<ConversationSummary>
{
    public void Configure(EntityTypeBuilder<ConversationSummary> builder)
    {
        builder.ToTable("ConversationSummaries");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.SummaryText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SummaryScope).HasConversion<int>();
        builder.Property(x => x.SummaryVersion).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.ConversationThreadId);
        builder.HasIndex(x => new { x.ConversationThreadId, x.SummaryVersion }).IsUnique();
        builder.HasIndex(x => x.CreatedUtc);

        builder.HasOne(x => x.ConversationThread)
            .WithMany(x => x.Summaries)
            .HasForeignKey(x => x.ConversationThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
