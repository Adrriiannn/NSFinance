using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class BankStandingOrderConfiguration : IEntityTypeConfiguration<BankStandingOrder>
{
    public void Configure(EntityTypeBuilder<BankStandingOrder> builder)
    {
        builder.ToTable("BankStandingOrders");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderStandingOrderId).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(80);
        builder.Property(x => x.Frequency).HasMaxLength(80);
        builder.Property(x => x.Reference).HasMaxLength(256);
        builder.Property(x => x.PayeeName).HasMaxLength(180);
        builder.Property(x => x.NextPaymentAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.NextPaymentCurrency).HasMaxLength(3);
        builder.Property(x => x.PayeeAccountMetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.LinkedBankAccountId);
        builder.HasIndex(x => new { x.LinkedBankAccountId, x.ProviderStandingOrderId }).IsUnique();

        builder.HasOne(x => x.LinkedBankAccount)
            .WithMany(x => x.StandingOrders)
            .HasForeignKey(x => x.LinkedBankAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
