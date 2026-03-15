using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpensePlanPublicationLikeConfiguration : IEntityTypeConfiguration<ExpensePlanPublicationLike>
{
    public void Configure(EntityTypeBuilder<ExpensePlanPublicationLike> builder)
    {
        builder.ToTable("ExpensePlanPublicationLikes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.HasIndex(x => new { x.PublicationId, x.UserId }).IsUnique();

        builder.HasOne(x => x.Publication)
            .WithMany(x => x.Likes)
            .HasForeignKey(x => x.PublicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.ExpensePlanPublicationLikes)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
