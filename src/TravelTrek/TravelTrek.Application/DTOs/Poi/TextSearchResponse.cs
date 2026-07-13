using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Osm;

public record TextSearchResponse
{
    [JsonPropertyName("places")]
    public List<PlaceDto>? Places { get; set; }
}