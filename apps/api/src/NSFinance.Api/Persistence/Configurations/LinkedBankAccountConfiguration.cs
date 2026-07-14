using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class LinkedBankAccountConfiguration : IEntityTypeConfiguration<LinkedBankAccount>
{
    public void Configure(EntityTypeBuilder<LinkedBankAccount> builder)
    {
        builder.ToTable("LinkedBankAccounts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderAccountId).HasMaxLength(180).IsRequired();
        builder.Property(x => x.AccountType).HasMaxLength(80);
        builder.Property(x => x.AccountSubType).HasMaxLength(80);
        builder.Property(x => x.DisplayName).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.AccountNumberMetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.CurrentConnectionHealth).HasMaxLength(40).IsRequired();
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TransactionSyncCoverageUtc);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.ConnectionId);
        builder.HasIndex(x => new { x.ConnectionId, x.ProviderAccountId }).IsUnique();
        builder.HasIndex(x => x.FinancialAccountId).IsUnique().HasFilter("\"FinancialAccountId\" IS NOT NULL");

        builder.HasOne(x => x.Connection)
            .WithMany(x => x.LinkedAccounts)
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FinancialAccount)
            .WithMany()
            .HasForeignKey(x => x.FinancialAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
