using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class LinkedBankCardConfiguration : IEntityTypeConfiguration<LinkedBankCard>
{
    public void Configure(EntityTypeBuilder<LinkedBankCard> builder)
    {
        builder.ToTable("LinkedBankCards");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderCardId).HasMaxLength(180).IsRequired();
        builder.Property(x => x.ProviderAccountId).HasMaxLength(180);
        builder.Property(x => x.DisplayName).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CardType).HasMaxLength(80);
        builder.Property(x => x.CardNetwork).HasMaxLength(80);
        builder.Property(x => x.CardNumberLastFour).HasMaxLength(12);
        builder.Property(x => x.NameOnCard).HasMaxLength(180);
        builder.Property(x => x.CurrentConnectionHealth).HasMaxLength(40).IsRequired();
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TransactionSyncCoverageUtc);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.ConnectionId);
        builder.HasIndex(x => new { x.ConnectionId, x.ProviderCardId }).IsUnique();

        builder.HasOne(x => x.Connection)
            .WithMany(x => x.LinkedCards)
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
