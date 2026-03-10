using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class BankBalanceSnapshotConfiguration : IEntityTypeConfiguration<BankBalanceSnapshot>
{
    public void Configure(EntityTypeBuilder<BankBalanceSnapshot> builder)
    {
        builder.ToTable("BankBalanceSnapshots");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Available).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Current).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Overdraft).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CapturedUtc).IsRequired();
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.LinkedBankAccountId);
        builder.HasIndex(x => new { x.LinkedBankAccountId, x.CapturedUtc });

        builder.HasOne(x => x.LinkedBankAccount)
            .WithMany(x => x.BalanceSnapshots)
            .HasForeignKey(x => x.LinkedBankAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
