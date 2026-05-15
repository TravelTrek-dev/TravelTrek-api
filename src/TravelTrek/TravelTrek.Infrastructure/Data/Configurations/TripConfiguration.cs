using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelTrek.Domain.Entities.Trip;

namespace TravelTrek.Infrastructure.Data.Configurations;

public class TripPlanConfiguration : IEntityTypeConfiguration<TripPlan>
{
    public void Configure(EntityTypeBuilder<TripPlan> builder)
    {
        builder.HasKey(t => t.Id);

        builder.OwnsOne(t => t.Weather, weatherBuilder =>
        {
            weatherBuilder.ToJson();
        });

        builder.Property(t => t.PackingTips)
            .HasConversion(
                v => JsonSerializer.Serialize(v, default(JsonSerializerOptions)),
                v => JsonSerializer.Deserialize<List<string>>(v, default(JsonSerializerOptions))!
            )
            .HasColumnType("nvarchar(max)");

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Days)
            .WithOne(d => d.TripPlan)
            .HasForeignKey(d => d.TripPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DayPlanConfiguration : IEntityTypeConfiguration<DayPlan>
{
    public void Configure(EntityTypeBuilder<DayPlan> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasMany(d => d.Activities)
            .WithOne(a => a.DayPlan)
            .HasForeignKey(a => a.DayPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(255);
    }
}