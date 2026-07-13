using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Osm;

public record LocationDto
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}