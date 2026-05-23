namespace TravelTrek.Domain.Entities.Trip;

public class DayPlan
{
    public Guid Id { get; set; }
    public Guid TripPlanId { get; set; } // fk
    public int DayNumber { get; set; }
    public List<Activity> Activities { get; set; } = new();
    public MealPlan? Meals { get; set; }
    
    public TripPlan TripPlan { get; set; } = null!;
}

public class MealPlan
{
    public string? Breakfast { get; set; }
    public string? Lunch { get; set; }
    public string? Dinner { get; set; }
}