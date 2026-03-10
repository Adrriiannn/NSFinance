using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TargetEntityType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.TargetEntityId).HasMaxLength(128);
        builder.Property(x => x.EventCategory).HasMaxLength(80).IsRequired();
        builder.Property(x => x.EventName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.EventTimestampUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.SourceChannel).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.EventTimestampUtc);
        builder.HasIndex(x => x.ActorId);
        builder.HasIndex(x => new { x.TargetEntityType, x.TargetEntityId });
        builder.HasIndex(x => x.CorrelationId);
    }
}
