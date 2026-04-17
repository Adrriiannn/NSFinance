using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class CompanionAIInteractionLogConfiguration : IEntityTypeConfiguration<CompanionAIInteractionLog>
{
    public void Configure(EntityTypeBuilder<CompanionAIInteractionLog> builder)
    {
        builder.ToTable("CompanionAIInteractionLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Intent).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ToolsUsed).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(120).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(160);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.UserId, x.CreatedUtc });
        builder.HasIndex(x => new { x.SessionId, x.CreatedUtc });
        builder.HasIndex(x => x.Intent);
    }
}
