using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class BankCardBalanceSnapshotConfiguration : IEntityTypeConfiguration<BankCardBalanceSnapshot>
{
    public void Configure(EntityTypeBuilder<BankCardBalanceSnapshot> builder)
    {
        builder.ToTable("BankCardBalanceSnapshots");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Available).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Current).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Limit).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Outstanding).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CapturedUtc).IsRequired();
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.LinkedBankCardId);
        builder.HasIndex(x => new { x.LinkedBankCardId, x.CapturedUtc });

        builder.HasOne(x => x.LinkedBankCard)
            .WithMany(x => x.BalanceSnapshots)
            .HasForeignKey(x => x.LinkedBankCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
