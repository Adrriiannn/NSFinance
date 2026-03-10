using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("FinancialAccounts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasOne(x => x.User)
            .WithMany(x => x.FinancialAccounts)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
