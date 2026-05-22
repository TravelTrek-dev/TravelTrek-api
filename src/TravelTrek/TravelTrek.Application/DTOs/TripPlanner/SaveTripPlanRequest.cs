namespace TravelTrek.Application.DTOs.TripPlanner;

/// <summary>
/// Input DTO for creating or updating a trip plan.
/// Does not include Id — for creates the server generates it,
/// for updates it comes from the route parameter.
/// </summary>
public class SaveTripPlanRequest
{
    public string City { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? Duration { get; set; }
    public string? Budget { get; set; }
    public string? GroupSize { get; set; }
    public WeatherSummaryDto? Weather { get; set; }
    public List<DayPlanDto> Days { get; set; } = new();
    public List<string> PackingTips { get; set; } = new();
    public string? GeneralAdvice { get; set; }
}
