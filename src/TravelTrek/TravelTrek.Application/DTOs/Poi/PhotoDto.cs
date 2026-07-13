using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Osm;

public record PhotoDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}