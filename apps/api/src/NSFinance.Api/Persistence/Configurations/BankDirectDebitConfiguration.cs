using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class BankDirectDebitConfiguration : IEntityTypeConfiguration<BankDirectDebit>
{
    public void Configure(EntityTypeBuilder<BankDirectDebit> builder)
    {
        builder.ToTable("BankDirectDebits");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderDirectDebitId).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(80);
        builder.Property(x => x.MandateType).HasMaxLength(80);
        builder.Property(x => x.Reference).HasMaxLength(256);
        builder.Property(x => x.MerchantName).HasMaxLength(180);
        builder.Property(x => x.PreviousPaymentAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.PreviousPaymentCurrency).HasMaxLength(3);
        builder.Property(x => x.NextPaymentAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.NextPaymentCurrency).HasMaxLength(3);
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.LinkedBankAccountId);
        builder.HasIndex(x => new { x.LinkedBankAccountId, x.ProviderDirectDebitId }).IsUnique();

        builder.HasOne(x => x.LinkedBankAccount)
            .WithMany(x => x.DirectDebits)
            .HasForeignKey(x => x.LinkedBankAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
