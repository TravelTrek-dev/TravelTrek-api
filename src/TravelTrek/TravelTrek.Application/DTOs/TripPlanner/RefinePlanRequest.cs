namespace TravelTrek.Application.DTOs.TripPlanner;

public record RefinePlanRequest
{
    public string UserPrompt { get; set; }
}