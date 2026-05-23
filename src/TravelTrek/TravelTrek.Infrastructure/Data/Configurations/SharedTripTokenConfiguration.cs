using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTrek.Domain.Entities.Trip;

namespace TravelTrek.Infrastructure.Data.Configurations;

public class SharedTripTokenConfiguration : IEntityTypeConfiguration<SharedTripToken>
{
    public void Configure(EntityTypeBuilder<SharedTripToken> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Token)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(s => s.Token)
            .IsUnique();

        builder.Property(s => s.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(s => s.IsRevoked)
            .HasDefaultValue(false);

        builder.Ignore(s => s.IsActive);

        builder.HasOne(s => s.TripPlan)
            .WithMany()
            .HasForeignKey(s => s.TripPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
