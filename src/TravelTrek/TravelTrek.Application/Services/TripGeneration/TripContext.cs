using TravelTrek.Application.DTOs.Ner;
using TravelTrek.Application.DTOs.Osm;
using TravelTrek.Application.DTOs.TripPlanner;

namespace TravelTrek.Application.Services.TripGeneration;

internal sealed class TripContext
{
    public required string UserPrompt { get; init; }
    public List<string>? Cities { get; init; }
    public ExtractedTripData? TripData { get; init; }
    public List<PoiDto>? Attractions { get; init; }
    public List<PoiDto>? Dining { get; init; }
    public List<WeatherSummaryDto?>? Weather { get; init; }
}