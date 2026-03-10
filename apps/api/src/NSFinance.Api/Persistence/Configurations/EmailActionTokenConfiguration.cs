using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class EmailActionTokenConfiguration : IEntityTypeConfiguration<EmailActionToken>
{
    public void Configure(EntityTypeBuilder<EmailActionToken> builder)
    {
        builder.ToTable("EmailActionTokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Purpose).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.RequestedByIp).HasMaxLength(64);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasOne(x => x.User)
            .WithMany(x => x.EmailActionTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Purpose });
    }
}
