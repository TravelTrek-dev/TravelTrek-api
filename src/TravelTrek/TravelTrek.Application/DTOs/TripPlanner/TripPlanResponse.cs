using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.TripPlanner;

/// <summary>
/// The complete trip itinerary returned to the client.
/// </summary>
public class TripPlanResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Id { get; set; }
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

/// <summary>
/// Weather summary for the trip period.
/// </summary>
public class WeatherSummaryDto
{
    public double AvgTempCelsius { get; set; }
    public string Condition { get; set; } = string.Empty;
    public double AvgHumidity { get; set; }
    public double AvgWindSpeed { get; set; }
}

/// <summary>
/// A single day in the itinerary.
/// </summary>
public class DayPlanDto
{
    public int DayNumber { get; set; }
    public List<ActivityDto> Activities { get; set; } = new();
    public MealPlanDto? Meals { get; set; }
}

public class MealPlanDto
{
    public string? Breakfast { get; set; }
    public string? Lunch { get; set; }
    public string? Dinner { get; set; }
}

/// <summary>
/// A single activity / POI visit within a day.
/// </summary>
public class ActivityDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? GoogleMapsLink { get; set; }
    public string? Website { get; set; }
    public string? City { get; set; }
    public string? ApproximateCost { get; set; }
    public string Type { get; set; } = "Activity";
}
