namespace TravelTrek.Application.DTOs.Osm;

public record PoiDto
{
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Score { get; set; }
    public string GoogleMapsLink { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Wikipedia { get; set; }
    public string? Wikidata { get; set; }
    public string? PhotoUrl { get; set; }
    public double? Rating { get; set; }
}

