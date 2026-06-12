namespace TravelTrek.Domain.Entities.Trip;

public class TripPlan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; } // fk
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
    public WeatherSummary? Weather { get; set; }    
    public List<DayPlan> Days { get; set; } = new();
    
    public List<string> PackingTips { get; set; } = new();
    public string? GeneralAdvice { get; set; }

    public User User { get; set; } = null!;
}