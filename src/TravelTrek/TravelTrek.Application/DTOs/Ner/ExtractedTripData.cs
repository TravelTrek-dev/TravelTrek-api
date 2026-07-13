namespace TravelTrek.Application.DTOs.Ner;

public record ExtractedTripData
{
    public List<string> Locations { get; set; } = [];
    public List<string> Dates { get; set; } = [];
    public List<string> Durations { get; set; } = [];
    public List<string> Budgets { get; set; } = [];
    public List<string> GroupSizes { get; set; } = [];
    public List<string> TravelTypes { get; set; } = [];
    public List<string> Activities { get; set; } = [];
}
