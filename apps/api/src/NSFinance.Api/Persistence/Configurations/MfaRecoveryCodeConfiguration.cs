using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class MfaRecoveryCodeConfiguration : IEntityTypeConfiguration<MfaRecoveryCode>
{
    public void Configure(EntityTypeBuilder<MfaRecoveryCode> builder)
    {
        builder.ToTable("MfaRecoveryCodes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();

        builder.HasOne(x => x.TotpAuthenticator)
            .WithMany(x => x.RecoveryCodes)
            .HasForeignKey(x => x.TotpAuthenticatorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CodeHash);
        builder.HasIndex(x => new { x.TotpAuthenticatorId, x.UsedUtc });
    }
}
