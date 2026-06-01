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

        builder.Property(t => t.Budget)
            .HasColumnType("decimal(18,2)");

        builder.Property(t => t.Currency)
            .HasMaxLength(10);

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