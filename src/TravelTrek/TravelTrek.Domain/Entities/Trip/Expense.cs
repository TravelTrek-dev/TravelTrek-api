namespace TravelTrek.Domain.Entities.Trip;

public class Expense
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public Guid TripPlanId { get; set; }

    public TripPlan TripPlan { get; set; }
    
    public Guid UserId { get; set; }
    public User User { get; set; }
    
    
}