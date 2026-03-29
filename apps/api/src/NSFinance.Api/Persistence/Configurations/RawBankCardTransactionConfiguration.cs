using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class RawBankCardTransactionConfiguration : IEntityTypeConfiguration<RawBankCardTransaction>
{
    public void Configure(EntityTypeBuilder<RawBankCardTransaction> builder)
    {
        builder.ToTable("RawBankCardTransactions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(180);
        builder.Property(x => x.DedupeKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.BookedAtUtc).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512).IsRequired();
        builder.Property(x => x.TransactionType).HasMaxLength(80);
        builder.Property(x => x.TransactionStatus).HasMaxLength(80);
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ImportedUtc).IsRequired();

        builder.HasIndex(x => x.LinkedBankCardId);
        builder.HasIndex(x => new { x.LinkedBankCardId, x.ProviderTransactionId })
            .IsUnique()
            .HasFilter("\"ProviderTransactionId\" IS NOT NULL");
        builder.HasIndex(x => new { x.LinkedBankCardId, x.DedupeKey }).IsUnique();

        builder.HasOne(x => x.LinkedBankCard)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.LinkedBankCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
