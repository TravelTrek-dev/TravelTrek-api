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

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(255);
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Price)
            .HasColumnType("decimal(18,2)");

        builder.HasOne(e => e.TripPlan)
            .WithMany()
            .HasForeignKey(e => e.TripPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}