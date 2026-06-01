using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTrek.Domain.Entities.Trip;

namespace TravelTrek.Infrastructure.Data.Configurations;

public class DayPlanConfiguration : IEntityTypeConfiguration<DayPlan>
{
    public void Configure(EntityTypeBuilder<DayPlan> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasMany(d => d.Activities)
            .WithOne(a => a.DayPlan)
            .HasForeignKey(a => a.DayPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(d => d.Meals);
    }
}