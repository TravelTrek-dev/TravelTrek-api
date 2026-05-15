namespace TravelTrek.Domain.Entities.Trip;

public class DayPlan
{
    public Guid Id { get; set; }
    public Guid TripPlanId { get; set; } // fk
    public int DayNumber { get; set; }
    public List<Activity> Activities { get; set; } = new();
    
    
    public TripPlan TripPlan { get; set; } = null!;
}