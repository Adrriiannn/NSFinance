using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("ConsentRecords");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConsentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(40).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasOne(x => x.User)
            .WithMany(x => x.ConsentRecords)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.ConsentType }).IsUnique();
    }
}
