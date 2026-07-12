using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class TotpAuthenticatorConfiguration : IEntityTypeConfiguration<TotpAuthenticator>
{
    public void Configure(EntityTypeBuilder<TotpAuthenticator> builder)
    {
        builder.ToTable("TotpAuthenticators");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.EncryptedSecret).HasColumnType("text").IsRequired();

        builder.HasOne(x => x.User)
            .WithMany(x => x.TotpAuthenticators)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.DisabledUtc });
    }
}
