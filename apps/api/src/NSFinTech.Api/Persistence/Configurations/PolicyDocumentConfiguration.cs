using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class PolicyDocumentConfiguration : IEntityTypeConfiguration<PolicyDocument>
{
    public void Configure(EntityTypeBuilder<PolicyDocument> builder)
    {
        builder.ToTable("PolicyDocuments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.PolicyType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.PolicyType).IsUnique();
    }
}
