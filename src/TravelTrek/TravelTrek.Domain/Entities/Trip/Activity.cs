namespace TravelTrek.Domain.Entities.Trip;

public class Activity
{
    public Guid Id { get; set; }
    public Guid DayPlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? GoogleMapsLink { get; set; }
    public string? Website { get; set; }
    public string? City { get; set; }
    public string Type { get; set; } = "Activity";
    
    public DayPlan DayPlan { get; set; } = null!;
}