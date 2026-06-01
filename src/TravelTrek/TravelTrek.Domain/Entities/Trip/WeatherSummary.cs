namespace TravelTrek.Domain.Entities.Trip;

public class WeatherSummary
{
    public double AvgTempCelsius { get; set; }
    public string Condition { get; set; } = string.Empty;
    public double AvgHumidity { get; set; }
    public double AvgWindSpeed { get; set; }
}