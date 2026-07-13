namespace TravelTrek.Application.DTOs.TripPlanner;

public record SaveTripPlanRequest
{
    public string Prompt { get; set; } = string.Empty;
    
    public string City { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? Duration { get; set; }
    public decimal? Budget { get; set; }
    public string? Currency { get; set; }
    public string? UserCurrency { get; set; }
    public decimal? ConversionRate { get; set; }
    public string? GroupSize { get; set; }
    public string? ImageUrl { get; set; }
    public WeatherSummaryDto? Weather { get; set; }
    public List<DayPlanDto> Days { get; set; } = new();
    public List<string> PackingTips { get; set; } = new();
    public string? GeneralAdvice { get; set; }
}
