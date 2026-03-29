using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class BankConnectionIdentityInfoConfiguration : IEntityTypeConfiguration<BankConnectionIdentityInfo>
{
    public void Configure(EntityTypeBuilder<BankConnectionIdentityInfo> builder)
    {
        builder.ToTable("BankConnectionIdentityInfos");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).HasMaxLength(180);
        builder.Property(x => x.Email).HasMaxLength(180);
        builder.Property(x => x.Phone).HasMaxLength(80);
        builder.Property(x => x.DateOfBirth).HasMaxLength(40);
        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FetchedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.ConnectionId).IsUnique();

        builder.HasOne(x => x.Connection)
            .WithOne(x => x.IdentityInfo)
            .HasForeignKey<BankConnectionIdentityInfo>(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
