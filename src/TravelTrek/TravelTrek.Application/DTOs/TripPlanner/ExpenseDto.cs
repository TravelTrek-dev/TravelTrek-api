namespace TravelTrek.Application.DTOs.TripPlanner;

public record ExpenseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
}