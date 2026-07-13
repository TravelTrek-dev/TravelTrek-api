using System.Text.Json.Serialization;

namespace TravelTrek.Application.DTOs.Osm;

public record PlaceDto
{
    [JsonPropertyName("displayName")]
    public DisplayNameDto? DisplayName { get; set; }
    [JsonPropertyName("location")]
    public LocationDto? Location { get; set; }
    [JsonPropertyName("types")]
    public List<string>? Types { get; set; }
    [JsonPropertyName("rating")]
    public double? Rating { get; set; }
    [JsonPropertyName("userRatingCount")]
    public int? UserRatingCount { get; set; }
    [JsonPropertyName("websiteUri")]
    public string? WebsiteUri { get; set; }
    [JsonPropertyName("googleMapsUri")]
    public string? GoogleMapsUri { get; set; }
    [JsonPropertyName("photos")]
    public List<PhotoDto>? Photos { get; set; }
}