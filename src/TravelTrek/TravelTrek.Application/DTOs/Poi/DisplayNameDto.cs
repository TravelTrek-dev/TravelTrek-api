using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Osm;

public record DisplayNameDto
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}