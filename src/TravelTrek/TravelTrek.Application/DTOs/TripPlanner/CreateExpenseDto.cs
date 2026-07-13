namespace TravelTrek.Application.DTOs.TripPlanner;

public record CreateExpenseDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
}
